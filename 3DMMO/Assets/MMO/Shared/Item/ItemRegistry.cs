using System.Collections.Generic;
using UnityEngine;

namespace MMO.Shared.Item
{
    /// <summary>
    /// Runtime registry for ItemDef assets. Loads from Resources/Items on first use.
    /// </summary>
    public static class ItemRegistry
    {
        static readonly Dictionary<string, ItemDef> _byId = new();
        static bool _loaded;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            var defs = Resources.LoadAll<ItemDef>("Items");
            foreach (var def in defs)
            {
                if (!def) continue;
                string key = (def.itemId ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) continue;

                if (_byId.ContainsKey(key))
                    Debug.LogWarning($"ItemRegistry: duplicate itemId '{key}' (asset: {def.name})");
                else
                    _byId[key] = def;
            }
            _loaded = true;
            Debug.Log($"ItemRegistry: loaded {_byId.Count} item(s) from Resources/Items");
        }

        public static ItemDef Get(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;
            EnsureLoaded();
            _byId.TryGetValue(itemId.Trim().ToLowerInvariant(), out var def);
            return def;
        }

        public static int MaxStack(string itemId) => Get(itemId) ? Mathf.Max(1, Get(itemId).maxStack) : 1;
    }
}
