// Assets/MMO/Debugging/JoinLifecycleLogger.cs
using UnityEngine;
using Mirror;
using System;
using System.Reflection;

namespace MMO.Debugging
{
    // Add this to any active GameObject (ideally near your NetworkManager).
    public class JoinLifecycleLogger : MonoBehaviour
    {
        void OnEnable()
        {
            // requireAuthentication=false so we can see messages during connect
            NetworkClient.RegisterHandler<SceneMessage>(OnSceneMessage, false);
        }

        void OnDisable()
        {
            NetworkClient.UnregisterHandler<SceneMessage>();
        }

        void Start()
        {
            Debug.Log("[JoinLog] Active scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }

        void OnSceneMessage(SceneMessage msg)
        {
            // Mirror versions differ: some have 'sceneOperation', some had 'additive/additiveLoad', some 'customHandling'
            string op = GetMemberString(msg, "sceneOperation");   // enum SceneOperation in newer Mirror
            string additive = GetMemberString(msg, "additive");         // old bool (may not exist)
            string additive2 = GetMemberString(msg, "additiveLoad");     // very old bool (may not exist)
            string custom = GetMemberString(msg, "customHandling");   // newer bool (may or may not exist)

            // Prefer op; fall back to additive flags if present
            string additiveFlag = additive != "n/a" ? additive : additive2;

            Debug.Log($"[JoinLog] SceneMessage scene='{msg.sceneName}' op={op} additive={additiveFlag} customHandling={custom}");
        }

        // ---- helpers -------------------------------------------------------

        static string GetMemberString(object obj, string name)
        {
            if (obj == null) return "n/a";
            try
            {
                var t = obj.GetType();

                // Try field first
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var v = f.GetValue(obj);
                    return v != null ? v.ToString() : "null";
                }

                // Then property
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead)
                {
                    var v = p.GetValue(obj, null);
                    return v != null ? v.ToString() : "null";
                }
            }
            catch (Exception e)
            {
                return "err:" + e.GetType().Name;
            }
            return "n/a";
        }

        // If you have a custom NetworkManager, these overrides (in your NM subclass) are great for tracing:
        //
        // public override void OnStartClient()        { Debug.Log("[JoinLog] OnStartClient"); base.OnStartClient(); }
        // public override void OnClientConnect()      { Debug.Log("[JoinLog] OnClientConnect"); base.OnClientConnect(); }
        // public override void OnClientSceneChanged() { Debug.Log("[JoinLog] OnClientSceneChanged"); base.OnClientSceneChanged(); }
        // public override void OnStopClient()         { Debug.Log("[JoinLog] OnStopClient"); base.OnStopClient(); }
        // public override void OnClientDisconnect()   { Debug.Log("[JoinLog] OnClientDisconnect"); base.OnClientDisconnect(); }
        // public override void OnClientError(TransportError error, string reason)
        // {
        //     Debug.LogError($"[JoinLog] Error:{error} {reason}");
        // }
    }
}
