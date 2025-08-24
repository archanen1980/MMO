using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.Chat.UI
{
    /// <summary>
    /// Attach to ChatWindow root. On left-click, raycast and if a tab Toggle under the cursor
    /// belongs to the configured tabBar, instruct ChatWindow to select it.
    /// </summary>
    public class TabLeftClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] ChatWindow window;
        [Tooltip("Parent that directly contains the tab Toggle objects (TabBarContent).")]
        [SerializeField] Transform tabBar;

        static readonly List<RaycastResult> _hits = new(16);

        void Reset()
        {
            if (!window) window = GetComponentInChildren<ChatWindow>() ?? GetComponent<ChatWindow>();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (!window) return;

            var es = EventSystem.current; if (!es) return;
            _hits.Clear();
            es.RaycastAll(e, _hits);

            Toggle hitToggle = null;
            for (int i = 0; i < _hits.Count; i++)
            {
                var t = _hits[i].gameObject.GetComponentInParent<Toggle>();
                if (!t) continue;

                // Must be one of our tab toggles (direct child of tabBar)
                if (!tabBar || (t.transform.parent == tabBar))
                {
                    hitToggle = t;
                    break;
                }
            }

            if (hitToggle != null)
                window.SelectTabByToggle(hitToggle);
        }
    }
}
