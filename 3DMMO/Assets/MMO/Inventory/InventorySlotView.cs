using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MMO.Inventory
{
    public class InventorySlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IDropHandler
    {
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI countText;

        PlayerInventory inv;
        ContainerKind kind;
        int index;

        // Drag UI
        Image dragGhost;
        Canvas canvas;

        public void Bind(PlayerInventory inv, ContainerKind kind, int index, Image dragGhost, Canvas canvas)
        {
            this.inv = inv;
            this.kind = kind;
            this.index = index;
            this.dragGhost = dragGhost;
            this.canvas = canvas;

            // Update visuals
            var slot = (kind == ContainerKind.Backpack) ? inv.Backpack[index] : inv.Equipment[index];
            if (slot.IsEmpty)
            {
                icon.enabled = false;
                countText.text = "";
            }
            else
            {
                var def = FindObjectOfType<ResourcesItemLookup>()?.GetById(slot.itemId);
                icon.enabled = true;
                icon.sprite = def ? def.icon : null;
                countText.text = (slot.count > 1) ? slot.count.ToString() : "";
            }
        }

        // Right-click quick move (to first valid destination)
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            if (!inv) return;

            // Quick move: if from backpack, try equipment if equippable, else to first empty stack; if from equipment, try backpack.
            // Keep it simple: right-click toggles between backpack<->equipment same index if possible, else tries first free.
            if (kind == ContainerKind.Equipment)
            {
                // move to backpack same index (or first free)
                TryMove(ContainerKind.Equipment, index, ContainerKind.Backpack, index, 0);
            }
            else
            {
                // to equipment same index (often makes sense for hotbar=equipment layouts); if fails, no-op
                TryMove(ContainerKind.Backpack, index, ContainerKind.Equipment, index, 1);
            }
        }

        Vector2 dragOffset;

        public void OnBeginDrag(PointerEventData e)
        {
            if (!inv) return;
            var slot = (kind == ContainerKind.Backpack) ? inv.Backpack[index] : inv.Equipment[index];
            if (slot.IsEmpty) return;

            dragGhost.gameObject.SetActive(true);
            dragGhost.sprite = FindObjectOfType<ResourcesItemLookup>()?.GetById(slot.itemId)?.icon;
            dragGhost.SetNativeSize();

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform, e.position, e.pressEventCamera, out var localPoint);

            dragGhost.rectTransform.anchoredPosition = localPoint;
            dragOffset = dragGhost.rectTransform.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!dragGhost.gameObject.activeSelf) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform, e.position, e.pressEventCamera, out var localPoint);

            dragGhost.rectTransform.anchoredPosition = localPoint;
        }

        public void OnEndDrag(PointerEventData e)
        {
            dragGhost.gameObject.SetActive(false);
        }

        // When another slot is dropped onto this one
        public void OnDrop(PointerEventData e)
        {
            var payload = e.pointerDrag ? e.pointerDrag.GetComponent<InventorySlotView>() : null;
            if (!payload || payload == this) return;

            ushort amount = 0; // 0 => move all
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                amount = (ushort)Mathf.Max(1, PayloadCount(payload) / 2);

            TryMove(payload.kind, payload.index, kind, index, amount);
        }

        int PayloadCount(InventorySlotView payload)
        {
            var slot = (payload.kind == ContainerKind.Backpack) ? payload.inv.Backpack[payload.index] : payload.inv.Equipment[payload.index];
            return slot.count;
        }

        void TryMove(ContainerKind srcKind, int srcIndex, ContainerKind dstKind, int dstIndex, ushort amount)
        {
            inv.CmdMove(srcKind, srcIndex, dstKind, dstIndex, amount);
        }
    }
}
