using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mirror;
using MMO.Shared.Item;

namespace MMO.Inventory
{
    /// <summary>
    /// Server-only developer seeder for quickly populating a player's backpack.
    /// - Accepts ItemDef, string itemId, or legacy numeric id (converted to string).
    /// - Uses PlayerInventory.ServerAdd(...) for stacking rules.
    /// - Checks (optional) empty-backpack condition and optional clear-before-seed.
    /// </summary>
    public class DevInventorySeeder : NetworkBehaviour
    {
        [Header("Target")]
        [Tooltip("PlayerInventory to seed. If empty, tries to resolve from the connection's player or the scene.")]
        public PlayerInventory target;

        [Tooltip("Optional: explicit lookup (e.g., ResourcesItemLookup). If null, uses target.itemLookup then Resources.")]
        public UnityEngine.Object itemLookupOverride;

        [Header("Behavior")]
        public bool seedOnServerStart = true;
        [Tooltip("Only seed if backpack is currently empty.")]
        public bool onlyIfEmpty = true;
        [Tooltip("Clear backpack before seeding.")]
        public bool clearBackpackFirst = false;
        static bool s_seededThisSession;
        
        [Header("Items to Seed")]
        public SeedEntry[] items =
        {
            new SeedEntry { itemId = "log",   amount = 5 },
            new SeedEntry { itemId = "gold",  amount = 100 },
            new SeedEntry { itemId = "sword", amount = 1 },
        };

        [Serializable]
        public class SeedEntry
        {
            [Tooltip("Preferred: direct ItemDef reference.")]
            public ItemDef def;

            [Tooltip("ItemDef.itemId (string). Use the dropdown to pick from all ItemDefs.")]
            [ItemId] public string itemId;

            [Tooltip("Legacy numeric ID. Used only if both 'def' and 'itemId' are empty.")]
            public int legacyNumericId;

            [Min(1)] public int amount = 1;
        }

        // ----- Lifecycle -----
        public override void OnStartServer()
        {
            base.OnStartServer();
            if (seedOnServerStart) TrySeed();
        }

#if UNITY_EDITOR
        [ContextMenu("DEV/Seed Now (Server)")]
        void DevSeedNow()
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[DevInventorySeeder] Not running as server — skipped.");
                return;
            }
            TrySeed();
        }
