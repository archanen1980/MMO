// Assets/MMO/Bootstrap/NetDebugOverlay.cs
using Mirror;
using UnityEngine;

public class NetDebugOverlay : MonoBehaviour
{
    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 700, 25),
            $"ServerActive={NetworkServer.active}  ClientActive={NetworkClient.active}  Connected={NetworkClient.isConnected}");
    }
}
