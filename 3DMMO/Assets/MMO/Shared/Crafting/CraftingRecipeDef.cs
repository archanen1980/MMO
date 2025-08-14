using System;
using UnityEngine;
using MMO.Shared.Item;

namespace MMO.Shared.Crafting
{
    /// <summary>
    /// Crafting recipe asset. Place under Resources/Recipes to auto-register.
    /// Uses ItemDef references for authoring; runtime can read itemId via item.itemId.
    /// </summary>
    [CreateAssetMenu(menuName = "MMO/Crafting Recipe", fileName = "NewRecipe")]
    public class CraftingRecipeDef : ScriptableObject
    {
        [Tooltip("Unique, stable ID for this recipe, e.g., 'craft_potion_health_s'")]
        public string recipeId = "new_recipe";

        [Tooltip("Optional tag for where this can be crafted (e.g., 'campfire', 'forge'). Leave blank for anywhere.")]
        public string stationTag = "";

        [Tooltip("Time in seconds to craft (progress bars, etc).")]
        public float craftSeconds = 1f;

        [Serializable]
        public class ItemAmount
        {
            public ItemDef item;
            [Min(1)] public int amount = 1;
        }

        [Header("Inputs (consumed)")]
        public ItemAmount[] inputs;

        [Header("Output (produced)")]
        public ItemAmount output;
    }
}
