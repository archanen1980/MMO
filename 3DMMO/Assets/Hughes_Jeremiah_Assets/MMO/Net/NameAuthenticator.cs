using Mirror;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Minimal username authenticator using Mirror's Authenticator API.
    /// Client: sends username in OnClientAuthenticate().
    /// Server: validates, then ServerAccept/ServerReject.
    /// </summary>
    public class NameAuthenticator : NetworkAuthenticator
    {
        public struct AuthRequest  : NetworkMessage { public string username; }
        public struct AuthResponse : NetworkMessage { public bool ok; public string msg; }

        [Tooltip("If true, empty or invalid usernames are rejected.")]
        public bool requireNonEmptyName = true;

        [Header("Client Username (set before StartClient)")]
        public string pendingUsername = string.Empty;

        // ---- Server lifecycle ----
        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<AuthRequest>(OnAuthRequestServer, false);
            Debug.Log("[AUTH][SERVER] Registered AuthRequest handler");
        }
        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<AuthRequest>();
        }

        // ---- Client lifecycle ----
        public override void OnStartClient()
        {
            // Register response handler (ok to re-register in OnClientAuthenticate as well)
            NetworkClient.RegisterHandler<AuthResponse>(OnAuthResponseClient, false);
            Debug.Log("[AUTH][CLIENT] Registered AuthResponse handler");
        }
        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AuthResponse>();
        }

        // Mirror calls this on the server when a client connects.
        public override void OnServerAuthenticate(NetworkConnectionToClient conn) { /* wait for AuthRequest */ }

        // Mirror calls this on the client after transport connects.
        public override void OnClientAuthenticate()
        {
            // Ensure handler exists
            NetworkClient.RegisterHandler<AuthResponse>(OnAuthResponseClient, false);

            string uname = pendingUsername;
            if (string.IsNullOrWhiteSpace(uname))
                uname = "Player" + Random.Range(1000, 9999);
            uname = uname.Trim();

            Debug.Log($"[AUTH][CLIENT] Sending username '{uname}'");
            NetworkClient.Send(new AuthRequest { username = uname });
        }

        // ---- Client side: server replied ----
        void OnAuthResponseClient(AuthResponse msg)
        {
            Debug.Log($"[AUTH][CLIENT] Response ok={msg.ok} msg='{msg.msg}'");
            if (msg.ok) ClientAccept();
            else        ClientReject();
        }

        // ---- Server side: handle client request ----
        void OnAuthRequestServer(NetworkConnectionToClient conn, AuthRequest msg)
        {
            string name = (msg.username ?? "").Trim();
            Debug.Log($"[AUTH][SERVER] AuthRequest from connId={conn.connectionId} name='{name}'");

            if (requireNonEmptyName && string.IsNullOrWhiteSpace(name))
            {
                conn.Send(new AuthResponse { ok = false, msg = "Invalid username" });
                ServerReject(conn);
                return;
            }

            conn.authenticationData = name; // stash for spawn
            conn.Send(new AuthResponse { ok = true, msg = "Welcome" });
            ServerAccept(conn);
        }
    }
}
