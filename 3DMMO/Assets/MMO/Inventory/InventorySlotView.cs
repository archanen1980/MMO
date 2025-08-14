using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using MMO.Shared.Item;

namespace MMO.Inventory
{
    /// <summary>
    /// Single slot widget. Handles visuals, clicks, and basic drag between slots.
    /// Works for Backpack and Equipment grids.
    /// </summary>
    public class InventorySlotView : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public enum Area { Backpack, Equipment }

        [Header("UI")]
        public Image icon;
        public TMP_Text countLabel;
        public Image selectionHighlight;

        // bound data
        PlayerInventory _player;
        Area _area;
        int _index;
        string _itemId;
        int _amount;
        Func<string, ItemDef> _resolveDef;

        // drag visuals (shared)
        static GameObject s_dragIcon;
        static RectTransform s_dragRT;
        static Canvas s_canvas;
        static InventorySlotView s_dragSource;

        public void Bind(PlayerInventory player, Area area, int index,
                         string itemId, int amount, ItemDef def,
                         Func<string, ItemDef> defResolver)
        {
            _player = player;
            _area = area;
            _index = index;
            _itemId = itemId;
            _amount = amount;
            _resolveDef = defResolver;

            // visuals
            if (icon)
            {
                var sprite = def ? def.icon : null;
                icon.enabled = sprite != null;
                icon.sprite = sprite;
                icon.color = sprite ? Color.white : new Color(1, 1, 1, 0.1f);
            }

            if (countLabel)
            {
                if (amount > 1)
                {
                    countLabel.text = amount.ToString();
                    countLabel.enabled = true;
                }
                else
                {
                    countLabel.text = "";
                    countLabel.enabled = false;
                }
            }

            if (selectionHighlight) selectionHighlight.enabled = false;

            // tooltips etc. (optional)
            // You can integrate your tooltip system here using def/displayName/description.
        }

        // ---------- Clicks ----------
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_player == null) return;

            // Left click behavior:
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (_area == Area.Backpack)
                {
                    // Auto-equip if equipment; otherwise no-op
                    _player.CmdAutoEquipFromBackpack(_index);
                }
                else // Equipment
                {
                    // Unequip back to backpack
                    _player.CmdUnequip(_index);
                }
            }

            // Right-click could open context (split/drop); implement as needed.
        }

        // ---------- Drag & Drop ----------
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_player == null) return;
            if (_area == Area.Equipment && string.IsNullOrEmpty(_itemId))
                return; // nothing to drag
            if (_area == Area.Backpack && string.IsNullOrEmpty(_itemId))
                return;

            s_dragSource = this;
            CreateDragIcon();
            UpdateDragIcon(eventData);
            if (selectionHighlight) selectionHighlight.enabled = true;
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
            if (target != null && target._player == _player)
            {
                HandleDropOn(target);
            }

            DestroyDragIcon();
            s_dragSource = null;
        }

        void HandleDropOn(InventorySlotView target)
        {
            if (s_dragSource == null || _player == null) return;

            // Backpack -> Backpack : move/merge
            if (_area == Area.Backpack && target._area == Area.Backpack)
            {
                _player.CmdMove(_index, target._index);
                return;
            }

            // Backpack -> Equipment : equip to that slot
            if (_area == Area.Backpack && target._area == Area.Equipment)
            {
                _player.CmdEquip(_index, target._index);
                return;
            }

            // Equipment -> Backpack : unequip (goes to backpack best slot)
            if (_area == Area.Equipment && target._area == Area.Backpack)
            {
                _player.CmdUnequip(_index);
                return;
            }

            // Equipment -> Equipment : simple swap not supported directly; do unequip then equip would be two steps.
            // You can extend this by: _player.CmdUnequip(_index); then find the item in backpack and CmdEquip(...) if needed.
        }

        // ---------- Drag icon helpers ----------
        void CreateDragIcon()
        {
            if (s_canvas == null)
            {
                // pick any top-level canvas in the scene for overlay
                s_canvas = FindObjectOfType<Canvas>();
            }
            if (s_dragIcon != null) Destroy(s_dragIcon);

            s_dragIcon = new GameObject("DraggingIcon", typeof(CanvasGroup), typeof(RectTransform), typeof(Image));
            s_dragRT = s_dragIcon.GetComponent<RectTransform>();
            var cg = s_dragIcon.GetComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.alpha = 0.8f;

            var img = s_dragIcon.GetComponent<Image>();
            img.raycastTarget = false;
            img.sprite = icon ? icon.sprite : null;
            img.enabled = img.sprite != null;

            var parent = s_canvas ? s_canvas.transform : transform.root;
            s_dragIcon.transform.SetParent(parent, false);
            s_dragRT.sizeDelta = icon ? (Vector2)(icon.rectTransform.rect.size) : new Vector2(64, 64);
        }

        void UpdateDragIcon(PointerEventData ev)
        {
            if (s_dragRT == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                s_dragRT.parent as RectTransform, ev.position, ev.pressEventCamera, out var local);
            s_dragRT.anchoredPosition = local;
        }

        void DestroyDragIcon()
        {
            if (s_dragIcon != null) Destroy(s_dragIcon);
            s_dragIcon = null;
            s_dragRT = null;
        }

        InventorySlotView RaycastForSlot(PointerEventData ev)
        {
            var results = UIRaycast(ev);
            foreach (var r in results)
            {
                var slot = r.gameObject.GetComponentInParent<InventorySlotView>();
                if (slot != null) return slot;
            }
            return null;
        }

        static System.Collections.Generic.List<RaycastResult> s_raycast = new System.Collections.Generic.List<RaycastResult>(16);
        static System.Collections.Generic.List<RaycastResult> UIRaycast(PointerEventData ev)
        {
            s_raycast.Clear();
            EventSystem.current?.RaycastAll(ev, s_raycast);
            return s_raycast;
        }
    }
}
