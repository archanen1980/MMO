using System;
using UnityEngine;

namespace MMO.Shared.Item
{
    /// <summary>
    /// Scriptable definition for an item.
    /// </summary>
    [CreateAssetMenu(menuName = "MMO/Item", fileName = "new_item")]
    public class ItemDef : ScriptableObject
    {
        // ---------- Identity ----------
        [Header("Identity")]
        [Tooltip("Stable string ID used in data, saves, recipes, etc.")]
        public string itemId = "new_item";

        [Tooltip("Friendly name shown to players.")]
        public string displayName = "New Item";

        // ---------- Presentation ----------
        [Header("Presentation")]
        public Sprite icon;
        [SerializeField] public ItemRarity rarity = ItemRarity.Common;
        public string RarityHex => ItemRarityUtil.Hex(rarity);
        
        [TextArea]
        public string description;

        // ---------- Stacking & Weight ----------
        [Header("Stacking & Weight")]
        [Min(1)] public int maxStack = 1;
        public float weight = 0f;

        // ---------- Classification & Crafting ----------
        [Header("Classification")]
        [Tooltip("High-level category used by gameplay, UI, loot tables, etc.")]
        public ItemKind kind = ItemKind.Resource;

        [Tooltip("If false, this item should not be produced by crafting (e.g., quest/key items).")]
        public bool isCraftable = true;

        // ---------- Equipment ----------
        [Header("Equipment (only if kind == Equipment)")]
        [Tooltip("Allowed equipment slots (bit flags). Only used when ItemKind is Equipment.")]
        public EquipSlot equipSlots = EquipSlot.None;

        /// <summary>Convenience: true if this item is equipment.</summary>
        public bool IsEquipment => kind == ItemKind.Equipment;

        /// <summary>Returns true if this item can be equipped in the provided slot.</summary>
        public bool CanEquipIn(EquipSlot slot) => IsEquipment && (equipSlots & slot) != 0;

#if UNITY_EDITOR
        // Light validation to keep values sane in the editor.
        void OnValidate()
        {
            if (maxStack < 1) maxStack = 1;
            if (!IsEquipment) equipSlots = EquipSlot.None;
        }
#endif
    }

    /// <summary>
    /// Broad item categories to drive logic (crafting eligibility, filters, etc.).
    /// </summary>
    public enum ItemKind
    {
        Resource,
        Equipment,
        Consumable,
        Material,
        Tool,
        Quest,
        Key,
        Misc
    }

    /// <summary>
    /// Equipment slots as bit flags. Combine for multi-slot compatibility (e.g., MainHand | OffHand).
    /// </summary>
    [Flags]
    public enum EquipSlot
    {
        None = 0,
        Head = 1 << 0,
        Chest = 1 << 1,
        Legs = 1 << 2,
        Hands = 1 << 3,
        Feet = 1 << 4,
        MainHand = 1 << 5,
        OffHand = 1 << 6,
        Accessory = 1 << 7,
    }
}
