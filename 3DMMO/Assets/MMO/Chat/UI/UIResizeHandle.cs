using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Chat.UI
{
    /// Put this on a corner handle (e.g., bottom-right) to resize the window.
    public class UIResizeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [SerializeField] RectTransform target;
        [SerializeField] Vector2 minSize = new Vector2(260, 160);
        [SerializeField] Canvas canvas;

        Vector2 _startSize;
        Vector2 _startMouse;

        void Reset()
        {
            if (!target) target = transform.parent as RectTransform;
            if (!canvas) canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!target || !canvas) return;
            _startSize = target.sizeDelta;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, e.position, canvas.worldCamera, out _startMouse);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!target || !canvas) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, e.position, canvas.worldCamera, out var cur);
            var delta = cur - _startMouse;
            var newSize = _startSize + new Vector2(delta.x, -delta.y); // bottom-right handle
            newSize.x = Mathf.Max(minSize.x, newSize.x);
            newSize.y = Mathf.Max(minSize.y, newSize.y);
            target.sizeDelta = newSize;
        }
    }
}
