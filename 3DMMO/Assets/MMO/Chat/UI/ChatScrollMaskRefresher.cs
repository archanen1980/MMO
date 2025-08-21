using UnityEngine;
using UnityEngine.UI;

namespace MMO.Chat.UI
{
    /// Keeps RectMask2D clipping correct when Content height changes.
    /// Put this on the Scroll View (same GO as ScrollRect), wire fields in Inspector.
    [ExecuteAlways]
    public class ChatScrollMaskRefresher : MonoBehaviour
    {
        public ScrollRect scrollRect;           // assign your ScrollRect
        public RectMask2D viewportMask;         // assign the Viewport's RectMask2D
        public RectTransform content;           // assign the Content under Viewport

        [Tooltip("Auto-snap to bottom when already near bottom (< this fraction)")]
        [Range(0f, 0.2f)] public float snapIfWithin = 0.05f;

        Vector2 _lastContentSize;
        bool _inited;

        void Reset()
        {
            scrollRect = GetComponent<ScrollRect>();
            if (scrollRect)
            {
                if (!viewportMask && scrollRect.viewport)
                    viewportMask = scrollRect.viewport.GetComponent<RectMask2D>();
                if (!content && scrollRect.content)
                    content = scrollRect.content;
            }
        }

        void OnEnable() { TryInit(); ForceClipRefresh(true); }
        void OnTransformChildrenChanged() { ForceClipRefresh(true); }

        void Update()
        {
#if UNITY_EDITOR
            if (!_inited) TryInit();
#endif
            if (!_inited || !content) return;

            var sz = content.rect.size;
            if (!Mathf.Approximately(sz.y, _lastContentSize.y) || !Mathf.Approximately(sz.x, _lastContentSize.x))
            {
                _lastContentSize = sz;
                ForceClipRefresh(false);
            }
        }

        void TryInit()
        {
            if (!scrollRect) scrollRect = GetComponent<ScrollRect>();
            if (scrollRect)
            {
                if (!viewportMask && scrollRect.viewport)
                    viewportMask = scrollRect.viewport.GetComponent<RectMask2D>();
                if (!content && scrollRect.content)
                    content = scrollRect.content;
            }
            _inited = scrollRect && viewportMask && content;
        }

        public void ForceClipRefresh(bool snapBottom)
        {
            if (!_inited) TryInit();
            if (!_inited) return;

            // 1) Rebuild layouts so Rects are up-to-date
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (scrollRect.viewport)
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.viewport);

            // 2) Nudge mask & graphics to re-evaluate clip rect
            //    (equivalent to toggling the RectMask2D without the flicker)
            if (viewportMask)
            {
                // Temporarily disable/enable WITHOUT sending OnDisable path in edit mode
                var wasEnabled = viewportMask.enabled;
                viewportMask.enabled = false;
                viewportMask.enabled = wasEnabled;
            }

            // 3) Mark all children graphics dirty so they recalc under the new clip
            var graphics = content.GetComponentsInChildren<MaskableGraphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                g.SetMaterialDirty();
                g.SetVerticesDirty();
            }

            // 4) If we were already near the bottom, keep it pinned to bottom
            if (scrollRect && scrollRect.vertical && snapIfWithin >= 0f)
            {
                float pos = scrollRect.verticalNormalizedPosition;
                // With top pivot on Content, 0 = bottom, 1 = top (Unity quirk)
                if (1f - pos <= snapIfWithin || snapBottom)
                {
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }
        
    }
}
