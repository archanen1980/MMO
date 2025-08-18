using System;
using Mirror;
using UnityEngine;

namespace MMO.Chat
{
    public static class ChatClient
    {
        public static event Action<ChatMessage> OnMessage;

        public static void Receive(ChatMessage m)
        {
            OnMessage?.Invoke(m);
        }

        public static void Send(ChatChannel channel, string text, string to = null)
        {
            var lp = NetworkClient.localPlayer;
            if (!lp) { Debug.LogWarning("[ChatClient] No local player to send from."); return; }

            var relay = lp.GetComponent<ChatRelay>();
            if (!relay) { Debug.LogWarning("[ChatClient] ChatRelay missing on local player."); return; }

            // trim and guard
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            relay.CmdSend((uint)channel, to ?? string.Empty, text);
        }
    }
}
