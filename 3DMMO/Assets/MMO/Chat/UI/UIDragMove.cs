using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Chat.UI
{
    /// Put this on the Chat header bar (or the whole window) to drag the RectTransform.
    public class UIDragMove : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [SerializeField] RectTransform target; // window root
        [SerializeField] Canvas canvas;

        Vector2 _offset;

        void Reset()
        {
            if (!target) target = transform as RectTransform;
            if (!canvas) canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!target || !canvas) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(target, e.position, canvas.worldCamera, out var local);
            _offset = local;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!target || !canvas) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, e.position, canvas.worldCamera, out var canvasLocal);
            target.anchoredPosition = canvasLocal - _offset;
        }
    }
}
