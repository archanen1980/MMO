using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Mirror;
using MMO.Shared.Item;  // ItemDef, ItemKind, EquipSlot, ItemRarity
using MMO.Chat;         // LootChatBridge
using MMO.Loot.UI;      // LootToastWindow

// Alias for readability
using EquipSlotAlias = MMO.Shared.Item.EquipSlot;

namespace MMO.Inventory
{
    /// <summary>
    /// Mirror-synced player inventory & equipment.
    /// - Backpack: grid of stackable InvSlot entries.
    /// - Equipment: fixed-size array; each index maps to a chosen EquipSlot (configurable in inspector).
    /// - On any successful ServerAdd, notifies the owning client via TargetLootToast()
    ///   which shows a toast and posts a Loot chat message.
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        // ---------- Inspector ----------
        [Header("Backpack")]
        [SerializeField] int backpackCapacity = 28; // e.g., 7x4

        [Header("Equipment Layout (left → right)")]
        [Tooltip("Which EquipSlot each equipment index represents (index 0 is the leftmost slot in your EquipmentGrid).")]
        [SerializeField]
        EquipSlotAlias[] equipLayout =
        {
            EquipSlotAlias.Head,      // 0
            EquipSlotAlias.Chest,     // 1
            EquipSlotAlias.Legs,      // 2
            EquipSlotAlias.Hands,     // 3
            EquipSlotAlias.Feet,      // 4
            EquipSlotAlias.MainHand,  // 5
            EquipSlotAlias.OffHand,   // 6
            EquipSlotAlias.Accessory, // 7
        };

        [Header("Lookups (optional)")]
        [Tooltip("Optional: assign your ResourcesItemLookup (or similar). If empty, code falls back to Resources/Items.")]
        public UnityEngine.Object itemLookup;

        // ---------- Sync Data ----------
        public readonly SyncList<InvSlot> Backpack = new SyncList<InvSlot>();
        public readonly SyncList<InvSlot> Equipped = new SyncList<InvSlot>();

        // ---------- UI Events ----------
        public event Action OnBackpackChanged;
        public event Action OnEquippedChanged;
        // Back-compat event name some UIs already subscribe to:
        public event Action OnClientInventoryChanged;

        // Back-compat property aliases
        public SyncList<InvSlot> Equipment => Equipped;
        public SyncList<InvSlot> Inventory => Backpack;

        // Public info for UI
        public const int DefaultEquipCount = 8;
        public int EquipCount => equipLayout != null && equipLayout.Length > 0 ? equipLayout.Length : DefaultEquipCount;

        // Index <-> EquipSlot mapping (uses the inspector-driven layout)
        public EquipSlotAlias IndexToSlot(int index)
            => (index >= 0 && index < EquipCount) ? equipLayout[index] : EquipSlotAlias.None;

        public int SlotToIndex(EquipSlotAlias slot)
        {
            if (equipLayout == null) return -1;
            for (int i = 0; i < equipLayout.Length; i++)
                if (equipLayout[i] == slot) return i;
            return -1;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Keep layout non-null and at least DefaultEquipCount long for convenience
            if (equipLayout == null || equipLayout.Length == 0)
                equipLayout = new[] {
                    EquipSlotAlias.Head, EquipSlotAlias.Chest, EquipSlotAlias.Legs, EquipSlotAlias.Hands,
                    EquipSlotAlias.Feet, EquipSlotAlias.MainHand, EquipSlotAlias.OffHand, EquipSlotAlias.Accessory
                };

            // Sanitize entries: force to single-bit flags (no multi-bit combos for a position)
            for (int i = 0; i < equipLayout.Length; i++)
                equipLayout[i] = SanitizeSingleFlag(equipLayout[i]);
        }

        static EquipSlotAlias SanitizeSingleFlag(EquipSlotAlias v)
        {
            // If it's exactly one known flag, keep it. Otherwise coerce "None".
            switch (v)
            {
                case EquipSlotAlias.None:
                case EquipSlotAlias.Head:
                case EquipSlotAlias.Chest:
                case EquipSlotAlias.Legs:
                case EquipSlotAlias.Hands:
                case EquipSlotAlias.Feet:
                case EquipSlotAlias.MainHand:
                case EquipSlotAlias.OffHand:
                case EquipSlotAlias.Accessory:
                    return v;
                default:
                    return EquipSlotAlias.None;
            }
        }
#endif

