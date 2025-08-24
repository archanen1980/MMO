using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using MMO.Shared.Item;   // ItemDef
using MMO.Inventory;     // namespace alignment
using MMO.Inventory.UI;  // ItemTooltipCursor (unified tooltip)

namespace MMO.Inventory
{
    /// <summary>
    /// Single slot widget for Backpack & Equipment.
    /// - Visuals: icon + stack count (+ optional empty overlay/highlight)
    /// - Click: Backpack → auto-equip if possible, Equipment → unequip
    /// - Drag: Backpack→Backpack move/merge; Backpack→Equipment equip; Equipment→Backpack unequip
    /// - Tooltip: now uses ItemTooltipCursor.ShowAtCursor(ItemDef) for unified tooltip (rarity-colors, etc.)
    /// </summary>
    public class InventorySlotView : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        // Kept for InventoryUI compatibility
        public enum Area { None = 0, Backpack = 1, Equipment = 2 }

        [Header("Identity (set by InventoryUI.Bind)")]
        [SerializeField] Area _area = Area.None;
        [SerializeField] int _index = -1;

        [Header("UI (optional)")]
        public Image icon;
        public TMP_Text countLabel;
        public GameObject emptyOverlay;
        public Image selectionHighlight;

        // Bound state
        PlayerInventory _player;
        string _itemId;
        int _amount;
        ItemDef _def;                         // cached for tooltip
        Func<string, ItemDef> _resolveDef;    // fallback resolver

        // Shared drag helpers
        static GameObject s_dragIcon;
        static RectTransform s_dragRT;
        static Canvas s_canvas;
        static Camera s_uiCam;
        static InventorySlotView s_dragSource;
        static readonly List<RaycastResult> s_raycast = new List<RaycastResult>(16);

        // ---------------------------------------------------------------------
        // Public API — called by InventoryUI
        // ---------------------------------------------------------------------
        public void Bind(PlayerInventory player, Area area, int index,
                         string itemId, int amount, ItemDef def,
                         Func<string, ItemDef> defResolver)
        {
            _player = player;
            _area = area;
            _index = index;
            _itemId = itemId;
            _amount = amount;
            _def = def;
            _resolveDef = defResolver;

            // Visuals
            if (icon)
            {
                var sprite = def ? def.icon : null;
                icon.enabled = sprite != null;
                icon.sprite = sprite;
                icon.color = sprite ? Color.white : new Color(1, 1, 1, 0.1f);
            }

            if (countLabel)
            {
                if (amount > 1) { countLabel.text = amount.ToString(); countLabel.enabled = true; }
                else { countLabel.text = string.Empty; countLabel.enabled = false; }
            }

            if (emptyOverlay) emptyOverlay.SetActive(string.IsNullOrEmpty(itemId) || amount <= 0);
            if (selectionHighlight) selectionHighlight.enabled = false;

            // Cache canvas for drag ghost
            if (s_canvas == null)
            {
                s_canvas = GetComponentInParent<Canvas>();
                if (s_canvas && s_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    s_uiCam = s_canvas.worldCamera;
            }
        }

        public void Clear()
        {
            _itemId = null;
            _amount = 0;
            _def = null;

            if (icon) { icon.sprite = null; icon.enabled = false; icon.color = new Color(1, 1, 1, 0.1f); }
            if (countLabel) { countLabel.text = string.Empty; countLabel.enabled = false; }
            if (emptyOverlay) emptyOverlay.SetActive(true);
            if (selectionHighlight) selectionHighlight.enabled = false;
        }

        // ---------------------------------------------------------------------
        // Click: quick equip/unequip
        // ---------------------------------------------------------------------
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_player == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;

            if (_area == Area.Backpack)
            {
                // Auto-equip if it’s equippable; server validates kind/slot
                _player.CmdAutoEquipFromBackpack(_index);
            }
            else if (_area == Area.Equipment)
            {
                // Unequip to backpack (server finds destination)
                _player.CmdUnequip(_index);
            }
        }

        // ---------------------------------------------------------------------
        // Drag: move/equip/unequip
        // ---------------------------------------------------------------------
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_player == null) return;
            if (string.IsNullOrEmpty(_itemId) || _amount <= 0) return;

            s_dragSource = this;
            CreateDragIcon();
            UpdateDragIcon(eventData);
            if (selectionHighlight) selectionHighlight.enabled = true;

            // hide tooltip while dragging
            ItemTooltipCursor.HideIfAny();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (s_dragIcon == null) return;
            UpdateDragIcon(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (selectionHighlight) selectionHighlight.enabled = false;

            var target = RaycastForSlot(eventData);
            if (target != null)
                HandleDropOn(target);

            DestroyDragIcon();
            s_dragSource = null;
        }

        void HandleDropOn(InventorySlotView target)
        {
            if (s_dragSource == null || _player == null) return;

            // Backpack -> Backpack : move/merge
            if (_area == Area.Backpack && target._area == Area.Backpack)
            {
                _player.CmdMove(_index, target._index);            // ✅ 2-arg signature in PlayerInventory
                return;
            }

            // Backpack -> Equipment : equip to that equipment index
            if (_area == Area.Backpack && target._area == Area.Equipment)
            {
                _player.CmdEquip(_index, target._index);           // ✅ equip specific slot
                return;
            }

            // Equipment -> Backpack : unequip (server picks backpack dest)
            if (_area == Area.Equipment && target._area == Area.Backpack)
            {
                _player.CmdUnequip(_index);
                return;
            }

            // Equipment -> Equipment : not supported here
        }

        // ---------------------------------------------------------------------
        // Tooltip (cursor-follow) — unified via ItemTooltipCursor.ShowAtCursor(ItemDef)
        // ---------------------------------------------------------------------
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(_itemId) || _amount <= 0) return;

            var def = _def ?? _resolveDef?.Invoke(_itemId);
            if (!def) return;

            // Unified: rarity-colored title + consistent body via ItemTooltipComposer
            ItemTooltipCursor.ShowAtCursor(def);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ItemTooltipCursor.HideIfAny();
        }

        void OnDisable() => ItemTooltipCursor.HideIfAny();

        // ---------------------------------------------------------------------
        // Drag icon helpers
        // ---------------------------------------------------------------------
        void CreateDragIcon()
        {
            if (s_canvas == null) return;

            s_dragIcon = new GameObject("DragIcon", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            s_dragRT = s_dragIcon.GetComponent<RectTransform>();
            s_dragRT.SetParent(s_canvas.transform, worldPositionStays: false);

            var img = s_dragIcon.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = icon ? icon.sprite : null;

            var cg = s_dragIcon.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;
            cg.alpha = 0.9f;

            // size similar to slot icon
            s_dragRT.sizeDelta = icon ? (Vector2)icon.rectTransform.rect.size : new Vector2(64, 64);
        }

        void UpdateDragIcon(PointerEventData eventData)
        {
            if (s_dragRT == null || s_canvas == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                s_canvas.transform as RectTransform, eventData.position, s_uiCam, out var local);
            s_dragRT.anchoredPosition = local;
        }

        void DestroyDragIcon()
        {
            if (s_dragIcon) Destroy(s_dragIcon);
            s_dragIcon = null;
            s_dragRT = null;
        }

        InventorySlotView RaycastForSlot(PointerEventData ev)
        {
            s_raycast.Clear();
            EventSystem.current?.RaycastAll(ev, s_raycast);
            for (int i = 0; i < s_raycast.Count; i++)
            {
                var slot = s_raycast[i].gameObject.GetComponentInParent<InventorySlotView>();
                if (slot != null) return slot;
            }
            return null;
        }
    }
}