#endif

        // ----- Seeding -----
        [Server]
        void TrySeed()
        {
            var inv = ResolveTargetInventory();
            if (inv == null)
            {
                Debug.LogWarning("[DevInventorySeeder] No PlayerInventory found to seed.");
                return;
            }

            if (onlyIfEmpty && !IsBackpackEmpty(inv))
            {
                Debug.Log("[DevInventorySeeder] Backpack not empty — skipping.");
                return;
            }

            if (clearBackpackFirst)
                ClearBackpack(inv);

            int addedTotal = 0;

            foreach (var e in items ?? Array.Empty<SeedEntry>())
            {
                if (e == null || e.amount <= 0) continue;

                ItemDef def = e.def;
                string id = (e.itemId ?? "").Trim();

                if (def == null && !string.IsNullOrEmpty(id))
                    def = ResolveDef(inv, id);

                if (def == null && string.IsNullOrEmpty(id) && e.legacyNumericId > 0)
                    id = e.legacyNumericId.ToString();

                if (def == null && !string.IsNullOrEmpty(id))
                    def = ResolveDef(inv, id);

                if (def != null)
                {
                    int added = inv.ServerAdd(def, e.amount);
                    addedTotal += added;
                    Debug.Log($"[DevInventorySeeder] +{added} '{def.itemId}' ({def.displayName})");
                }
                else if (!string.IsNullOrEmpty(id))
                {
                    int added = inv.ServerAdd(id, e.amount);
                    addedTotal += added;
                    if (added > 0)
                        Debug.Log($"[DevInventorySeeder] +{added} '{id}' (by itemId)");
                    else
                        Debug.LogWarning($"[DevInventorySeeder] ItemDef not found for id='{id}'. " +
                                         "Make sure an ItemDef with this itemId exists (lookup or Resources/Items).");
                }
                else
                {
                    Debug.LogWarning("[DevInventorySeeder] Skipped entry — no def, itemId, or legacy id.");
                }
            }

            Debug.Log($"[DevInventorySeeder] Done. Total added: {addedTotal}");
        }

        // ----- Helpers -----
        [Server]
        PlayerInventory ResolveTargetInventory()
        {
            if (target != null) return target;

            if (connectionToClient != null && connectionToClient.identity != null)
            {
                target = connectionToClient.identity.GetComponent<PlayerInventory>();
                if (target != null) return target;
            }

            target = FindObjectOfType<PlayerInventory>();
            return target;
        }

        ItemDef ResolveDef(PlayerInventory inv, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            if (itemLookupOverride != null)
            {
                var hit = LookupTry(itemLookupOverride, itemId);
                if (hit != null) return hit;
            }

            if (inv != null && inv.itemLookup != null)
            {
                var hit = LookupTry(inv.itemLookup, itemId);
                if (hit != null) return hit;
            }

            var direct = Resources.Load<ItemDef>($"Items/{itemId}");
            if (direct) return direct;

            var all = Resources.LoadAll<ItemDef>("Items");
            return all.FirstOrDefault(d => d && string.Equals(d.itemId, itemId, StringComparison.OrdinalIgnoreCase));
        }

        static ItemDef LookupTry(UnityEngine.Object lookup, string itemId)
        {
            if (lookup == null) return null;
            var t = lookup.GetType();

            var mTry = t.GetMethod("TryGetById", new[] { typeof(string), typeof(ItemDef).MakeByRefType() });
            if (mTry != null)
            {
                object[] args = new object[] { itemId, null };
                bool ok = (bool)mTry.Invoke(lookup, args);
                if (ok) return (ItemDef)args[1];
            }

            var mGet = t.GetMethod("GetByIdOrNull", new[] { typeof(string) });
            if (mGet != null)
            {
                var res = mGet.Invoke(lookup, new object[] { itemId }) as ItemDef;
                if (res != null) return res;
            }

            return null;
        }

        [Server]
        bool IsBackpackEmpty(PlayerInventory inv)
        {
            var list = inv.Backpack;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (!SlotAccess.IsEmpty(s)) return false;
            }
            return true;
        }

        [Server]
        void ClearBackpack(PlayerInventory inv)
        {
            for (int i = 0; i < inv.Backpack.Count; i++)
                inv.Backpack[i] = default;
        }

        // ---- Light reflection helpers (safe Convert) ----
        static class SlotAccess
        {
            static readonly FieldInfo fId, fAmt;
            static readonly PropertyInfo pId, pAmt;

            static SlotAccess()
            {
                var t = typeof(InvSlot);
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                fId = t.GetField("itemId", BF) ?? t.GetField("ItemId", BF) ??
                      t.GetField("id", BF) ?? t.GetField("Id", BF) ??
                      t.GetField("itemID", BF) ?? t.GetField("ItemID", BF);
                pId = t.GetProperty("itemId", BF) ?? t.GetProperty("ItemId", BF) ??
                      t.GetProperty("id", BF) ?? t.GetProperty("Id", BF) ??
                      t.GetProperty("itemID", BF) ?? t.GetProperty("ItemID", BF);

                fAmt = t.GetField("amount", BF) ?? t.GetField("Amount", BF) ??
                       t.GetField("count", BF) ?? t.GetField("Count", BF) ??
                       t.GetField("stack", BF) ?? t.GetField("Stack", BF) ??
                       t.GetField("quantity", BF) ?? t.GetField("Quantity", BF);
                pAmt = t.GetProperty("amount", BF) ?? t.GetProperty("Amount", BF) ??
                       t.GetProperty("count", BF) ?? t.GetProperty("Count", BF) ??
                       t.GetProperty("stack", BF) ?? t.GetProperty("Stack", BF) ??
                       t.GetProperty("quantity", BF) ?? t.GetProperty("Quantity", BF);
            }

            public static bool IsEmpty(InvSlot s)
            {
                var id = GetId(s);
                var a = GetAmt(s);
                return string.IsNullOrEmpty(id) || a <= 0;
            }

            static string GetId(InvSlot s)
            {
                if (fId != null) { var v = fId.GetValue(s); return v != null ? Convert.ToString(v) : null; }
                if (pId != null) { var v = pId.GetValue(s); return v != null ? Convert.ToString(v) : null; }
                return null;
            }

            static int GetAmt(InvSlot s)
            {
                if (fAmt != null) { var v = fAmt.GetValue(s); return v != null ? Convert.ToInt32(v) : 0; }
                if (pAmt != null) { var v = pAmt.GetValue(s); return v != null ? Convert.ToInt32(v) : 0; }
                return 0;
            }
        }
    }
}
