using Mirror;
using UnityEngine;
using MMO.Shared;
using MMO.Server.Persistence;
using MMO.Gameplay;

namespace MMO.Net
{
    public class MmoNetworkManager : NetworkManager
    {
        public int maxPlayers = 100;
        public Transform[] spawnPoints;
        IPersistence _persistence;

        public override void Awake()
        {
            base.Awake();
            if (authenticator == null)
                authenticator = gameObject.AddComponent<NameAuthenticator>();
            _persistence = new JsonPersistence();
        }

        public override void OnStartServer(){ base.OnStartServer(); Debug.Log("[Server] Started"); }
        public override void OnStopServer(){ base.OnStopServer(); Debug.Log("[Server] Stopped"); }
        public override void OnStartClient(){ base.OnStartClient(); Debug.Log("[Client] Started"); }
        public override void OnClientConnect(){ base.OnClientConnect(); Debug.Log("[Client] Connected to server"); }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (NetworkServer.connections.Count > maxPlayers) { conn.Disconnect(); return; }
            string accountId = conn.authenticationData as string ?? $"Guest{conn.connectionId}";
            var data = _persistence.LoadCharacterAsync(accountId).ConfigureAwait(false).GetAwaiter().GetResult();

            Vector3 pos = data.position; Quaternion rot = Quaternion.Euler(0, data.yaw, 0);
            if (spawnPoints != null && spawnPoints.Length > 0 && pos == Vector3.zero)
            { int i = NetworkServer.connections.Count % spawnPoints.Length; pos = spawnPoints[i].position; rot = spawnPoints[i].rotation; }

            GameObject player = Instantiate(playerPrefab, pos, rot);
            var nameComp = player.GetComponent<PlayerName>(); if (nameComp != null) nameComp.SetDisplayName(accountId);
            NetworkServer.AddPlayerForConnection(conn, player);
            Debug.Log($"[Server] Player joined → connId={conn.connectionId}");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            string accountId = conn.authenticationData as string;
            if (!string.IsNullOrEmpty(accountId) && conn.identity != null)
            {
                var go = conn.identity.gameObject;
                var saved = new CharacterData { characterName = accountId, position = go.transform.position, yaw = go.transform.eulerAngles.y };
                _ = _persistence.SaveCharacterAsync(accountId, saved);
            }
            base.OnServerDisconnect(conn);
        }
    }
}