        // ---------- Mirror lifecycle ----------
        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureCapacities();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsureCapacities();

            Backpack.Callback += OnBackpackSyncChanged;
            Equipped.Callback += OnEquippedSyncChanged;

            // Initial UI notify
            OnClientInventoryChanged?.Invoke();
            OnBackpackChanged?.Invoke();
            OnEquippedChanged?.Invoke();
        }

        void OnDestroy()
        {
            Backpack.Callback -= OnBackpackSyncChanged;
            Equipped.Callback -= OnEquippedSyncChanged;
        }

        // ---------- Capacity / init ----------
        [Server]
        public void EnsureCapacities()
        {
            // Backpack
            while (Backpack.Count < backpackCapacity) Backpack.Add(default);
            while (Backpack.Count > backpackCapacity) Backpack.RemoveAt(Backpack.Count - 1);

            // Equipped mirrors inspector-driven EquipCount
            int desired = EquipCount;
            while (Equipped.Count < desired) Equipped.Add(default);
            while (Equipped.Count > desired) Equipped.RemoveAt(Equipped.Count - 1);
        }

        // ---------- Callbacks (client) ----------
        void OnBackpackSyncChanged(SyncList<InvSlot>.Operation op, int index, InvSlot oldItem, InvSlot newItem)
        {
            OnBackpackChanged?.Invoke();
            OnClientInventoryChanged?.Invoke();
        }

        void OnEquippedSyncChanged(SyncList<InvSlot>.Operation op, int index, InvSlot oldItem, InvSlot newItem)
        {
            OnEquippedChanged?.Invoke();
            OnClientInventoryChanged?.Invoke();
        }

        // =====================================================================
        //                            Public API (Server)
        // =====================================================================

        [Server] public int ServerAdd(string itemId, int amount) => ServerAdd(ResolveDef(itemId), amount);

        [Server]
        public int ServerAdd(ItemDef def, int amount)
        {
            if (def == null || amount <= 0) return 0;
            EnsureCapacities();

            int remaining = amount;

            // 1) fill partial stacks
            for (int i = 0; i < Backpack.Count && remaining > 0; i++)
            {
                var s = Backpack[i];
                if (!IsSameItem(s, def)) continue;

                int max = Math.Max(1, def.maxStack);
                int cur = GetAmount(s);
                if (cur >= max) continue;

                int can = Math.Min(remaining, max - cur);
                s = WithAmount(s, cur + can);
                Backpack[i] = s;
                remaining -= can;
            }

            // 2) fill empty slots
            for (int i = 0; i < Backpack.Count && remaining > 0; i++)
            {
                var s = Backpack[i];
                if (!IsEmpty(s)) continue;

                int max = Math.Max(1, def.maxStack);
                int put = Math.Min(remaining, max);
                s = WithItemId(default, def.itemId);
                s = WithAmount(s, put);
                Backpack[i] = s;
                remaining -= put;
            }

            int added = amount - remaining;

            // Notify the owning client once per add (aggregate across stacks)
            if (added > 0 && connectionToClient != null)
            {
                string itemName = TryGetDisplayName(def) ?? def.itemId;
                string rarityHex = RarityHex(def.rarity);
                // icon path not used: client resolves icon from ItemDef (or a provided path)
                TargetLootToast(connectionToClient, def.itemId, itemName, added, rarityHex, null);
            }

            return added;
        }

        [Server]
        public int ServerRemove(string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return 0;

            int left = amount;
            for (int i = 0; i < Backpack.Count && left > 0; i++)
            {
                var s = Backpack[i];
                if (!IsSameItem(s, itemId)) continue;

                int cur = GetAmount(s);
                int take = Math.Min(left, cur);
                int newA = cur - take;

                s = (newA > 0) ? WithAmount(s, newA) : default;
                Backpack[i] = s;
                left -= take;
            }
            return amount - left;
        }

        [Server]
        public bool ServerSwapBackpack(int a, int b)
        {
            if (!InBackpack(a) || !InBackpack(b) || a == b) return false;
            var tmp = Backpack[a];
            Backpack[a] = Backpack[b];
            Backpack[b] = tmp;
            return true;
        }

        [Server]
        public bool ServerMoveOrMerge(int fromIndex, int toIndex)
        {
            if (!InBackpack(fromIndex) || !InBackpack(toIndex) || fromIndex == toIndex) return false;
            var from = Backpack[fromIndex];
            var to = Backpack[toIndex];

            if (IsEmpty(from)) return false;

            // merge if same item and room
            if (!IsEmpty(to) && GetItemId(from).Equals(GetItemId(to), StringComparison.OrdinalIgnoreCase))
            {
                var def = ResolveDef(GetItemId(from));
                int max = Math.Max(1, def ? def.maxStack : 1);

                int ca = GetAmount(from);
                int cb = GetAmount(to);
                int can = Math.Min(ca, Math.Max(0, max - cb));
                if (can > 0)
                {
                    to = WithAmount(to, cb + can);
                    ca -= can;
                    from = (ca > 0) ? WithAmount(from, ca) : default;

                    Backpack[toIndex] = to;
                    Backpack[fromIndex] = from;
                    return true;
                }
            }

            // else swap
            Backpack[toIndex] = from;
            Backpack[fromIndex] = to;
            return true;
        }

        [Server]
        public bool ServerEquipFromBackpack(int backpackIndex, int equipIndex, out string reason)
        {
            reason = null;
            if (!InBackpack(backpackIndex) || !InEquip(equipIndex)) { reason = "Index out of range"; return false; }

            var from = Backpack[backpackIndex];
            if (IsEmpty(from)) { reason = "No item"; return false; }

            var def = ResolveDef(GetItemId(from));
            var slotKind = IndexToSlot(equipIndex);
            if (!CanEquipAt(def, slotKind)) { reason = "Not compatible with slot"; return false; }

            var eqNew = WithItemId(default, def.itemId);
            eqNew = WithAmount(eqNew, 1);

            var eqOld = Equipped[equipIndex];
            Equipped[equipIndex] = eqNew;

            int left = GetAmount(from) - 1;
            from = (left > 0) ? WithAmount(from, left) : default;
            Backpack[backpackIndex] = from;

            if (!IsEmpty(eqOld))
                ServerAdd(GetItemId(eqOld), GetAmount(eqOld));

            return true;
        }

        [Server]
        public bool ServerUnequipToBackpack(int equipIndex)
        {
            if (!InEquip(equipIndex)) return false;
            var eq = Equipped[equipIndex];
            if (IsEmpty(eq)) return false;

            int added = ServerAdd(GetItemId(eq), GetAmount(eq));
            if (added <= 0) return false;

            Equipped[equipIndex] = default;
            return true;
        }

        [Server]
        public bool ServerAutoEquipFromBackpack(int backpackIndex, out string reason)
        {
            reason = null;
            if (!InBackpack(backpackIndex)) { reason = "Index out of range"; return false; }
            var from = Backpack[backpackIndex];
            if (IsEmpty(from)) { reason = "No item"; return false; }

            var def = ResolveDef(GetItemId(from));
            if (def == null || !def.IsEquipment) { reason = "Not equipment"; return false; }

            for (int i = 0; i < EquipCount; i++)
            {
                var slotKind = IndexToSlot(i);
                if ((def.equipSlots & slotKind) == 0) continue;
                if (ServerEquipFromBackpack(backpackIndex, i, out reason)) return true;
            }
            reason = "No compatible slot";
            return false;
        }

        // ---------- Client Commands (UI) ----------
        [Command] public void CmdSwapBackpack(int a, int b) => ServerSwapBackpack(a, b);
        [Command] public void CmdMoveOrMerge(int from, int to) => ServerMoveOrMerge(from, to);
        [Command] public void CmdEquipFromBackpack(int bagIndex, int equipIndex) { ServerEquipFromBackpack(bagIndex, equipIndex, out _); }
        [Command] public void CmdUnequipToBackpack(int equipIndex) { ServerUnequipToBackpack(equipIndex); }
        [Command] public void CmdAutoEquipFromBackpack(int bagIndex) { ServerAutoEquipFromBackpack(bagIndex, out _); }

        // ---- Back-compat command overloads used by older UIs ----
        [Command] public void CmdMove(int from, int to) => ServerMoveOrMerge(from, to);
        [Command] public void CmdMove(int from, int to, bool split, int amount, bool allowSwap) => ServerMoveCompat(from, to, split, amount, allowSwap);
        [Command] public void CmdMove(int from, int to, bool split) => ServerMoveCompat(from, to, split, split ? 1 : 0, true);
        [Command] public void CmdMove(int from, int to, int amount) => ServerMoveCompat(from, to, true, Math.Max(1, amount), true);
        [Command] public void CmdMove(int from, int to, bool split, int amount) => ServerMoveCompat(from, to, split, Math.Max(1, amount), true);
        [Command] public void CmdMove(int from, int to, int amount, bool allowSwap) => ServerMoveCompat(from, to, true, Math.Max(1, amount), allowSwap);

        [Command]
        public void CmdCheatAddItem(int legacyNumericId, int amount)
        {
            if (amount <= 0) return;
            _ = ServerAdd(legacyNumericId.ToString(), amount); // ServerAdd handles toast+chat
        }

        [Command]
        public void CmdCheatAddItemS(string itemId, int amount)
        {
            if (string.IsNullOrWhiteSpace(itemId) || amount <= 0) return;
            _ = ServerAdd(itemId, amount); // ServerAdd handles toast+chat
        }

        /// <summary>
        /// Owner-only RPC that shows the toast (icon loaded client-side) AND posts a Loot chat message.
        /// </summary>
        [TargetRpc]
        void TargetLootToast(NetworkConnectionToClient conn,
                             string itemId, string itemName, int amount,
                             string rarityHex, string iconPathOrNull)
        {
            // Try explicit Resources path if provided
            Sprite icon = null;
            if (!string.IsNullOrEmpty(iconPathOrNull))
                icon = Resources.Load<Sprite>(iconPathOrNull);

            // Fallback: resolve the item locally and use its icon
            if (!icon)
            {
                var def = ResolveDef(itemId);   // works on client too (Resources/lookup)
                if (def) icon = def.icon;
            }

            // On-screen toast
            LootToastWindow.PostLootToast(itemId, itemName, amount, rarityHex, icon);

            // Chat message
            LootChatBridge.PostLootReceived(itemId, itemName, amount);
        }

        // Prefer “displayName” if available; fall back to Unity name or itemId.
        private string TryGetDisplayName(ItemDef def)
        {
            if (!def) return null;

            // Try common property names via reflection, then fallback to UnityEngine.Object.name.
            try
            {
                var t = def.GetType();
                var p = t.GetProperty("displayName") ?? t.GetProperty("DisplayName") ?? t.GetProperty("title") ?? t.GetProperty("Title");
                if (p != null)
                {
                    var v = p.GetValue(def) as string;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { /* ignore */ }

            // Unity object name (often human-readable for ScriptableObject assets)
            return def.name;
        }

        [Server]
        void ServerMoveCompat(int from, int to, bool split, int amount, bool allowSwap)
        {
            if (!InBackpack(from) || !InBackpack(to) || from == to) return;

            var src = Backpack[from];
            var dst = Backpack[to];
            if (IsEmpty(src)) return;

            if (split && amount > 0)
            {
                int moving = Math.Min(amount, GetAmount(src));

                if (IsEmpty(dst))
                {
                    dst = WithItemId(default, GetItemId(src));
                    dst = WithAmount(dst, moving);
                    Backpack[to] = dst;

                    int remain = GetAmount(src) - moving;
                    src = (remain > 0) ? WithAmount(src, remain) : default;
                    Backpack[from] = src;
                    return;
                }

                if (GetItemId(dst).Equals(GetItemId(src), StringComparison.OrdinalIgnoreCase))
                {
                    var def = ResolveDef(GetItemId(src));
                    int max = Math.Max(1, def ? def.maxStack : 1);
                    int can = Math.Min(moving, Math.Max(0, max - GetAmount(dst)));
                    if (can > 0)
                    {
                        dst = WithAmount(dst, GetAmount(dst) + can);
                        Backpack[to] = dst;

                        src = WithAmount(src, GetAmount(src) - can);
                        if (GetAmount(src) <= 0) src = default;
                        Backpack[from] = src;
                    }
                    return;
                }

                if (allowSwap && moving == GetAmount(src))
                {
                    Backpack[to] = src;
                    Backpack[from] = dst;
                }
                return;
            }

            ServerMoveOrMerge(from, to);
        }

        [Command] public void CmdEquip(int bagIndex, int equipIndex) { ServerEquipFromBackpack(bagIndex, equipIndex, out _); }
        [Command] public void CmdUnequip(int equipIndex) { ServerUnequipToBackpack(equipIndex); }

        // seeders / older scripts (id variants)
        [Server] public int ServerAddItem(string itemId, int amount) => ServerAdd(itemId, amount);
        [Server] public int ServerAddItem(ItemDef def, int amount) => ServerAdd(def, amount);
        [Server] public int ServerAddItem(int legacyNumericId, int amount) => ServerAdd(legacyNumericId.ToString(), amount);
        [Server] public int ServerAdd(int legacyNumericId, int amount) => ServerAdd(legacyNumericId.ToString(), amount);
        [Server] public int ServerRemove(int legacyNumericId, int amount) => ServerRemove(legacyNumericId.ToString(), amount);

        // ---------- Helpers ----------
        bool InBackpack(int i) => i >= 0 && i < Backpack.Count;
        bool InEquip(int i) => i >= 0 && i < Equipped.Count;

        bool CanEquipAt(ItemDef def, EquipSlotAlias slot)
        {
            if (def == null || !def.IsEquipment) return false;
            return (def.equipSlots & slot) != 0;
        }

        ItemDef ResolveDef(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            // 1) Try given lookup component (reflection)
            if (itemLookup != null)
            {
                var t = itemLookup.GetType();

                var mTry = t.GetMethod("TryGetById", new[] { typeof(string), typeof(ItemDef).MakeByRefType() });
                if (mTry != null)
                {
                    object[] args = new object[] { itemId, null };
                    bool ok = (bool)mTry.Invoke(itemLookup, args);
                    if (ok) return (ItemDef)args[1];
                }

                var mGet = t.GetMethod("GetByIdOrNull", new[] { typeof(string) });
                if (mGet != null)
                {
                    var res = mGet.Invoke(itemLookup, new object[] { itemId }) as ItemDef;
                    if (res != null) return res;
                }
            }

            // 2) Resources fallback
            var direct = Resources.Load<ItemDef>($"Items/{itemId}");
            if (direct) return direct;

            var all = Resources.LoadAll<ItemDef>("Items");
            return all.FirstOrDefault(d => d && string.Equals(d.itemId, itemId, StringComparison.OrdinalIgnoreCase));
        }

        // Local rarity → hex mapping (kept in sync with chat/tooltip colors)
        static string RarityHex(ItemRarity r) => r switch
        {
            ItemRarity.Common => "#9DA3A6",
            ItemRarity.Uncommon => "#1EFF00",
            ItemRarity.Rare => "#0070DD",
            ItemRarity.Heroic => "#A335EE",
            ItemRarity.Divine => "#FF8000",
            ItemRarity.Epic => "#FFD700",
            ItemRarity.Legendary => "#FF4040",
            ItemRarity.Mythic => "#CD7F32",
            ItemRarity.Ancient => "#00E5FF",
            ItemRarity.Artifact => "#E6CC80",
            _ => "#FFFFFF"
        };

        // ----- Slot accessors (simple & safe: Convert for get) -----
        static class Slot
        {
            static readonly FieldInfo fId, fAmt;
            static readonly PropertyInfo pId, pAmt;

            static Slot()
            {
                var t = typeof(InvSlot);
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                fId = t.GetField("itemId", BF) ?? t.GetField("ItemId", BF) ?? t.GetField("id", BF) ?? t.GetField("Id", BF) ?? t.GetField("itemID", BF) ?? t.GetField("ItemID", BF);
                pId = t.GetProperty("itemId", BF) ?? t.GetProperty("ItemId", BF) ?? t.GetProperty("id", BF) ?? t.GetProperty("Id", BF) ?? t.GetProperty("itemID", BF) ?? t.GetProperty("ItemID", BF);

                fAmt = t.GetField("amount", BF) ?? t.GetField("Amount", BF) ?? t.GetField("count", BF) ?? t.GetField("Count", BF) ?? t.GetField("stack", BF) ?? t.GetField("Stack", BF) ?? t.GetField("quantity", BF) ?? t.GetField("Quantity", BF);
                pAmt = t.GetProperty("amount", BF) ?? t.GetProperty("Amount", BF) ?? t.GetProperty("count", BF) ?? t.GetProperty("Count", BF) ?? t.GetProperty("stack", BF) ?? t.GetProperty("Stack", BF) ?? t.GetProperty("quantity", BF) ?? t.GetProperty("Quantity", BF);
            }

            public static string GetId(InvSlot s)
            {
                if (fId != null) { var v = fId.GetValue(s); return v != null ? Convert.ToString(v) : null; }
                if (pId != null) { var v = pId.GetValue(s); return v != null ? Convert.ToString(v) : null; }
                return null;
            }

            public static int GetAmt(InvSlot s)
            {
                if (fAmt != null) { var v = fAmt.GetValue(s); return v != null ? Convert.ToInt32(v) : 0; }
                if (pAmt != null) { var v = pAmt.GetValue(s); return v != null ? Convert.ToInt32(v) : 0; }
                return 0;
            }

            public static InvSlot WithId(InvSlot s, string id)
            {
                object box = s;
                try
                {
                    if (fId != null)
                    {
                        var t = fId.FieldType;
                        object val = t == typeof(string) ? (object)id : Convert.ChangeType(id, t);
                        fId.SetValue(box, val);
                    }
                    else if (pId != null && pId.CanWrite)
                    {
                        var t = pId.PropertyType;
                        object val = t == typeof(string) ? (object)id : Convert.ChangeType(id, t);
                        pId.SetValue(box, val);
                    }
                }
                catch { /* ignore */ }
                return (InvSlot)box;
            }

            public static InvSlot WithAmt(InvSlot s, int amt)
            {
                object box = s;
                try
                {
                    if (fAmt != null)
                    {
                        var t = fAmt.FieldType;
                        object val = t == typeof(int) ? (object)amt : Convert.ChangeType(amt, t);
                        fAmt.SetValue(box, val);
                    }
                    else if (pAmt != null && pAmt.CanWrite)
                    {
                        var t = pAmt.PropertyType;
                        object val = t == typeof(int) ? (object)amt : Convert.ChangeType(amt, t);
                        pAmt.SetValue(box, val);
                    }
                }
                catch { /* ignore */ }
                return (InvSlot)box;
            }

            public static bool IsEmpty(InvSlot s)
            {
                var id = GetId(s);
                var a = GetAmt(s);
                return string.IsNullOrEmpty(id) || a <= 0;
            }
        }

        // Shorthands
        static bool IsEmpty(InvSlot s) => Slot.IsEmpty(s);
        static string GetItemId(InvSlot s) => Slot.GetId(s);
        static int GetAmount(InvSlot s) => Slot.GetAmt(s);
        static InvSlot WithItemId(InvSlot s, string id) => Slot.WithId(s, id);
        static InvSlot WithAmount(InvSlot s, int a) => Slot.WithAmt(s, a);
        static bool IsSameItem(InvSlot s, ItemDef d) => !IsEmpty(s) && d != null && string.Equals(GetItemId(s), d.itemId, StringComparison.OrdinalIgnoreCase);
        static bool IsSameItem(InvSlot s, string id) => !IsEmpty(s) && string.Equals(GetItemId(s), id, StringComparison.OrdinalIgnoreCase);
    }
}
