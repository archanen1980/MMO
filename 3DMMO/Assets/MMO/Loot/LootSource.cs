using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using MMO.Shared.Item;   // ItemDef
using MMO.Inventory;    // PlayerInventory (adjust if yours lives elsewhere)

namespace MMO.Loot
{
    /// <summary>
    /// Interactable, networked loot source that can draw from:
    ///  - INLINE entries, or
    ///  - a LootTable ScriptableObject.
    ///
    /// Selection:
    ///  - WeightedOne: pick exactly 1 entry by 'weight' (fallback to 'chance' if weight==0).
    ///  - IndependentChance: iterate entries, each rolls by 'chance' (fallback to normalized weight if chance==0).
    ///
    /// Caps:
    ///  - Per-item caps: entry.maxPerBag (0 = unlimited). For tables, taken from table rows.
    ///  - Auto-cap non-stackables (def.maxStack <= 1) to 1 when no explicit cap set (optional).
    ///  - Total items cap: sum of quantities across ALL items after per-item caps.
    ///    Set per bag on this component; if 0, optional fallback to table’s default cap.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public class LootSource : NetworkBehaviour, ILootable
    {
        public enum SourceMode { Inline, Table }
        public enum SelectionMode { WeightedOne, IndependentChance }

        [Header("Mode")]
        [SerializeField] SourceMode mode = SourceMode.Inline;

        [Tooltip("Used if Mode = Table")]
        [SerializeField] LootTable lootTable;

        [Tooltip("Used if Mode = Inline")]
        [SerializeField] List<Entry> entries = new();

        [Header("Selection")]
        [Tooltip("WeightedOne = pick 1 by weight (fallback to chance). IndependentChance = each entry rolls chance (fallback to normalized weight).")]
        [SerializeField] SelectionMode selection = SelectionMode.WeightedOne;

        [Header("Caps")]
        [Tooltip("If true, items with maxStack <= 1 auto-cap to 1 when no explicit cap is set on the entry.")]
        [SerializeField] bool autoCapNonStackables = true;

        [Tooltip("Total items cap for THIS bag. 0 = unlimited. Counts duplicates (e.g., Sword x2 counts as 2). If 0 and Mode=Table, will fallback to the table's default total cap if set.")]
        [Min(0)][SerializeField] int totalItemsMax = 0;

        [Header("Lifecycle")]
        [Tooltip("Destroy the loot object on successful loot.")]
        [SerializeField] bool destroyOnLoot = true;

        [Tooltip("If not destroyed, mark as unavailable after loot.")]
        [SerializeField] bool markUnavailableAfterLoot = true;

        [Header("Debug")]
        [SerializeField] bool logServer = false;

        bool _available = true;

        // --------------- Data structures ---------------

        [Serializable]
        public class Entry
        {
            public ItemDef def;
            [Min(1)] public int minAmount = 1;
            [Min(1)] public int maxAmount = 1;

            [Range(0, 1f)] public float chance = 0f; // for IndependentChance
            [Min(0f)] public float weight = 0f;      // for WeightedOne

            [Tooltip("Cap for this item per bag. 0 = unlimited.")]
            [Min(0)] public int maxPerBag = 0;
        }

        struct Pick { public ItemDef def; public int amount; }

        // --------------- ILootable ---------------

        public bool IsAvailable
        {
            get
            {
                if (!_available) return false;
                return HasAnyContent();
            }
        }

        public Transform GetTransform() => transform;

        [Server]
        public bool ServerTryLoot(GameObject looter)
        {
            if (!IsAvailable || !looter) return false;

            var inv = looter.GetComponent<PlayerInventory>();
            if (!inv)
            {
                if (logServer) Debug.LogWarning($"[LootSource] No PlayerInventory on looter '{looter.name}'.");
                return false;
            }

            // 1) Roll raw picks
            var raw = RollPicks();

            // 2) Aggregate + apply per-item caps (preserve first-seen order)
            var perItemCapped = ApplyPerItemCaps(raw);

            // 3) Apply TOTAL items cap (bag-level)
            int cap = totalItemsMax;
            if (cap <= 0 && mode == SourceMode.Table && lootTable)
                cap = lootTable.DefaultTotalItemsMax;

            var finalPicks = ApplyTotalItemsCap(perItemCapped, cap);

            // 4) Award (use ServerAwardLoot so toasts/chat fire)
            int totalAdded = 0;
            foreach (var p in finalPicks)
                totalAdded += inv.ServerAwardLoot(p.def, Mathf.Max(1, p.amount));

            if (logServer)
                Debug.Log($"[LootSource] Awarded {finalPicks.Count} item types, totalUnits={SumUnits(finalPicks)}, totalAdded={totalAdded}");

            // 5) Consume
            if (markUnavailableAfterLoot) _available = false;
            if (destroyOnLoot && NetworkServer.active) NetworkServer.Destroy(gameObject);
            return true;
        }

        // --------------- Rolling ---------------

        bool HasAnyContent()
        {
            switch (mode)
            {
                case SourceMode.Table:
                    return lootTable && lootTable.HasAny();
                case SourceMode.Inline:
                    if (entries == null || entries.Count == 0) return false;
                    foreach (var e in entries)
                        if (e != null && e.def) return true;
                    return false;
                default:
                    return false;
            }
        }

        List<Pick> RollPicks()
        {
            var picks = new List<Pick>(4);

            if (mode == SourceMode.Table)
            {
                if (!lootTable) return picks;

                var ltPicks = lootTable.Roll(selection);
                foreach (var lp in ltPicks)
                {
                    if (!lp.def) continue;
                    picks.Add(new Pick { def = lp.def, amount = Mathf.Max(1, lp.amount) });
                }
                return picks;
            }

            // Inline entries
            if (entries == null || entries.Count == 0) return picks;

            switch (selection)
            {
                case SelectionMode.WeightedOne:
                    {
                        // Effective weight = weight if >0 else (chance if >0) else 0
                        float sum = 0f;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            if (e == null || !e.def) continue;
                            float w = (e.weight > 0f) ? e.weight : (e.chance > 0f ? e.chance : 0f);
                            sum += Mathf.Max(0f, w);
                        }

                        if (sum <= 0f) return picks;

                        float r = UnityEngine.Random.value * sum;
                        float acc = 0f;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            if (e == null || !e.def) continue;
                            float w = (e.weight > 0f) ? e.weight : (e.chance > 0f ? e.chance : 0f);
                            if (w <= 0f) continue;

                            acc += w;
                            if (r <= acc)
                            {
                                int amt = UnityEngine.Random.Range(e.minAmount, e.maxAmount + 1);
                                picks.Add(new Pick { def = e.def, amount = Mathf.Max(1, amt) });
                                break;
                            }
                        }
                    }
                    break;

                case SelectionMode.IndependentChance:
                    {
                        // For entries with chance==0, fallback to normalized weight
                        float sumWeight = 0f;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            if (e == null || !e.def) continue;
                            if (e.weight > 0f) sumWeight += e.weight;
                        }

                        for (int i = 0; i < entries.Count; i++)
                        {
                            var e = entries[i];
                            if (e == null || !e.def) continue;

                            float p = e.chance;
                            if (p <= 0f && e.weight > 0f && sumWeight > 0f)
                                p = Mathf.Clamp01(e.weight / sumWeight); // fallback prob

                            if (p <= 0f) continue;

                            if (UnityEngine.Random.value <= p)
                            {
                                int amt = UnityEngine.Random.Range(e.minAmount, e.maxAmount + 1);
                                picks.Add(new Pick { def = e.def, amount = Mathf.Max(1, amt) });
                            }
                        }
                    }
                    break;
            }

            return picks;
        }

        // --------------- Caps / Aggregation ---------------

        List<Pick> ApplyPerItemCaps(List<Pick> raw)
        {
            // Aggregate by item, preserving first-seen order
            var order = new List<ItemDef>(raw.Count);
            var sumByDef = new Dictionary<ItemDef, int>();

            foreach (var p in raw)
            {
                if (!p.def) continue;
                if (!sumByDef.ContainsKey(p.def))
                {
                    sumByDef[p.def] = 0;
                    order.Add(p.def);
                }
                sumByDef[p.def] += Mathf.Max(1, p.amount);
            }

            var result = new List<Pick>(order.Count);
            foreach (var def in order)
            {
                int total = sumByDef[def];
                int cap = ResolveCapFor(def);
                int final = Mathf.Min(total, cap);
                if (final > 0)
                    result.Add(new Pick { def = def, amount = final });
            }
            return result;
        }

        int ResolveCapFor(ItemDef def)
        {
            int cap = 0;

            if (mode == SourceMode.Inline)
            {
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        if (e != null && e.def == def)
                        {
                            cap = e.maxPerBag;
                            if (cap > 0) break; // first explicit cap wins
                        }
                    }
                }
            }
            else if (mode == SourceMode.Table && lootTable)
            {
                cap = lootTable.ResolveCapFor(def);
            }

            if (cap <= 0 && autoCapNonStackables && def && def.maxStack <= 1)
                cap = 1;

            if (cap <= 0) cap = int.MaxValue;
            return cap;
        }

        List<Pick> ApplyTotalItemsCap(List<Pick> picks, int cap)
        {
            if (cap <= 0) return picks;

            int remaining = cap;
            var result = new List<Pick>(picks.Count);

            // Greedy: keep first-seen order, trim amounts as needed
            foreach (var p in picks)
            {
                if (remaining <= 0) break;
                int give = Mathf.Min(p.amount, remaining);
                if (give > 0)
                    result.Add(new Pick { def = p.def, amount = give });
                remaining -= give;
            }
            return result;
        }

        int SumUnits(List<Pick> picks)
        {
            int s = 0;
            foreach (var p in picks) s += Mathf.Max(0, p.amount);
            return s;
        }
    }
}
