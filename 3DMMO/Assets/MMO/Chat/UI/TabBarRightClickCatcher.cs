// Assets/MMO/Chat/UI/ChatTabClickCatchers.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Chat.UI
{
    // Right-click on the TAB BAR background → open Add New Tab menu
    public sealed class TabBarRightClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public ChatWindow owner;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right) return;
            owner?.ShowTabBarMenu(e.position);
#if UNITY_EDITOR
            // quick debug: what UI was under the mouse?
            var top = TopUIUnderMouse();
            if (top) Debug.Log($"[Chat] TabBar right-click hit: {top.name} (path: {GetPath(top.transform)})", top);
#endif
        }

#if UNITY_EDITOR
        static GameObject TopUIUnderMouse()
        {
            if (!EventSystem.current) return null;
            var ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }
        static string GetPath(Transform t)
        {
            var s = t.name;
            while (t.parent) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
#endif
    }

    // Right-click on a TAB → rename + channels menu
    public sealed class TabRightClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public ChatWindow owner;
        public int tabIndex;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right) return;
            owner?.ShowTabMenu(tabIndex, e.position);
        }
    }
}
