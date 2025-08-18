using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using MMO.Shared.Item;
using TMPro;

namespace MMO.Inventory
{
    /// <summary>
    /// Runtime Inventory UI: builds equipment + backpack grids.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        [Header("Refs")]
        public PlayerInventory player;
        public Transform equipmentGrid;
        public Transform backpackGrid;
        public InventorySlotView slotPrefab;

        [Header("Optional")]
        public TMP_Text title;

        bool _subscribed;
        bool _isRebuilding;
        void Awake()
        {
            if (title != null && string.IsNullOrWhiteSpace(title.text))
                title.text = "Inventory";
        }

        void OnEnable()
        {
            EnsurePlayerBound();
            Subscribe();
            RebuildUI();
        }

        void OnDisable() => Unsubscribe();

        void EnsurePlayerBound()
        {
            if (player != null) return;

            if (NetworkClient.active && NetworkClient.localPlayer != null)
            {
                player = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
                if (player) return;
            }
            player = FindObjectOfType<PlayerInventory>();
        }

        void Subscribe()
        {
            if (player == null || _subscribed) return;
            player.OnClientInventoryChanged += RebuildUI;
            player.OnBackpackChanged        += RebuildUI;
            player.OnEquippedChanged        += RebuildUI;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (player == null || !_subscribed) return;
            player.OnClientInventoryChanged -= RebuildUI;
            player.OnBackpackChanged        -= RebuildUI;
            player.OnEquippedChanged        -= RebuildUI;
            _subscribed = false;
        }

        public void RebuildUI()
        {
            if (player == null || slotPrefab == null || equipmentGrid == null || backpackGrid == null)
                return;

            int equipCount = player != null ? player.EquipCount : PlayerInventory.DefaultEquipCount;

            BuildGrid(equipmentGrid, equipCount);
            BuildGrid(backpackGrid, player.Backpack.Count);
            if (_isRebuilding) return;
            _isRebuilding = true;
            try
            {
                if (player == null || slotPrefab == null || equipmentGrid == null || backpackGrid == null)
                    return;
                // equipment
                for (int i = 0; i < equipCount; i++)
                {
                    var view = equipmentGrid.GetChild(i).GetComponent<InventorySlotView>();
                    BindSlot(view, InventorySlotView.Area.Equipment, i, GetSlot(player.Equipped, i));
                }

                // backpacka
                for (int i = 0; i < player.Backpack.Count; i++)
                {
                    var view = backpackGrid.GetChild(i).GetComponent<InventorySlotView>();
                    BindSlot(view, InventorySlotView.Area.Backpack, i, GetSlot(player.Backpack, i));
                }
            }
            finally { _isRebuilding = false; }
        }

        void BuildGrid(Transform root, int desired)
        {
            while (root.childCount < desired)
                Instantiate(slotPrefab, root, false);
            for (int i = root.childCount - 1; i >= desired; i--)
                Destroy(root.GetChild(i).gameObject);
        }

        void BindSlot(InventorySlotView view, InventorySlotView.Area area, int index, object slotBoxed)
        {
            if (view == null) return;

            string itemId = SlotAccess.GetId(slotBoxed);
            int amount    = SlotAccess.GetAmt(slotBoxed);

            var def = ResolveDef(itemId);

            view.Bind(
                player: player,
                area: area,
                index: index,
                itemId: itemId,
                amount: amount,
                def: def,
                defResolver: ResolveDef
            );
        }

        ItemDef ResolveDef(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            if (player != null && player.itemLookup != null)
            {
                var t = player.itemLookup.GetType();
                var mTry = t.GetMethod("TryGetById", new[] { typeof(string), typeof(ItemDef).MakeByRefType() });
                if (mTry != null)
                {
                    object[] args = new object[] { itemId, null };
                    bool ok = (bool)mTry.Invoke(player.itemLookup, args);
                    if (ok) return (ItemDef)args[1];
                }
                var mGet = t.GetMethod("GetByIdOrNull", new[] { typeof(string) });
                if (mGet != null)
                {
                    var res = mGet.Invoke(player.itemLookup, new object[] { itemId }) as ItemDef;
                    if (res != null) return res;
                }
            }

            var direct = Resources.Load<ItemDef>($"Items/{itemId}");
            if (direct) return direct;

            var all = Resources.LoadAll<ItemDef>("Items");
            return all.FirstOrDefault(d => d && string.Equals(d.itemId, itemId, StringComparison.OrdinalIgnoreCase));
        }

        static class SlotAccess
        {
            static readonly FieldInfo fId, fAmt;
            static readonly PropertyInfo pId, pAmt;

            static SlotAccess()
            {
                var t = typeof(InvSlot);
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                fId = t.GetField("itemId", BF) ?? t.GetField("ItemId", BF) ?? t.GetField("id", BF) ?? t.GetField("Id", BF) ?? t.GetField("itemID", BF) ?? t.GetField("ItemID", BF);
                pId = t.GetProperty("itemId", BF) ?? t.GetProperty("ItemId", BF) ?? t.GetProperty("id", BF) ?? t.GetProperty("Id", BF) ?? t.GetProperty("itemID", BF) ?? t.GetProperty("ItemID", BF);

                fAmt = t.GetField("amount", BF) ?? t.GetField("Amount", BF) ?? t.GetField("count", BF) ?? t.GetField("Count", BF) ?? t.GetField("stack", BF) ?? t.GetField("Stack", BF) ?? t.GetField("quantity", BF) ?? t.GetField("Quantity", BF);
                pAmt = t.GetProperty("amount", BF) ?? t.GetProperty("Amount", BF) ?? t.GetProperty("count", BF) ?? t.GetProperty("Count", BF) ?? t.GetProperty("stack", BF) ?? t.GetProperty("Stack", BF) ?? t.GetProperty("quantity", BF) ?? t.GetProperty("Quantity", BF);
            }

            public static string GetId(object boxedInvSlot)
            {
                if (boxedInvSlot == null) return null;
                if (fId != null) { var v = fId.GetValue(boxedInvSlot); return v != null ? Convert.ToString(v) : null; }
                if (pId != null) { var v = pId.GetValue(boxedInvSlot); return v != null ? Convert.ToString(v) : null; }
                return null;
            }

            public static int GetAmt(object boxedInvSlot)
            {
                if (boxedInvSlot == null) return 0;
                if (fAmt != null) { var v = fAmt.GetValue(boxedInvSlot); return v != null ? Convert.ToInt32(v) : 0; }
                if (pAmt != null) { var v = pAmt.GetValue(boxedInvSlot); return v != null ? Convert.ToInt32(v) : 0; }
                return 0;
            }
        }

        static object GetSlot(Mirror.SyncList<InvSlot> list, int index)
        {
            if (list == null || index < 0 || index >= list.Count) return null;
            InvSlot s = list[index];
            return s;
        }

        public void OnClickClose() => gameObject.SetActive(false);
    }
}
