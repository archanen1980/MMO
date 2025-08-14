using System;
using System.Reflection;                 // reflection helpers for equip mask + itemId parsing
using Mirror;
using UnityEngine;
using MMO.Shared.Item;                    // YOUR ItemDef base

namespace MMO.Inventory
{
    // Server-authoritative inventory for Mirror
    public class PlayerInventory : NetworkBehaviour
    {
        [Header("Config")]
        [SerializeField, Min(1)] int backpackSize = 24;
        [SerializeField, Min(1)] int equipmentSize = 8;

        // Hard caps to prevent runaway allocations / editor hangs
        const int MAX_BACKPACK_SLOTS = 200;
        const int MAX_EQUIP_SLOTS = 32;

        [SerializeField, HideInInspector] ResourcesItemLookup itemLookup; // scene singleton; hidden to avoid prefab confusion

        // Mirror sync state
        public readonly SyncList<InvSlot> Backpack = new SyncList<InvSlot>();
        public readonly SyncList<InvSlot> Equipment = new SyncList<InvSlot>();

        // UI hook (client-only)
        public event Action OnClientInventoryChanged;

        void OnValidate()
        {
            if (backpackSize < 1) backpackSize = 1;
            if (equipmentSize < 1) equipmentSize = 1;
            if (backpackSize > MAX_BACKPACK_SLOTS) backpackSize = MAX_BACKPACK_SLOTS;
            if (equipmentSize > MAX_EQUIP_SLOTS) equipmentSize = MAX_EQUIP_SLOTS;
        }

        #region Mirror lifecycle
        public override void OnStartServer()
        {
            base.OnStartServer();
            int bp = Mathf.Clamp(backpackSize, 1, MAX_BACKPACK_SLOTS);
            int eq = Mathf.Clamp(equipmentSize, 1, MAX_EQUIP_SLOTS);
            ServerEnsureSize(Backpack, bp);
            ServerEnsureSize(Equipment, eq);
            Debug.Log($"[PlayerInventory] ServerEnsureSize -> Backpack:{Backpack.Count} Equipment:{Equipment.Count}");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Backpack.Callback += OnSyncListChanged;
            Equipment.Callback += OnSyncListChanged;
            OnClientInventoryChanged?.Invoke();
        }

        public override void OnStopClient()
        {
            Backpack.Callback -= OnSyncListChanged;
            Equipment.Callback -= OnSyncListChanged;
        }

        void OnSyncListChanged(SyncList<InvSlot>.Operation op, int index, InvSlot oldItem, InvSlot newItem)
        {
            OnClientInventoryChanged?.Invoke();
        }
        #endregion

        #region Public (server) API
        /// <summary>Server: add amount of itemId to Backpack. Returns leftover if not all fit.</summary>
        [Server]
        public ushort ServerAddItem(int itemId, ushort amount)
        {
            var def = Lookup(itemId);
            if (def == null || amount == 0) return amount;

            // 1) Fill existing stacks
            if (GetMaxStack(def) > 1)
            {
                for (int i = 0; i < Backpack.Count && amount > 0; i++)
                {
                    var s = Backpack[i];
                    if (!s.IsEmpty && s.itemId == itemId)
                    {
                        int canTake = Mathf.Min(GetMaxStack(def) - s.count, amount);
                        if (canTake > 0)
                        {
                            s.count += (ushort)canTake;
                            Backpack[i] = s;
                            amount -= (ushort)canTake;
                        }
                    }
                }
            }

            // 2) Fill empty slots
            for (int i = 0; i < Backpack.Count && amount > 0; i++)
            {
                var s = Backpack[i];
                if (s.IsEmpty)
                {
                    int put = Mathf.Min(GetMaxStack(def), amount);
                    s.itemId = itemId;
                    s.count = (ushort)put;
                    Backpack[i] = s;
                    amount -= (ushort)put;
                }
            }

            return amount; // >0 means inventory full
        }
        #endregion

        #region Client->Server commands
        /// <summary>Move/split/swap/equip/unequip. amount=0 => all.</summary>
        [Command]
        public void CmdMove(ContainerKind srcKind, int srcIndex, ContainerKind dstKind, int dstIndex, ushort amount)
        {
            if (!isServer) return;
            if (!ServerTryMove(srcKind, srcIndex, dstKind, dstIndex, amount))
                TargetNotifyMoveFailed(connectionToClient);
        }

        [TargetRpc]
        void TargetNotifyMoveFailed(NetworkConnectionToClient conn)
        {
            // Optional: show a small toast on client
        }
        #endregion

