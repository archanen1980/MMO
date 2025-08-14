// Assets/MMO/Bootstrap/MmoBootstrap.cs
using System.Linq;
using Mirror;
using UnityEngine;
using MMO.Net;

namespace MMO.Bootstrap
{
    /// <summary>
    /// Boots into Host (Editor), Dedicated Server (-server/batchmode), or Client.
    /// - If a LoginCanvasRuntimeUI is in the scene (and not headless), do NOT auto-connect.
    /// - CLI: -connect <addr>   -port <number>   -user <name>   -server
    /// - Ensures TelepathyTransport exists and is bound to the NetworkManager.
    /// </summary>
    public class MmoBootstrap : MonoBehaviour
    {
        [Tooltip("Auto-run as Host when playing in the Editor for faster iteration")]
        public bool runHostInEditor = true;

        [Tooltip("Address to connect to when running as a client (fallback when no UI)")]
        public string clientAddress = "localhost";

        [Tooltip("Default Telepathy port if not provided via -port")]
        public ushort defaultPort = 7777;

        void Awake()
        {
            Application.targetFrameRate = 60;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            var nm = FindObjectOfType<MMO.Net.MmoNetworkManager>();
            if (nm == null)
            {
                Debug.LogError("MmoBootstrap: No MmoNetworkManager found in scene.");
                return;
            }

            // Ensure a transport and bind it
            var transport =
                nm.GetComponent<TelepathyTransport>() ??
                FindObjectOfType<TelepathyTransport>() ??
                nm.gameObject.AddComponent<TelepathyTransport>();

            nm.transport = transport;
            if (NetworkManager.singleton != null)
                NetworkManager.singleton.transport = transport;

            // Parse command-line args
            string[] args = System.Environment.GetCommandLineArgs();
            bool serverFlag = args.Any(a => a == "-server");
            string cliUser = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-connect") clientAddress = args[i + 1];
                if (args[i] == "-user") cliUser = args[i + 1];
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out var p))
                    transport.port = p;
            }

            if (transport.port == 0)
                transport.port = defaultPort;

            // If a uGUI login exists, let the UI control connection (unless headless/server)
            bool hasLoginUI = FindObjectOfType<MMO.UI.LoginCanvasRuntimeUI>() != null;

#if UNITY_EDITOR
            if (!serverFlag && hasLoginUI)
            {
                Debug.Log("[BOOT] Login UI present — not auto-connecting in Editor. Use the UI.");
                return;
            }

            if (runHostInEditor && !Application.isBatchMode && !serverFlag)
            {
                Debug.Log($"[BOOT] Host (Editor). Port={transport.port}");
                nm.StartHost();
                return;
            }
#endif

            if (Application.isBatchMode || serverFlag)
            {
                Debug.Log($"[BOOT] Dedicated Server. Port={transport.port}");
                nm.StartServer();
                return;
            }

            if (hasLoginUI)
            {
                Debug.Log("[BOOT] Login UI present — not auto-connecting. Use the UI.");
                return;
            }

            // Fallback auto-client (no UI present)
            var auth = nm.GetComponent<NameAuthenticator>() ?? nm.gameObject.AddComponent<NameAuthenticator>();
            if (!string.IsNullOrWhiteSpace(cliUser))
                auth.pendingUsername = cliUser;
            else if (string.IsNullOrWhiteSpace(auth.pendingUsername))
                auth.pendingUsername = $"Arch_{Random.Range(100, 999)}";

            NetworkManager.singleton.networkAddress = clientAddress;
            Debug.Log($"[BOOT] Client → {clientAddress}:{transport.port}, User='{auth.pendingUsername}'");
            nm.StartClient();
        }
    }
}
