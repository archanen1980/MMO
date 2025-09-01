// Assets/Hughes_Jeremiah_Assets/MMO/Net/MmoNetworkManager.cs
using Mirror;
using UnityEngine;
using MMO.Shared;
using MMO.Gameplay;                 // PlayerName
using MMO.Server.Persistence;       // IPersistence / JsonPersistence

namespace MMO.Net
{
    /// <summary>
    /// Thin wrapper over Mirror's NetworkManager so we can:
    /// - Log lifecycle events
    /// - Handle auth + spawn with saved data
    /// - Set replicated display names on spawn
    /// - Save character state on disconnect
    /// - Provide SafeStartHost() & robust shutdown
    /// </summary>
    public class MmoNetworkManager : NetworkManager
    {
        [Header("MMO Settings")]
        [Tooltip("Max simultaneous players (soft cap)")]
        public int maxPlayers = 100;

        [Tooltip("Optional spawn points; will cycle through if set")]
        public Transform[] spawnPoints;

        bool serverSystemsInitialized;
        IPersistence _persistence;

        public override void Awake()
        {
            base.Awake();

            // Prevent duplicates (common with DDOL & additive scenes)
            if (singleton != null && singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);

            // Ensure a Transport is assigned on the NetworkManager
            if (!transport)
            {
                // Try to pick one from the same GameObject
                var t = GetComponent<Transport>();
                if (t != null) transport = t;
            }

            // Ensure an authenticator exists & is set
            if (!authenticator)
            {
                var na = GetComponent<NameAuthenticator>() ?? gameObject.AddComponent<NameAuthenticator>();
                authenticator = na;
            }

            // Simple JSON persistence for dev/learning
            _persistence = new JsonPersistence();
        }

        /// <summary>
        /// Call this from your bootstrap/UI instead of StartHost().
        /// </summary>
        public bool SafeStartHost()
        {
            if (!PreflightCheck())
                return false;

            if (NetworkServer.active || NetworkClient.isConnected || NetworkClient.active)
            {
                Debug.Log("[Net] Already running/connected, ignoring SafeStartHost.");
                return true;
            }

            Debug.Log("[Net] SafeStartHost()");
            StartHost(); // will trigger server/client callbacks
            return true;
        }

        bool PreflightCheck()
        {
            if (!transport)
            {
                Debug.LogError("[Net] No Transport assigned on NetworkManager. Add a Transport (e.g., TelepathyTransport) to the same GameObject and assign it.");
                return false;
            }

            if (!playerPrefab)
            {
                Debug.LogError("[Net] Player Prefab is not assigned on NetworkManager.");
                return false;
            }

            if (!playerPrefab.GetComponent<NetworkIdentity>())
            {
                Debug.LogError("[Net] Player Prefab must have a NetworkIdentity component.");
                return false;
            }

            // Optional: authenticator
            if (!authenticator)
                Debug.LogWarning("[Net] No Authenticator set. Guests will be used.");

            return true;
        }

        // ---------------- Mirror lifecycle ----------------

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (serverSystemsInitialized) return;
            serverSystemsInitialized = true;
            Debug.Log("[Server] Started (init once)");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            serverSystemsInitialized = false;
            Debug.Log("[Server] Stopped");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log("[Client] Started");
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            Debug.Log("[Client] Connected to server");
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            if (numPlayers >= maxPlayers)
            {
                Debug.LogWarning("[Server] Max players reached, rejecting connection.");
                conn.Disconnect();
                return;
            }
            base.OnServerConnect(conn);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Account/username from authenticator (set during auth handshake)
            string accountId = conn.authenticationData as string;
            if (string.IsNullOrWhiteSpace(accountId))
                accountId = $"Guest{conn.connectionId}";

            // Load saved character data synchronously (simple dev approach)
            CharacterData data = null;
            try
            {
                data = _persistence.LoadCharacterAsync(accountId)
                                   .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Server] LoadCharacter failed for '{accountId}': {ex.Message}");
            }

            // Choose spawn pos/rot
            Vector3 spawnPos = data != null ? data.position : Vector3.zero;
            Quaternion spawnRot = Quaternion.Euler(0f, data != null ? data.yaw : 0f, 0f);

            // If no saved position and we have spawn points, pick one
            if ((spawnPoints != null && spawnPoints.Length > 0) &&
                (data == null || spawnPos == Vector3.zero))
            {
                int i = NetworkServer.connections.Count % spawnPoints.Length;
                spawnPos = spawnPoints[i].position;
                spawnRot = spawnPoints[i].rotation;
            }

            // Instantiate player
            GameObject player = Instantiate(playerPrefab, spawnPos, spawnRot);

            // Set replicated display name
            if (player.TryGetComponent(out PlayerName nameComp))
            {
                string display = (data != null && !string.IsNullOrWhiteSpace(data.characterName))
                    ? data.characterName
                    : accountId;
                nameComp.ServerSetDisplayName(display);
            }

            // Finalize spawn
            NetworkServer.AddPlayerForConnection(conn, player);
            Debug.Log($"[Server] Player joined → connId={conn.connectionId}, account='{accountId}'");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            // Save on disconnect
            try
            {
                string accountId = conn.authenticationData as string;
                if (!string.IsNullOrWhiteSpace(accountId) && conn.identity != null)
                {
                    var player = conn.identity.gameObject;
                    string displayName = null;

                    if (player.TryGetComponent(out PlayerName pn))
                        displayName = pn.displayName;

                    var saved = new CharacterData
                    {
                        characterName = string.IsNullOrWhiteSpace(displayName) ? accountId : displayName,
                        position = player.transform.position,
                        yaw = player.transform.eulerAngles.y
                    };

                    _ = _persistence.SaveCharacterAsync(accountId, saved);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Server] SaveCharacter failed: {ex.Message}");
            }

            Debug.Log($"[Server] Player left → connId={conn.connectionId}");
            base.OnServerDisconnect(conn);
        }

        void OnApplicationQuit()
        {
            // Robust shutdown for editor & player (no Transport.activeTransport usage)
            try
            {
                if (NetworkServer.active && NetworkClient.isConnected) StopHost();
                else if (NetworkServer.active) StopServer();
                else if (NetworkClient.isConnected) StopClient();
            }
            catch { /* ignore */ }
        }
    }
}
