using Mirror;
using UnityEngine;
using System.Collections.Generic;

namespace MMO.Gameplay
{
    public class ChatBehaviour : NetworkBehaviour
    {
        const int MAX_LEN = 200; const float WINDOW = 10f; const int MAX_MSGS = 10;
        readonly Queue<float> _timestamps = new();
        [Command] public void CmdSendChat(string message){ if(!isServer) return; if(string.IsNullOrWhiteSpace(message)) return; message=message.Trim(); if(message.Length>MAX_LEN) message=message[..MAX_LEN];
            float now=Time.time; while(_timestamps.Count>0 && now-_timestamps.Peek()>WINDOW)_timestamps.Dequeue(); if(_timestamps.Count>=MAX_MSGS) return; _timestamps.Enqueue(now);
            string from=GetComponent<PlayerName>()?.displayName ?? $"Player{netId}"; RpcReceiveChat(from,message); }
        [ClientRpc] void RpcReceiveChat(string from,string message){ Debug.Log($"[Chat] {from}: {message}"); }
    }
}
