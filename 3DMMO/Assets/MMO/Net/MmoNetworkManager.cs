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
    /// - Handle authentication + spawn with saved data
    /// - Set replicated display names on spawn
    /// - Save character state on disconnect
    /// </summary>
    public class MmoNetworkManager : NetworkManager
    {
        [Header("MMO Settings")]
        [Tooltip("Max simultaneous players (soft cap)")]
        public int maxPlayers = 100;

        [Tooltip("Optional spawn points; will cycle through if set")]
        public Transform[] spawnPoints;

        // NOTE: Do NOT redeclare "authenticator" here — NetworkManager already has it.

        IPersistence _persistence;

        public override void Awake()
        {
            base.Awake();

            // Ensure a NameAuthenticator component exists and assign it
            var na = gameObject.GetComponent<NameAuthenticator>();
            if (na == null)
                na = gameObject.AddComponent<NameAuthenticator>();

            // Use the base field from NetworkManager
            this.authenticator = na;

            // Simple JSON persistence for dev/learning
            _persistence = new JsonPersistence(); // persistentDataPath/MMO_Save
        }

        // --- Logging (optional) ---
        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("[Server] Started");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
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

        // --- Player Spawn Path ---
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Soft cap
            if (NetworkServer.connections.Count > maxPlayers)
            {
                Debug.LogWarning("[Server] Max players reached, rejecting connection.");
                conn.Disconnect();
                return;
            }

            // Account/username from authenticator (set during auth handshake)
            string accountId = conn.authenticationData as string;
            if (string.IsNullOrWhiteSpace(accountId))
                accountId = $"Guest{conn.connectionId}";

            // Load saved character data (sync-over-async for simplicity here)
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

            // Instantiate the player prefab
            GameObject player = Instantiate(playerPrefab, spawnPos, spawnRot);

            // Set replicated display name on spawn
            if (player.TryGetComponent(out PlayerName nameComp))
            {
                // Prefer saved character name; otherwise use accountId
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
            // Save character on disconnect if we have enough info
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
    }
}
