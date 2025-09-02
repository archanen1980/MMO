using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if MIRROR
using Mirror;
#endif

namespace MMO.Networking
{
    /// <summary>
    /// Drop-in diagnostics for host crashes around StartHost().
    /// - Adds verbose logging around host/client/server lifecycle
    /// - Performs sanity checks (scenes, player prefab, transport, port availability)
    /// - Provides a safe wrapper to StartHost() you can wire to your Host button
    /// </summary>
    public class MMONetworkDiagnostics : MonoBehaviour
    {
#if MIRROR
        [Header("References")]
        [Tooltip("Explicit NetworkManager reference. If null, will use NetworkManager.singleton at runtime.")]
        public NetworkManager networkManager;

        [Header("Options")] public int expectedPort = 7777; // Telepathy default
        public bool writeFileLog = true;

        string _logFile;

        void Awake()
        {
            if (!networkManager) networkManager = NetworkManager.singleton;
            _logFile = Path.Combine(Application.persistentDataPath, "mmo_host_diag.log");
            Application.logMessageReceived += OnLog;
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);

            Log("[MMO-DIAG] Awake. persistentDataPath=" + Application.persistentDataPath);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        public void StartHostSafe()
        {
            if (!networkManager)
            {
                Error("No NetworkManager found.");
                return;
            }

            // 1) Scenes configured?
#if UNITY_EDITOR
            if (!SceneIsInBuild(networkManager.offlineScene) || !SceneIsInBuild(networkManager.onlineScene))
            {
                Warning($"Scene(s) not in Build Settings. offline='{networkManager.offlineScene}' online='{networkManager.onlineScene}'.");
            }
#endif
            // 2) Player prefab sanity
            if (!networkManager.playerPrefab)
            {
                Error("NetworkManager.playerPrefab is not assigned.");
                return;
            }

            // 3) Transport present on same GameObject
            var transport = GetComponent<UnityEngine.Component>();
            // We avoid referencing Mirror.Transport API directly to keep compatibility.
            transport = GetComponent("TelepathyTransport") as Component ?? transport;
            if (!transport)
                Warning("No transport component found on NetworkManager object (expected TelepathyTransport). Check your setup.");

            // 4) Port availability quick probe (best-effort)
            if (!IsPortFree(expectedPort))
                Warning($"Port {expectedPort} appears in use. If hosting fails, try another port or stop the other process.");

            try
            {
                Log("[MMO-DIAG] Calling NetworkManager.StartHost() …");
                networkManager.StartHost();
                Log("[MMO-DIAG] StartHost() returned.");
            }
            catch (Exception ex)
            {
                Error("StartHost() threw: " + ex);
                throw; // rethrow so you still see the exception in Console
            }
        }

        // Optional helpers to wire from inspector without code changes
        public void StartServerSafe()
        {
            if (!networkManager) networkManager = NetworkManager.singleton;
            try
            {
                Log("[MMO-DIAG] StartServer()");
                networkManager.StartServer();
            }
            catch (Exception ex)
            {
                Error("StartServer() threw: " + ex);
            }
        }

        public void StartClientLoopback()
        {
            if (!networkManager) networkManager = NetworkManager.singleton;
            try
            {
                Log("[MMO-DIAG] StartClient(127.0.0.1)");
                networkManager.StartClient();
            }
            catch (Exception ex)
            {
                Error("StartClient() threw: " + ex);
            }
        }

        static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }

#if UNITY_EDITOR
        static bool SceneIsInBuild(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;
            for (int i = 0; i < UnityEditor.EditorBuildSettings.scenes.Length; i++)
            {
                var s = UnityEditor.EditorBuildSettings.scenes[i];
                if (s.path == scenePath && s.enabled) return true;
            }
            return false;
        }
#endif

        void OnLog(string condition, string stack, LogType type)
        {
            if (!writeFileLog) return;
            try
            {
                File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {type}: {condition}\n{stack}\n");
            }
            catch { /* ignore */ }
        }

        void Log(string msg) { Debug.Log(msg); }
        void Warning(string msg) { Debug.LogWarning(msg); }
        void Error(string msg) { Debug.LogError(msg); }
#endif // MIRROR
    }
}
