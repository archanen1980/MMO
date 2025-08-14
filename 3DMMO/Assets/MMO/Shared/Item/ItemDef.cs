using UnityEngine;

namespace MMO.Shared.Item
{
    /// <summary>
    /// Design-time item definition stored as a ScriptableObject.
    /// Place instances under Resources/Items so they auto-register at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "MMO/Item Definition", fileName = "NewItemDef")]
    public class ItemDef : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique, stable ID (e.g., 'potion_health_s'). Used at runtime & in saves.")]
        public string itemId = "new_item";

        [Tooltip("Human-readable name shown in UI.")]
        public string displayName = "New Item";

        [Header("Stacking")]
        [Tooltip("Max items per stack; 1 = non-stackable (e.g., sword).")]
        public int maxStack = 1;

        [Header("Presentation")]
        public Sprite icon;
        [TextArea] public string description;

        [Header("Gameplay (optional)")]
        public float weight = 0f;
    }
}
