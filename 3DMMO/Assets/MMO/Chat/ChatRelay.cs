using System;
using Mirror;
using UnityEngine;

namespace MMO.Chat
{
    /// Attach to the *root* player object that has the NetworkIdentity.
    public class ChatRelay : NetworkBehaviour
    {
        [Header("Limits")]
        [SerializeField] int maxLength = 240;
        [SerializeField] float minSendInterval = 0.2f;

        double _lastSendTime;

        // Client → Server
        [Command(requiresAuthority = false)]
        public void CmdSend(uint channelMask, string to, string text)
        {
            if (!isServer) return;

            // Only allow the *owner* of this NetworkIdentity to send through this component
            if (connectionToClient == null || connectionToClient.identity != netIdentity)
                return;

            var now = NetworkTime.time;
            if (now - _lastSendTime < minSendInterval) return;
            _lastSendTime = now;

            if (string.IsNullOrWhiteSpace(text)) return;

            text = text.Trim();
            if (text.Length > maxLength) text = text.Substring(0, maxLength);

            var msg = new ChatMessage
            {
                channel = (ChatChannel)channelMask,
                from = gameObject.name, // replace with your player display name
                to = string.IsNullOrWhiteSpace(to) ? null : to,
                text = text,
                unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

#if UNITY_EDITOR
            Debug.Log($"[ChatRelay][SVR] {msg.channel} {msg.from}: {msg.text}");
#endif

            // MVP: broadcast to all (you’ll scope this later for Party/Guild/Whisper)
            foreach (var kv in NetworkServer.connections)
            {
                var conn = kv.Value;
                var id = conn?.identity;
                if (!id) continue;

                var relay = id.GetComponent<ChatRelay>();
                if (!relay) continue;

                relay.TargetReceive(conn, msg);
            }
        }

        // Server → Client
        [TargetRpc]
        void TargetReceive(NetworkConnection conn, ChatMessage msg)
        {
#if UNITY_EDITOR
            Debug.Log($"[ChatRelay][CLI] recv {msg.channel} {msg.from}: {msg.text}");
#endif
            ChatClient.Receive(msg);
        }

        // Convenience for system lines from server
        [Server]
        public static void ServerBroadcastSystem(string text, ChatChannel ch = ChatChannel.System)
        {
            var msg = new ChatMessage
            {
                channel = ch,
                from = "System",
                to = null,
                text = text,
                unixTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            foreach (var kv in NetworkServer.connections)
            {
                var conn = kv.Value;
                var id = conn?.identity;
                if (!id) continue;
                var r = id.GetComponent<ChatRelay>();
                if (!r) continue;
                r.TargetReceive(conn, msg);
            }
        }
    }
}
