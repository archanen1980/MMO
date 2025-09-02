using UnityEngine;
#if MIRROR
using Mirror;
#endif

namespace MMO.Networking
{
    /// <summary>
    /// For a single-scene setup where a menu overlay lives in the same scene as gameplay.
    /// Automatically hides the menu when the network (host/client) is active, and shows it when stopped.
    /// Keeps things stable if StartHost/StartClient is clicked repeatedly.
    /// </summary>
    public class InSceneMenuFlow : MonoBehaviour
    {
        [Tooltip("Top-level GameObject for your menu overlay (panels, buttons, etc.)")]
        public GameObject menuRoot;

        [Tooltip("Also disable the EventSystem when the network is active (optional). If left null, we will auto-find one.")]
        public GameObject eventSystemGO;

        [Header("Runtime Creation Support")]
        [Tooltip("If your login/menu Canvas is INSTANCIATED at runtime, enable auto-discovery.")]
        public bool autoDiscoverMenuAtRuntime = true;

        [Tooltip("Optional tag to find the menu GameObject (e.g., 'UIMenu'). If empty, name/type heuristics are used.")]
        public string menuTag = "";

        [Tooltip("Heuristic names to look for if no tag is set (case-insensitive contains).")]
        public string[] candidateNames = new[] { "LoginCanvas", "Login", "MainMenu", "Menu" };

        [Tooltip("If a component type with this name exists on the menu root, it will be preferred (e.g., 'LoginCanvasRuntimeUI').")]
        public string candidateComponentTypeName = "LoginCanvasRuntimeUI";

        [Tooltip("If true, the menuRoot will be Destroyed when networking becomes active (instead of just SetActive(false)).")]
        public bool destroyMenuWhenNetActive = false;

        [Tooltip("Re-query interval (seconds) when trying to auto-discover the runtime-created menu.")]
        [Min(0.1f)] public float requeryInterval = 0.5f;

        [Tooltip("Log state changes to the Console for quick debugging.")]
        public bool verbose = true;

        static readonly System.Collections.Generic.HashSet<InSceneMenuFlow> _instances = new System.Collections.Generic.HashSet<InSceneMenuFlow>();

        /// <summary>Runtime API: register a newly created menu root so all InSceneMenuFlow instances can manage it immediately.</summary>
        public static void RegisterMenuRoot(GameObject root)
        {
            if (!root) return;
            foreach (var inst in _instances)
            {
                inst.menuRoot = root;
#if MIRROR
                inst.Apply();
#endif
                if (inst.verbose) Debug.Log($"[InSceneMenuFlow] Registered runtime menu root: {root.name}");
            }
        }

        /// <summary>Runtime API: unregister a menu root if it gets destroyed/replaced.</summary>
        public static void UnregisterMenuRoot(GameObject root)
        {
            if (!root) return;
            foreach (var inst in _instances)
            {
                if (inst && inst.menuRoot == root) inst.menuRoot = null;
            }
        }

#if MIRROR
        float _nextRequery;

        void Reset()
        {
            if (!menuRoot && transform.childCount > 0)
                menuRoot = transform.GetChild(0).gameObject;
            if (!eventSystemGO)
            {
                var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>(true);
                if (es) eventSystemGO = es.gameObject;
            }
        }

        void OnEnable()
        {
            _instances.Add(this);
            TryResolveMenu();
            Apply();
        }

        void OnDisable()
        {
            _instances.Remove(this);
        }

        void Update()
        {
            // Polling approach is simple and robust across Mirror versions
            if (autoDiscoverMenuAtRuntime && !menuRoot && Time.time >= _nextRequery)
            {
                _nextRequery = Time.time + requeryInterval;
                TryResolveMenu();
            }
            Apply();
        }

        void Apply()
        {
            bool netActive = (NetworkClient.active || NetworkServer.active);
            SetMenuVisible(!netActive);
        }

        void SetMenuVisible(bool visible)
        {
            if (menuRoot)
            {
                if (!visible && destroyMenuWhenNetActive)
                {
                    if (verbose) Debug.Log("[InSceneMenuFlow] Destroying menuRoot due to network activation.");
                    Destroy(menuRoot);
                    menuRoot = null;
                }
                else if (menuRoot.activeSelf != visible)
                {
                    if (verbose) Debug.Log($"[InSceneMenuFlow] Menu {(visible ? "shown" : "hidden")} (clientActive={NetworkClient.active}, serverActive={NetworkServer.active})");
                    menuRoot.SetActive(visible);
                }
            }

            if (eventSystemGO && eventSystemGO.activeSelf != visible)
            {
                eventSystemGO.SetActive(visible);
            }
            else if (!eventSystemGO)
            {
                // Best-effort auto-find if it appears later
                var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>(true);
                if (es) eventSystemGO = es.gameObject;
            }
        }

        void TryResolveMenu()
        {
            if (menuRoot) return;

            GameObject found = null;

            // 1) Tag
            if (!string.IsNullOrWhiteSpace(menuTag))
            {
                try { found = GameObject.FindGameObjectWithTag(menuTag); }
                catch { /* tag may not exist */ }
            }

            // 2) Type by name (e.g., LoginCanvasRuntimeUI)
            if (!found && !string.IsNullOrWhiteSpace(candidateComponentTypeName))
            {
                var allBehaviours = FindObjectsOfType<MonoBehaviour>(true);
                foreach (var mb in allBehaviours)
                {
                    if (!mb) continue;
                    var t = mb.GetType();
                    if (t.Name.IndexOf(candidateComponentTypeName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = mb.gameObject;
                        break;
                    }
                }
            }

            // 3) Canvas by name heuristics
            if (!found)
            {
                var canvases = FindObjectsOfType<Canvas>(true);
                foreach (var c in canvases)
                {
                    if (!c) continue;
                    string n = c.gameObject.name;
                    foreach (var key in candidateNames)
                    {
                        if (!string.IsNullOrEmpty(n) && n.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = c.gameObject;
                            break;
                        }
                    }
                    if (found) break;
                }
            }

            if (found)
            {
                menuRoot = found;
                if (verbose) Debug.Log($"[InSceneMenuFlow] Auto-discovered menuRoot: {menuRoot.name}");
            }
        }
#endif
    }
}
