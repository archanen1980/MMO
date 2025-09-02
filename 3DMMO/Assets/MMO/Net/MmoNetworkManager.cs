using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace MMO.Net
{
    /// <summary>
    /// Hardened NetworkManager that never throws if spawn points are missing.
    /// - Uses Mirror's NetworkStartPosition if available
    /// - Otherwise uses the first valid Transform in the inspector array
    /// - Otherwise finds a GameObject by tag (default "SpawnPoint")
    /// - Otherwise spawns at Vector3.zero with identity rotation
    ///
    /// Also supports round-robin spawn cycling and safe tag checks.
    /// </summary>
    public class MmoNetworkManager : NetworkManager
    {
        [Header("Spawning (Inspector)")]
        [Tooltip("Optional explicit spawn points. If empty, we fall back to NetworkStartPosition or a tag search.")]
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();

        [Tooltip("If no NetworkStartPosition and no inspector points, try this tag.")]
        [SerializeField] private string spawnTag = "SpawnPoint";

        [Tooltip("If true, we'll attempt a tag-based search for spawn points. If the tag isn't defined, we skip it without throwing.")]
        [SerializeField] private bool useTagSearch = true;

        [Header("Options")]
        [Tooltip("Cycle through spawn points instead of always using the first.")]
        [SerializeField] private bool roundRobin = true;

        [Tooltip("Log helpful diagnostics to the Console during spawns.")]
        [SerializeField] private bool verboseLogs = true;

        int _nextIndex;

        [Header("Discovery")]
        [Tooltip("Search for spawn points on Awake (NetworkStartPosition + tag) and cache them before any spawn occurs.")]
        [SerializeField] private bool scanOnAwake = true;

        void Awake()
        {
            if (scanOnAwake) ScanSpawnPoints("Awake");
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            ScanSpawnPoints("OnServerSceneChanged");
        }

        // --- Lifecycle ------------------------------------------------------------------
        public override void OnStartServer()
        {
            base.OnStartServer();
            // Ensure we have all spawn points cached when the server starts
            ScanSpawnPoints("OnStartServer");
            // scrub null entries defensively
            if (spawnPoints != null)
                spawnPoints = spawnPoints.Where(t => t != null).ToArray();
        }

        // --- Scanner --------------------------------------------------------------------
        void ScanSpawnPoints(string reason)
        {
            try
            {
                var list = new List<Transform>(16);

                // From inspector first
                if (spawnPoints != null)
                {
                    foreach (var t in spawnPoints) if (t) list.Add(t);
                }

                // All NetworkStartPosition in scene (works great when you add that component to markers)
                var nsp = FindObjectsOfType<NetworkStartPosition>(true);
                foreach (var s in nsp) if (s && s.transform && !list.Contains(s.transform)) list.Add(s.transform);

                // Any GameObject with the configured tag (safely)
                if (useTagSearch && !string.IsNullOrWhiteSpace(spawnTag))
                {
                    if (TryFindByTag(spawnTag, out var tagged))
                    {
                        foreach (var go in tagged) if (go && go.transform && !list.Contains(go.transform)) list.Add(go.transform);
                    }
                    else if (verboseLogs)
                    {
                        Debug.LogWarning($"[MmoNetworkManager] Tag '{spawnTag}' not defined or no objects found; skipping tag search.");
                    }
                }

                // De-dupe & commit
                spawnPoints = list.Where(t => t != null).Distinct().ToArray();
                if (verboseLogs)
                    Debug.Log($"[MmoNetworkManager] ScanSpawnPoints({reason}) found {spawnPoints.Length} spawn(s).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MmoNetworkManager] ScanSpawnPoints({reason}) failed: {ex.Message}");
            }
        }

        // --- Player Add -----------------------------------------------------------------
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            Transform start = ResolveSpawn();
            Vector3 pos = start ? start.position : Vector3.zero;
            Quaternion rot = start ? start.rotation : Quaternion.identity;

            if (verboseLogs)
            {
                string where = start ? $"{start.name} at {pos}" : "origin (no spawn found)";
                Debug.Log($"[MmoNetworkManager] Spawning player for conn={conn.connectionId} at {where}");
            }

            GameObject player = Instantiate(playerPrefab, pos, rot);
            NetworkServer.AddPlayerForConnection(conn, player);
        }

        // --- Helpers --------------------------------------------------------------------
        Transform ResolveSpawn()
        {
            // 1) Mirror built-in: NetworkStartPosition
            var startPos = GetStartPosition();
            if (startPos)
                return startPos;

            // 2) Inspector-provided list
            if (spawnPoints == null || spawnPoints.Length == 0) ScanSpawnPoints("ResolveSpawn");
            var t = NextFromInspectorList();
            if (t)
                return t;

            // 3) Find by tag (safely)
            if (useTagSearch && !string.IsNullOrWhiteSpace(spawnTag))
            {
                if (TryFindByTag(spawnTag, out var tagged) && tagged.Length > 0)
                    return tagged[0].transform;
            }

            // 4) Nothing found
            if (verboseLogs)
                Debug.LogWarning("[MmoNetworkManager] No spawn points found; using Vector3.zero.");
            return null;
        }

        Transform NextFromInspectorList()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
                return null;

            // remove any nulls at runtime just in case
            if (spawnPoints.Any(t => t == null))
                spawnPoints = spawnPoints.Where(t => t != null).ToArray();

            if (spawnPoints.Length == 0)
                return null;

            if (!roundRobin)
                return spawnPoints[0];

            // round robin
            if (_nextIndex >= spawnPoints.Length) _nextIndex = 0;
            return spawnPoints[_nextIndex++];
        }

        static bool TryFindByTag(string tag, out GameObject[] tagged)
        {
            tagged = Array.Empty<GameObject>();
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            // Editor: check if tag exists to avoid exceptions
#if UNITY_EDITOR
            try
            {
                var tags = UnityEditorInternal.InternalEditorUtility.tags;
                if (tags == null || Array.IndexOf(tags, tag) < 0)
                    return false;
            }
            catch { /* ignore, fall back to runtime try/catch */ }
#endif
            try
            {
                tagged = GameObject.FindGameObjectsWithTag(tag);
                return tagged != null && tagged.Length > 0;
            }
            catch (UnityException)
            {
                // tag wasn't defined at runtime — safely skip
                return false;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (spawnPoints != null)
                spawnPoints = spawnPoints.Where(t => t != null).ToArray();
        }
#endif
    }
}
