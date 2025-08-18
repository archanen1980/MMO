// Assets/MMO/Chat/UI/ChatRigValidator.cs
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Chat.UI
{
    [ExecuteAlways]
    public class ChatRigValidator : MonoBehaviour
    {
        [Tooltip("Assign your ScrollRect here (normally on the same object).")]
        public ScrollRect scrollRect;

        [Tooltip("Set true to tint Viewport so you can SEE the mask area while testing.")]
        public bool debugTintViewport = false;

        void Reset()
        {
            scrollRect = GetComponent<ScrollRect>();
        }

        void OnEnable()
        {
            FixRig();
        }

        void Update()
        {
#if UNITY_EDITOR
            // keep rig sane while adjusting in editor
            FixRig();
#endif
        }

        [ContextMenu("Fix Chat Rig Now")]
        public void FixRig()
        {
            if (!scrollRect) { scrollRect = GetComponent<ScrollRect>(); if (!scrollRect) return; }

            // Ensure Viewport exists & assigned
            RectTransform viewport = scrollRect.viewport;
            if (!viewport)
            {
                var vpX = transform.Find("Viewport") as RectTransform;
                if (!vpX)
                {
                    var go = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
                    vpX = go.GetComponent<RectTransform>();
                    vpX.SetParent(transform, false);
                }
                viewport = vpX;
                scrollRect.viewport = viewport;
            }
            // Ensure mask + image
            var img = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            img.color = debugTintViewport ? new Color(0, 1, 0, 0.08f) : new Color(0, 0, 0, 0f);
            img.raycastTarget = true; // needed for ScrollRect
            if (!viewport.GetComponent<RectMask2D>()) viewport.gameObject.AddComponent<RectMask2D>();

            // Ensure Content exists & parented to viewport
            RectTransform content = scrollRect.content;
            if (!content)
            {
                var cX = viewport.Find("Content") as RectTransform;
                if (!cX)
                {
                    var go = new GameObject("Content", typeof(RectTransform));
                    cX = go.GetComponent<RectTransform>();
                    cX.SetParent(viewport, false);
                }
                content = cX;
                scrollRect.content = content;
            }
            else if (content.parent != viewport)
            {
                content.SetParent(viewport, false);
            }

            // Anchor + pivot corrections
            // Viewport: full stretch
            viewport.anchorMin = new Vector2(0, 0);
            viewport.anchorMax = new Vector2(1, 1);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = Vector2.zero;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;

            // Content: top-stretch, top pivot
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;

            // Layout components on Content
            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            if (vlg.spacing < 1f) vlg.spacing = 2f;

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ScrollRect settings
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

#if UNITY_EDITOR
            // force an immediate layout pass so you'll see it fix in Scene view
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scrollRect.verticalNormalizedPosition = 0f;
#endif
        }
    }
}