        #region Server move logic
        [Server]
        bool ServerTryMove(ContainerKind srcKind, int srcIndex, ContainerKind dstKind, int dstIndex, ushort amount)
        {
            var src = GetList(srcKind);
            var dst = GetList(dstKind);
            if (src == null || dst == null) return false;
            if (!InRange(src, srcIndex) || !InRange(dst, dstIndex)) return false;

            var s = src[srcIndex];
            if (s.IsEmpty) return false;

            var item = Lookup(s.itemId);
            if (item == null) return false;

            if (amount == 0 || amount > s.count) amount = s.count;

            var d = dst[dstIndex];

            // ---- EQUIPMENT ----
            if (dstKind == ContainerKind.Equipment)
            {
                if (amount > 1) amount = 1;
                if (!CanEquip(item, dstIndex)) return false;

                int numericId;
                if (!TryGetNumericItemId(item, out numericId)) return false;

                if (d.IsEmpty)
                {
                    // move 1 into equipment
                    s.count -= 1;
                    if (s.count == 0) s.Clear();
                    d.itemId = numericId; // write numeric id to slot
                    d.count = 1;
                    src[srcIndex] = s;
                    dst[dstIndex] = d;
                    return true;
                }
                else
                {
                    // swap equip <-> src
                    if (d.count != 1) return false;

                    var dstItem = Lookup(d.itemId);
                    if (dstItem == null) return false;
                    if (!CanEquip(item, dstIndex)) return false;

                    if (srcKind == ContainerKind.Equipment)
                    {
                        if (!CanEquip(dstItem, srcIndex)) return false;
                    }

                    var tmp = d;
                    d = new InvSlot { itemId = s.itemId, count = 1 };

                    s.count -= 1;
                    if (s.count == 0) s.Clear();

                    if (srcKind == ContainerKind.Equipment)
                    {
                        src[srcIndex] = tmp;
                    }
                    else
                    {
                        if (src[srcIndex].IsEmpty)
                        {
                            src[srcIndex] = tmp;
                        }
                        else
                        {
                            var ss = src[srcIndex];
                            var tDef = Lookup(tmp.itemId);
                            if (!ss.IsEmpty && ss.itemId == tmp.itemId && tDef != null && GetMaxStack(tDef) > ss.count)
                            {
                                int can = Mathf.Min(GetMaxStack(tDef) - ss.count, tmp.count);
                                if (can > 0)
                                {
                                    ss.count += (ushort)can;
                                    src[srcIndex] = ss;
                                    tmp.count -= (ushort)can;
                                }
                            }
                            if (tmp.count > 0)
                            {
                                ushort leftover = ServerAddItem(tmp.itemId, tmp.count);
                                if (leftover > 0) return false;
                            }
                        }
                    }

                    dst[dstIndex] = d;
                    return true;
                }
            }

            // ---- BACKPACK ----
            if (dstKind == ContainerKind.Backpack)
            {
                if (d.IsEmpty)
                {
                    d.itemId = s.itemId;
                    d.count = amount;
                    s.count -= amount;
                    if (s.count == 0) s.Clear();
                    src[srcIndex] = s;
                    dst[dstIndex] = d;
                    return true;
                }

                if (d.itemId == s.itemId)
                {
                    if (GetMaxStack(item) <= 1) return false;
                    int canTake = Mathf.Min(GetMaxStack(item) - d.count, amount);
                    if (canTake == 0) return false;
                    d.count += (ushort)canTake;
                    s.count -= (ushort)canTake;
                    if (s.count == 0) s.Clear();
                    src[srcIndex] = s;
                    dst[dstIndex] = d;
                    return true;
                }
                else
                {
                    var tmp = d;
                    d = s;
                    src[srcIndex] = tmp;
                    dst[dstIndex] = d;
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region Helpers (sizes, lookup, equip, parsing)
        [Server]
        void ServerEnsureSize(SyncList<InvSlot> list, int size)
        {
            int cap = (list == Backpack) ? MAX_BACKPACK_SLOTS : MAX_EQUIP_SLOTS;
            size = Mathf.Clamp(size, 1, cap);

            for (int i = list.Count; i < size; i++) list.Add(default(InvSlot));
            for (int i = list.Count - 1; i >= size; i--) list.RemoveAt(i);
        }

        SyncList<InvSlot> GetList(ContainerKind kind)
        {
            if (kind == ContainerKind.Backpack) return Backpack;
            if (kind == ContainerKind.Equipment) return Equipment;
            return null;
        }

        static bool InRange(SyncList<InvSlot> list, int index) => index >= 0 && index < list.Count;

        ItemDef Lookup(int id)
        {
            if (itemLookup == null)
                itemLookup = ResourcesItemLookup.Instance;
            return (itemLookup != null) ? itemLookup.GetById(id) : null;
        }

        // Max stack via reflection-friendly fallback (supports maxStack or stackSize etc.)
        int GetMaxStack(ItemDef def)
        {
            if (def == null) return 1;

            // Try common names
            object val = GetMemberValue(def, new[] { "maxStack", "stackSize", "stackLimit", "stackCap" });
            if (val is int i) return Mathf.Max(1, i);
            if (val is string s && int.TryParse(s, out var parsed)) return Mathf.Max(1, parsed);

            // Default to 1
            return 1;
        }

        // Equip permission (works with various schemas)
        bool CanEquip(ItemDef item, int equipmentIndex)
        {
            EquipSlot target;
            switch (equipmentIndex)
            {
                case 0: target = EquipSlot.Head; break;
                case 1: target = EquipSlot.Chest; break;
                case 2: target = EquipSlot.Legs; break;
                case 3: target = EquipSlot.MainHand; break;
                case 4: target = EquipSlot.OffHand; break;
                case 5: target = EquipSlot.Accessory; break;
                case 6: target = EquipSlot.Accessory; break;
                case 7: target = EquipSlot.Accessory; break;
                default: target = EquipSlot.None; break;
            }

            if (!TryGetEquipMask(item, out var mask))
                return false;

            return (mask & target) != 0;
        }

        // Attempts to extract an EquipSlot mask from your ItemDef via common member names/types.
        static bool TryGetEquipMask(ItemDef item, out EquipSlot mask)
        {
            mask = EquipSlot.None;
            if (item == null) return false;

            string[] names =
            {
                "equipSlotsMask", "equipMask", "allowedSlots", "allowedEquipSlots",
                "slotMask", "equipSlot", "slot", "equipmentSlot", "equipmentSlots"
            };

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = item.GetType();

            foreach (var name in names)
            {
                var f = t.GetField(name, flags);
                if (f != null && TryConvertEquipMask(f.GetValue(item), out mask)) return true;

                var p = t.GetProperty(name, flags);
                if (p != null && p.CanRead && TryConvertEquipMask(p.GetValue(item, null), out mask)) return true;
            }

            return false;
        }

        static bool TryConvertEquipMask(object value, out EquipSlot mask)
        {
            mask = EquipSlot.None;
            if (value == null) return false;

            if (value is int i) { mask = (EquipSlot)i; return true; }
            if (value is uint ui) { mask = (EquipSlot)(int)ui; return true; }
            if (value is short s) { mask = (EquipSlot)s; return true; }
            if (value is ushort us) { mask = (EquipSlot)us; return true; }
            if (value is byte b) { mask = (EquipSlot)b; return true; }

            var type = value.GetType();
            if (type.IsEnum)
            {
                try { mask = (EquipSlot)Convert.ChangeType(value, typeof(int)); return true; }
                catch { return TryParseEquipSlotString(value.ToString(), out mask); }
            }

            if (value is string str) return TryParseEquipSlotString(str, out mask);

            return false;
        }

        static bool TryParseEquipSlotString(string s, out EquipSlot mask)
        {
            mask = EquipSlot.None;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Split(new[] { '|', ',', '+', ';' }, StringSplitOptions.RemoveEmptyEntries);
            EquipSlot m = EquipSlot.None;
            foreach (var raw in parts)
            {
                var part = raw.Trim();
                if (Enum.TryParse(part, true, out EquipSlot flag))
                    m |= flag;
            }
            mask = m;
            return m != EquipSlot.None;
        }

        /// <summary>Parse ItemDef.itemId (int or string) → int.</summary>
        static bool TryGetNumericItemId(ItemDef def, out int numericId)
        {
            numericId = 0;
            if (def == null) return false;

            var t = def.GetType();
            // field "itemId" or "id"
            var f = t.GetField("itemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 ?? t.GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                object v = f.GetValue(def);
                if (v is int i) { numericId = i; return true; }
                if (v is string s && int.TryParse(s, out numericId)) return true;
            }

            // property "itemId" or "id"
            var p = t.GetProperty("itemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                 ?? t.GetProperty("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                object v = p.GetValue(def, null);
                if (v is int i) { numericId = i; return true; }
                if (v is string s && int.TryParse(s, out numericId)) return true;
            }

            return false;
        }

        // Reflection: try a list of possible member names
        static object GetMemberValue(object obj, string[] names)
        {
            if (obj == null || names == null) return null;
            var t = obj.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var name in names)
            {
                var f = t.GetField(name, flags);
                if (f != null) return f.GetValue(obj);

                var p = t.GetProperty(name, flags);
                if (p != null && p.CanRead) return p.GetValue(obj, null);
            }
            return null;
        }
        #endregion

        #region Persistence
        [Serializable]
        public class InventorySave
        {
            public InvSlot[] backpack;
            public InvSlot[] equipment;
        }

        [Server]
        public InventorySave CaptureState()
        {
            var bp = new InvSlot[Backpack.Count];
            for (int i = 0; i < Backpack.Count; i++) bp[i] = Backpack[i];

            var eq = new InvSlot[Equipment.Count];
            for (int i = 0; i < Equipment.Count; i++) eq[i] = Equipment[i];

            return new InventorySave { backpack = bp, equipment = eq };
        }

        [Server]
        public void RestoreState(InventorySave save)
        {
            if (save == null) return;

            Backpack.Clear();
            Equipment.Clear();

            if (save.backpack != null)
                for (int i = 0; i < save.backpack.Length; i++)
                    Backpack.Add(save.backpack[i]);

            if (save.equipment != null)
                for (int i = 0; i < save.equipment.Length; i++)
                    Equipment.Add(save.equipment[i]);
        }
        #endregion
    }
}
