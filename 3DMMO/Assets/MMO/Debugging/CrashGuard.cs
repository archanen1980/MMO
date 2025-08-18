using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace MMO.Debugging
{
    // Drop once in your bootstrap scene (or mark DontDestroyOnLoad).
    public class CrashGuard : MonoBehaviour
    {
        static CrashGuard _instance;
        readonly Queue<string> ring = new Queue<string>();
        const int Max = 200;

        void Awake()
        {
            if (_instance) { Destroy(gameObject); return; }
            _instance = this; DontDestroyOnLoad(gameObject);
            Application.logMessageReceivedThreaded += OnLog;
            Debug.Log("[CrashGuard] installed");
        }

        void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            var line = $"[{type}] {condition}\n{stackTrace}";
            lock (ring)
            {
                ring.Enqueue(line);
                while (ring.Count > Max) ring.Dequeue();
            }
        }

        void OnApplicationQuit()
        {
            try
            {
                if (NetworkServer.active && NetworkClient.isConnected) NetworkManager.singleton.StopHost();
                else if (NetworkServer.active) NetworkManager.singleton.StopServer();
                else if (NetworkClient.isConnected) NetworkManager.singleton.StopClient();
                NetworkManager.singleton?.transport?.Shutdown(); // closes Telepathy sockets
            }
            catch { }
        }

        void Dump(string reason)
        {
            lock (ring)
            {
                var path = System.IO.Path.Combine(Application.persistentDataPath, "last_run_log.txt");
                System.IO.File.WriteAllText(path,
                    $"==== CrashGuard dump ({reason}) {DateTime.Now} ====\n" +
                    string.Join("\n----\n", ring.ToArray()));
                Debug.Log($"[CrashGuard] wrote {ring.Count} lines to {path}");
            }
        }
    }
}
