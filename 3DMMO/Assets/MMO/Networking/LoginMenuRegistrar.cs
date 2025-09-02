using UnityEngine;

namespace MMO.Networking
{
    /// <summary>
    /// Attach this to the root GameObject of your runtime-created login/menu Canvas.
    /// It registers itself with InSceneMenuFlow so the menu can be hidden/destroyed as soon as Host/Client starts.
    /// </summary>
    public class LoginMenuRegistrar : MonoBehaviour
    {
        [Tooltip("Optional: assign a tag (e.g., 'UIMenu') so other systems can find the menu easily.")]
        public string setTag = "";

        void Awake()
        {
            if (!string.IsNullOrWhiteSpace(setTag))
            {
                try { gameObject.tag = setTag; } catch { /* tag may not exist */ }
            }
            InSceneMenuFlow.RegisterMenuRoot(gameObject);
        }

        void OnDestroy()
        {
            InSceneMenuFlow.UnregisterMenuRoot(gameObject);
        }
    }
}
