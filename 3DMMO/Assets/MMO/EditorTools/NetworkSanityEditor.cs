#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
#if MIRROR
using Mirror;
#endif

namespace MMO.EditorTools
{
    public static class NetworkSanityEditor
    {
        [MenuItem("Tools/MMO Starter/Diagnostics/Run Network Sanity Check")]
        public static void Run()
        {
#if MIRROR
            var nm = Object.FindObjectOfType<NetworkManager>();
            if (!nm)
            {
                EditorUtility.DisplayDialog("Network Sanity", "No NetworkManager found in the scene.", "OK");
                return;
            }

            string msg = "";
            bool ok = true;

            // Scenes
            if (string.IsNullOrEmpty(nm.offlineScene)) { msg += "• offlineScene is not set\n"; ok = false; }
            if (string.IsNullOrEmpty(nm.onlineScene)) { msg += "• onlineScene is not set\n"; ok = false; }

            // Build settings
            if (!IsInBuild(nm.offlineScene)) { msg += $"• offlineScene not in Build Settings: {nm.offlineScene}\n"; ok = false; }
            if (!IsInBuild(nm.onlineScene)) { msg += $"• onlineScene not in Build Settings: {nm.onlineScene}\n"; ok = false; }

            // Player prefab
            if (!nm.playerPrefab) { msg += "• playerPrefab is not assigned\n"; ok = false; }
            else if (!nm.playerPrefab.GetComponent<NetworkIdentity>()) { msg += "• playerPrefab is missing NetworkIdentity\n"; ok = false; }

            // Transport
            var transport = nm.GetComponent("TelepathyTransport") as Component;
            if (!transport) { msg += "• TelepathyTransport component not found on the same GameObject as NetworkManager\n"; ok = false; }

            if (ok) msg = "All core checks passed. If you still crash on Host, enable MMONetworkDiagnostics and use StartHostSafe().";
            EditorUtility.DisplayDialog("Network Sanity", msg, "OK");
#else
            EditorUtility.DisplayDialog("Network Sanity", "MIRROR scripting define not set. Add 'MIRROR' to Project Settings ▸ Player ▸ Scripting Define Symbols, or remove the #if MIRROR guards.", "OK");
#endif
        }

#if UNITY_EDITOR
        static bool IsInBuild(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;
            return EditorBuildSettings.scenes.Any(s => s.enabled && s.path == scenePath);
        }
#endif
    }
}
#endif
