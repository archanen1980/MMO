using System;
using System.Collections.Generic;
using UnityEngine;
using MMO.Shared.Item;   // ItemDef

namespace MMO.Loot
{
    /// <summary>
    /// ScriptableObject loot table used by LootSource (Table mode).
    /// Each row supports a per-item cap (maxPerBag).
    /// Optional default total-items cap for bags that use this table.
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable", menuName = "MMO/Loot/LootTable")]
    public class LootTable : ScriptableObject
    {
        [Serializable]
        public class Row
        {
            public ItemDef def;
            [Min(1)] public int minAmount = 1;
            [Min(1)] public int maxAmount = 1;

            [Range(0, 1f)] public float chance = 0f; // for IndependentChance
            [Min(0f)] public float weight = 0f;      // for WeightedOne

            [Tooltip("Cap for this item per bag when this table is used. 0 = unlimited.")]
            [Min(0)] public int maxPerBag = 0;
        }

        [SerializeField] List<Row> rows = new();

        [Header("Defaults")]
        [Tooltip("Default total items cap when a LootSource uses this table and its own cap is 0. 0 = unlimited.")]
        [Min(0)][SerializeField] int defaultTotalItemsMax = 0;
        public int DefaultTotalItemsMax => defaultTotalItemsMax;

        public struct LootRoll
        {
            public ItemDef def;
            public int amount;
        }

        public bool HasAny()
        {
            if (rows == null || rows.Count == 0) return false;
            foreach (var r in rows)
                if (r != null && r.def) return true;
            return false;
        }

        /// <summary>Roll this table per the given selection mode.</summary>
        public List<LootRoll> Roll(LootSource.SelectionMode selection)
        {
            var picks = new List<LootRoll>(4);
            if (rows == null || rows.Count == 0) return picks;

            switch (selection)
            {
                case LootSource.SelectionMode.WeightedOne:
                    {
                        float sum = 0f;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            var r = rows[i];
                            if (r == null || !r.def) continue;
                            float w = (r.weight > 0f) ? r.weight : (r.chance > 0f ? r.chance : 0f);
                            sum += Mathf.Max(0f, w);
                        }
                        if (sum <= 0f) return picks;

                        float roll = UnityEngine.Random.value * sum;
                        float acc = 0f;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            var r = rows[i];
                            if (r == null || !r.def) continue;
                            float w = (r.weight > 0f) ? r.weight : (r.chance > 0f ? r.chance : 0f);
                            if (w <= 0f) continue;

                            acc += w;
                            if (roll <= acc)
                            {
                                int amt = UnityEngine.Random.Range(r.minAmount, r.maxAmount + 1);
                                picks.Add(new LootRoll { def = r.def, amount = Mathf.Max(1, amt) });
                                break;
                            }
                        }
                    }
                    break;

                case LootSource.SelectionMode.IndependentChance:
                    {
                        float sumWeight = 0f;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            var r = rows[i];
                            if (r == null || !r.def) continue;
                            if (r.weight > 0f) sumWeight += r.weight;
                        }

                        for (int i = 0; i < rows.Count; i++)
                        {
                            var r = rows[i];
                            if (r == null || !r.def) continue;

                            float p = r.chance;
                            if (p <= 0f && r.weight > 0f && sumWeight > 0f)
                                p = Mathf.Clamp01(r.weight / sumWeight); // fallback prob

                            if (p <= 0f) continue;

                            if (UnityEngine.Random.value <= p)
                            {
                                int amt = UnityEngine.Random.Range(r.minAmount, r.maxAmount + 1);
                                picks.Add(new LootRoll { def = r.def, amount = Mathf.Max(1, amt) });
                            }
                        }
                    }
                    break;
            }

            return picks;
        }

        /// <summary>
        /// Return the cap for this item in this table (0 = unlimited).
        /// If the item appears multiple times, the first non-zero cap found is returned;
        /// otherwise the highest cap found (or 0 if none).
        /// </summary>
        public int ResolveCapFor(ItemDef def)
        {
            if (!def || rows == null || rows.Count == 0) return 0;

            int best = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r != null && r.def == def)
                {
                    if (r.maxPerBag > 0) return r.maxPerBag; // first explicit cap wins
                    best = Mathf.Max(best, r.maxPerBag);
                }
            }
            return best; // 0 means unlimited
        }
    }
}
