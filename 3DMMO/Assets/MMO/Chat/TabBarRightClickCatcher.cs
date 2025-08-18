// Assets/MMO/Chat/UI/ChatTabClickCatchers.cs
using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Chat.UI
{
    // Right-click on the TAB BAR background → open "Add New Tab" menu
    public sealed class TabBarRightClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public ChatWindow owner;
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right)
            {
                owner?.ShowTabBarMenu(e.position);
                // Debug.Log("[Chat] TabBarRightClickCatcher: right-click");
            }
        }
    }

    // Right-click on a TAB → rename + channels menu
    public sealed class TabRightClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public ChatWindow owner;
        public int tabIndex;
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right)
            {
                owner?.ShowTabMenu(tabIndex, e.position);
                // Debug.Log($"[Chat] TabRightClickCatcher: right-click on tab {tabIndex}");
            }
        }
    }
}
