using System.Collections.Generic;
using UnityEngine;

namespace MMO.Shared.Crafting
{
    /// <summary>
    /// Runtime registry for CraftingRecipeDef assets. Loads from Resources/Recipes.
    /// </summary>
    public static class RecipeRegistry
    {
        static readonly Dictionary<string, CraftingRecipeDef> _byId = new();
        static bool _loaded;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            var defs = Resources.LoadAll<CraftingRecipeDef>("Recipes");
            foreach (var def in defs)
            {
                if (!def) continue;
                string key = (def.recipeId ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) continue;

                if (_byId.ContainsKey(key))
                    Debug.LogWarning($"RecipeRegistry: duplicate recipeId '{key}' (asset: {def.name})");
                else
                    _byId[key] = def;
            }
            _loaded = true;
            Debug.Log($"RecipeRegistry: loaded {_byId.Count} recipe(s) from Resources/Recipes");
        }

        public static CraftingRecipeDef Get(string recipeId)
        {
            if (string.IsNullOrWhiteSpace(recipeId)) return null;
            EnsureLoaded();
            _byId.TryGetValue(recipeId.Trim().ToLowerInvariant(), out var def);
            return def;
        }

        public static IEnumerable<CraftingRecipeDef> All()
        {
            EnsureLoaded();
            return _byId.Values;
        }
    }
}
