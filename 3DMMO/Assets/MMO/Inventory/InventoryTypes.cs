using ItemDef = MMO.Shared.Item.ItemDef;

using System;
using UnityEngine;


namespace MMO.Inventory
{
    // Equipment slots (expand as needed)
    [Flags]
    public enum EquipSlot : int
    {
        None = 0,
        Head = 1 << 0,
        Chest = 1 << 1,
        Legs = 1 << 2,
        Feet = 1 << 3,
        Hands = 1 << 4,
        MainHand = 1 << 5,
        OffHand = 1 << 6,
        Accessory = 1 << 7,
    }

    public enum ContainerKind : byte
    {
        Backpack = 0,
        Equipment = 1
    }

    [Serializable]
    public struct InvSlot
    {
        public int itemId;
        public ushort count;
        public bool IsEmpty => itemId <= 0 || count == 0;
        public void Clear() { itemId = 0; count = 0; }
    }

    public interface IItemLookup
    {
        ItemDef GetById(int id);   // ← now returns YOUR ItemDef type
    }
}