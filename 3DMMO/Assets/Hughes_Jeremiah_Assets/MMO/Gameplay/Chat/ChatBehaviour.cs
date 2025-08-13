using Mirror;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using MMO.Shared;

namespace MMO.Gameplay
{
    /// <summary>
    /// Attach to the Player prefab. The local player calls CmdSendChat(text).
    /// Server sanitizes, rate-limits, and broadcasts ChatMessage to all clients.
    /// Adds a simple slash command: /name NewName  (server-authoritative).
    /// </summary>
    public class ChatBehaviour : NetworkBehaviour
    {
        const int MAX_LEN = 200;
        const float WINDOW = 10f; // seconds
        const int MAX_MSGS = 8;   // per window per player

        readonly Queue<float> _timestamps = new(); // server-only
        float _lastNameChange = -999f;

        [Command]
        public void CmdSendChat(string message)
        {
            if (!isServer) return;
            if (string.IsNullOrWhiteSpace(message)) return;

            message = message.Trim();
            if (message.Length > MAX_LEN) message = message.Substring(0, MAX_LEN);

            // Slash commands
            if (message.StartsWith("/"))
            {
                HandleSlashCommand(message);
                return;
            }

            // Rate limit normal chat
            float now = Time.time;
            while (_timestamps.Count > 0 && now - _timestamps.Peek() > WINDOW)
                _timestamps.Dequeue();
            if (_timestamps.Count >= MAX_MSGS) return;
            _timestamps.Enqueue(now);

            // Resolve display name: PlayerName → authenticator username → fallback
            string from = $"Player{netId}";

            if (TryGetComponent<PlayerName>(out var pn) && !string.IsNullOrWhiteSpace(pn.displayName))
                from = pn.displayName;
            else if (connectionToClient?.authenticationData is string uname && !string.IsNullOrWhiteSpace(uname))
                from = uname.Trim();

            NetworkServer.SendToAll(new ChatMessage
            {
                from = from,
                text = message,
                time = NetworkTime.time
            });
        }

        // ---- Commands ----
        [Server]
        void HandleSlashCommand(string raw)
        {
            string cmd, arg;
            int space = raw.IndexOf(' ');
            if (space < 0) { cmd = raw.ToLowerInvariant(); arg = ""; }
            else { cmd = raw.Substring(0, space).ToLowerInvariant(); arg = raw.Substring(space + 1).Trim(); }

            switch (cmd)
            {
                case "/name":
                    TrySetDisplayName(arg);
                    break;
                default:
                    SendSystemToSelf("Unknown command. Try /name YourCharacter");
                    break;
            }
        }

        [Server]
        void TrySetDisplayName(string desired)
        {
            if (string.IsNullOrWhiteSpace(desired))
            {
                SendSystemToSelf("Usage: /name YourCharacter");
                return;
            }

            // Minimum cooldown to avoid spam
            if (Time.time - _lastNameChange < 3f)
            {
                SendSystemToSelf("Please wait a few seconds before changing name again.");
                return;
            }

            string clean = SanitizeName(desired);
            if (clean.Length < 3 || clean.Length > 20)
            {
                SendSystemToSelf("Name must be 3–20 characters (letters, numbers, space, _ or -).");
                return;
            }

            if (!TryGetComponent<PlayerName>(out var pn))
            {
                SendSystemToSelf("Name system not available.");
                return;
            }

            pn.ServerSetDisplayName(clean);
            _lastNameChange = Time.time;

            SendSystemToSelf($"Display name set to <b>{clean}</b>.");
            // (Optional) tell everyone:
            // NetworkServer.SendToAll(new ChatMessage { from = "System", text = $"{clean} joined the realm!", time = NetworkTime.time });
        }

        // Allow letters/numbers/space/_/-
        static string SanitizeName(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-')
                    sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        [Server]
        void SendSystemToSelf(string text)
        {
            connectionToClient?.Send(new ChatMessage
            {
                from = "System",
                text = text,
                time = NetworkTime.time
            });
        }
    }
}
