using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Chat
{
    /// Kills legacy chat UIs that spawn at runtime (before they grab input).
    /// Put this on a persistent GameObject in your HUD scene.
    public class ChatDisabler : MonoBehaviour
    {
        [Header("Match by type names (substring match, case-insensitive)")]
        [Tooltip("Components to destroy if found. Add the old chat script class names (or parts of them).")]
        public string[] typeNameContains = { "ChatUI", "OldChat", "ChatWindow", "ChatManager" };

        [Header("Match by GameObject names (substring match)")]
        public string[] objectNameContains = { "Chat", "ChatUI", "ChatWindow" };

        [Header("When to run")]
        public bool onAwake = true;
        public bool onSceneLoaded = true;   // catch additively loaded scenes
        public bool repeatOnStart = true;   // second pass after early spawners

        void Awake()
        {
            if (onAwake) TryDisable();
            if (onSceneLoaded) SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Start()
        {
            if (repeatOnStart) TryDisable();
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode) => TryDisable();

        public void TryDisable()
        {
            int killed = 0;

            // 1) Kill by component type name (excluding our new system in MMO.Chat*)
            foreach (var mb in FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null) continue;

                var t = mb.GetType();
                string ns = t.Namespace ?? "";
                if (ns.StartsWith("MMO.Chat")) continue; // keep our new chat

                string typeName = t.FullName ?? t.Name;
                if (typeNameContains.Any(p => typeName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    // destroy whole GO if it looks like a chat root, else just this component
                    var go = mb.gameObject;
                    if (objectNameContains.Any(p => go.name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Destroy(go);
                        killed++;
                        continue;
                    }
                    Destroy(mb);
                    killed++;
                }
            }

            // 2) Kill by GameObject name
            foreach (var tr in FindObjectsOfType<Transform>(true))
            {
                var go = tr.gameObject;
                if (!go) continue;

                // skip anything under our new chat namespace marker (optional)
                if (go.GetComponentInParent<Chat.UI.ChatWindow>(true)) continue;

                if (objectNameContains.Any(p => go.name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    // If it’s clearly a UI root with a Canvas, drop it
                    if (go.GetComponent<Canvas>() || go.GetComponent<UnityEngine.UI.ScrollRect>())
                    {
                        Destroy(go);
                        killed++;
                    }
                }
            }

#if UNITY_EDITOR
            if (killed > 0) Debug.Log($"[ChatDisabler] Removed {killed} legacy chat objects/components.");
#endif
        }
    }
}
