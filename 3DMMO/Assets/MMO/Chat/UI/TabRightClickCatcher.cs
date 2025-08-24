// Assets/MMO/Chat/UI/TabRightClickCatcher.cs
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.Chat.UI
{
    /// <summary>
    /// Put this on the ChatWindow root. Opens the Tab context menu
    /// when right-click occurs over a tab Toggle inside tabBarContent.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Chat/Tab Right Click Catcher (Root)")]
    public class TabRightClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("Owning ChatWindow")]
        public ChatWindow owner;

        [Tooltip("Viewport for the tab row (e.g., Header/TabScroll/Viewport)")]
        public RectTransform tabBarViewport;

        [Tooltip("Content that holds the tab Toggles (e.g., .../Viewport/TabBarContent)")]
        public Transform tabBarContent;

        void Awake()
        {
            if (!owner) owner = GetComponent<ChatWindow>() ?? GetComponentInParent<ChatWindow>();
            if (!tabBarViewport && owner)
                tabBarViewport = owner.transform.Find("Header/TabScroll/Viewport") as RectTransform;
            if (!tabBarContent && owner)
                tabBarContent = owner.transform.Find("Header/TabScroll/Viewport/TabBarContent");

            // Ensure root can receive clicks (transparent & raycastable is fine)
            var g = GetComponent<Graphic>();
            if (!g)
            {
                var img = gameObject.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = true;
            }
            else g.raycastTarget = true;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right) return;
            if (!owner || !tabBarContent || !tabBarViewport) return;

            var canvas = tabBarViewport.GetComponentInParent<Canvas>();
            var cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            // Must be inside the tab-bar viewport
            if (!RectTransformUtility.RectangleContainsScreenPoint(tabBarViewport, e.position, cam))
                return;

            int tabIndex = FindTabIndexAt(e.position, cam);
            if (tabIndex < 0) return;

            // We found a tab: open the Tab menu
            owner.ShowContextMenu("Tab", e.position, tabIndex);
        }

        int FindTabIndexAt(Vector2 screenPos, Camera cam)
        {
            if (!tabBarContent) return -1;
            int logical = 0;
            for (int i = 0; i < tabBarContent.childCount; i++)
            {
                var child = tabBarContent.GetChild(i);
                var tog = child.GetComponent<Toggle>();
                if (!tog) continue; // skip non-tab children
                var rt = child as RectTransform;
                if (rt && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam))
                    return logical;
                logical++;
            }
            return -1;
        }
    }
}
