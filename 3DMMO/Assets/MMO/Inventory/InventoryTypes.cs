using ItemDef = MMO.Shared.Item.ItemDef;

using System;
using UnityEngine;


namespace MMO.Inventory
{
    // Equipment slots (expand as needed)
    [Flags]
    
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