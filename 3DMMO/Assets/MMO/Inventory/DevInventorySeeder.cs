using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEngine;
using MMO.Shared.Item;   // your ItemDef

namespace MMO.Inventory
{
    /// <summary>
    /// Attach this to your Player prefab (or a child). Seeds items on the SERVER after spawn.
    /// Robust: waits for ResourcesItemLookup + sized inventory. Supports string/int itemIds.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory))]
    public class DevInventorySeeder : NetworkBehaviour
    {
        [Serializable]
        public class Grant
        {
            [Tooltip("Item id to give. Accepts '3' or 3. Will be parsed to int.")]
            public string itemId = "1";
            [Tooltip("How many to give.")]
            public ushort count = 1;
        }

        [Header("Seeder")]
        [Tooltip("Run seeding only inside the Unity Editor.")]
        public bool editorOnly = true;

        [Tooltip("Clear backpack before seeding.")]
        public bool clearBackpackFirst = true;

        [Tooltip("Items to grant on server start for this player.")]
        public List<Grant> grants = new List<Grant>
        {
            new Grant { itemId = "1", count = 1 },
            new Grant { itemId = "2", count = 5 },
            // new Grant { itemId = "3", count = 1 },
        };

        PlayerInventory inv;

        public override void OnStartServer()
        {
            base.OnStartServer();
#if !UNITY_EDITOR
            if (editorOnly) return;
#endif
            inv = GetComponent<PlayerInventory>();
            StartCoroutine(SeedRoutine());
        }

        IEnumerator SeedRoutine()
        {
            // 1) Wait for lookup to exist
            float t0 = Time.time;
            while (ResourcesItemLookup.Instance == null && Time.time - t0 < 5f)
                yield return null;

            var lookup = ResourcesItemLookup.Instance;
            if (lookup == null)
            {
                Debug.LogWarning("[DevInventorySeeder] No ResourcesItemLookup in scene; abort seeding.");
                yield break;
            }

            // 2) Wait for inventory lists to be sized by server (OnStartServer in PlayerInventory)
            t0 = Time.time;
            while ((inv.Backpack.Count == 0 || inv.Equipment.Count == 0) && Time.time - t0 < 5f)
                yield return null;

            if (inv.Backpack.Count == 0)
            {
                Debug.LogWarning("[DevInventorySeeder] Backpack has 0 slots; abort seeding.");
                yield break;
            }

            // 3) Log what the lookup indexed (count only, light)
            Debug.Log($"[DevInventorySeeder] Lookup ready. Known item count: {lookup.CountIndexed}");

            // 4) (Optional) clear backpack
            if (clearBackpackFirst)
            {
                for (int i = 0; i < inv.Backpack.Count; i++)
                {
                    var s = inv.Backpack[i];
                    s.Clear();
                    inv.Backpack[i] = s;
                }
            }

            // 5) Grant
            foreach (var g in grants)
            {
                if (!TryParseNumeric(g.itemId, out int nid))
                {
                    Debug.LogWarning($"[DevInventorySeeder] Grant '{g.itemId}' is not a numeric id. Skipping.");
                    continue;
                }

                var def = lookup.GetById(nid);
                if (def == null)
                {
                    Debug.LogWarning($"[DevInventorySeeder] ItemDef not found for id={nid}. " +
                                     $"Ensure an ItemDef with this numeric itemId exists under any 'Resources' subpath the lookup scans.");
                    continue;
                }

                ushort leftover = inv.ServerAddItem(nid, g.count);
                if (leftover > 0)
                    Debug.LogWarning($"[DevInventorySeeder] Backpack full while adding id={nid}. Leftover={leftover}.");
                else
                    Debug.Log($"[DevInventorySeeder] Granted {g.count} x {PrettyName(def)} (id={nid}).");
            }
        }

        // --- helpers ---

        static bool TryParseNumeric(string text, out int value)
        {
            if (int.TryParse(text, out value)) return true;
            // allow hex like "0x0A" just in case
            if (text != null && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out value);
            value = 0; return false;
        }

        // Display name fallback
        static string PrettyName(ItemDef def)
        {
            if (def == null) return "(null)";
            object disp = GetMemberValue(def, new[] { "displayName", "title", "label" });
            return disp as string ?? def.name;
        }

        static object GetMemberValue(object obj, string[] names)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var n in names)
            {
                var f = t.GetField(n, F);
                if (f != null) return f.GetValue(obj);
                var p = t.GetProperty(n, F);
                if (p != null && p.CanRead) return p.GetValue(obj, null);
            }
            return null;
        }
    }
}
