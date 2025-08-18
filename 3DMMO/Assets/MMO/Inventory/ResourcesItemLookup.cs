using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using MMO.Shared.Item; // your ItemDef type

namespace MMO.Inventory
{
    /// <summary>
    /// Scene singleton that indexes ItemDef assets from Resources and provides lookup by numeric itemId.
    /// Set 'searchPaths' to the Resources-relative folders that contain your items (e.g., "Items", "MMO/GameItems").
    /// SPECIAL: an entry of "" or "*" means "scan ALL Resources" (useful for debugging).
    /// </summary>
    public class ResourcesItemLookup : MonoBehaviour, IItemLookup
    {
        public static ResourcesItemLookup Instance { get; private set; }

        [Tooltip("Resources-relative folders to scan (e.g., \"Items\", \"MMO/GameItems\"). Use \"\" or \"*\" to scan ALL Resources.")]
        [SerializeField] private string[] searchPaths = new string[] { "Items" };

        [Tooltip("Verbose logging of what gets indexed.")]
        [SerializeField] private bool verbose = true;

        // Indexed by NUMERIC itemId (we parse string itemId -> int)
        private readonly Dictionary<int, ItemDef> dict = new Dictionary<int, ItemDef>();

        // Debug helpers for Seeder / probes
        public int CountIndexed => dict.Count;
        public int[] IndexedIds => dict.Keys.OrderBy(k => k).ToArray();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (verbose) Debug.Log("[ResourcesItemLookup] Awake start");
            BuildIndex();
            if (verbose) Debug.Log("[ResourcesItemLookup] Awake end");
        }

        private void BuildIndex()
        {
            
            dict.Clear();
            var dupes = new HashSet<int>();

            // Normalize paths; if none set, default to "Items"
            if (searchPaths == null || searchPaths.Length == 0)
                searchPaths = new string[] { "Items" };

            // If ANY entry is "" or "*", treat as global scan ONCE
            bool doGlobal = false;
            for (int i = 0; i < searchPaths.Length; i++)
            {
                if (searchPaths[i] == null) searchPaths[i] = "";
                searchPaths[i] = searchPaths[i].Trim();
                if (searchPaths[i] == "" || searchPaths[i] == "*")
                    doGlobal = true;
            }

            if (doGlobal)
            {
                IndexPath("", dupes); // global
            }
            else
            {
                foreach (var p in searchPaths)
                    if (!string.IsNullOrEmpty(p))
                        IndexPath(p, dupes);
            }

#if UNITY_EDITOR
            if (verbose)
            {
                int count = dict.Count;
                const int cap = 200; // don’t spam the console
                var preview = dict.Take(cap)
                    .Select(kv => kv.Key + ":" + (string.IsNullOrEmpty(kv.Value.displayName) ? kv.Value.name : kv.Value.displayName));
                Debug.Log($"[ResourcesItemLookup] Indexed {count} ItemDefs from [{string.Join(", ", searchPaths)}]. " +
                          $"Showing first {Mathf.Min(cap, count)}: {string.Join(", ", preview)}");

                if (dupes.Count > 0)
                    Debug.LogWarning("[ResourcesItemLookup] Duplicate numeric itemIds ignored (kept first): " +
                                     string.Join(", ", dupes.ToArray()));
            }
#endif

            // (Optional hard-guard near your 'doGlobal' logic)
            if (doGlobal)
            {
#if UNITY_EDITOR
                Debug.LogWarning("[ResourcesItemLookup] Global scan ('*' or '') enabled. This is heavy. Consider scoping to 'Items/'.");
#endif
            }
        }

        private void IndexPath(string path, HashSet<int> dupes)
        {
            // NOTE: path == "" means "ALL Resources"
            var found = Resources.LoadAll<ItemDef>(path);
            if (verbose) Debug.Log($"[ResourcesItemLookup] Scanning \"{(path == "" ? "<ALL>" : path)}\" → {found.Length} assets");

            foreach (var d in found)
            {
                if (d == null) continue;
                if (!TryGetNumericItemId(d, out int nid))
                {
                    if (verbose) Debug.LogWarning("[ResourcesItemLookup] '" + d.name + "' has non-numeric or missing itemId.");
                    continue;
                }
                if (nid <= 0)
                {
                    if (verbose) Debug.LogWarning("[ResourcesItemLookup] '" + d.name + "' itemId must be > 0.");
                    continue;
                }
                if (dict.ContainsKey(nid)) { dupes.Add(nid); continue; }
                dict.Add(nid, d);
            }
        }

        public ItemDef GetById(int id)
        {
            ItemDef def;
            return dict.TryGetValue(id, out def) ? def : null;
        }

        /// <summary>Supports ItemDef.itemId (string or int) and also "id" as fallback; returns parsed int.</summary>
        private static bool TryGetNumericItemId(ItemDef def, out int numericId)
        {
            numericId = 0;
            if (def == null) return false;

            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = def.GetType();

            // Field "itemId" or "id"
            var f = t.GetField("itemId", F) ?? t.GetField("id", F);
            if (f != null)
            {
                var v = f.GetValue(def);
                if (v is int i) { numericId = i; return true; }
                if (v is string s && int.TryParse(s, out numericId)) return true;
            }

            // Property "itemId" or "id"
            var p = t.GetProperty("itemId", F) ?? t.GetProperty("id", F);
            if (p != null)
            {
                var v = p.GetValue(def, null);
                if (v is int i) { numericId = i; return true; }
                if (v is string s && int.TryParse(s, out numericId)) return true;
            }

            return false;
        }
    }
}
