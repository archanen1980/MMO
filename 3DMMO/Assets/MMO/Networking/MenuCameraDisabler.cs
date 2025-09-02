using UnityEngine;
#if MIRROR
using Mirror;
#endif

namespace MMO.Networking
{
    /// <summary>
    /// Disables any stray AudioListener/Camera from menus when the local player spawns.
    /// Helps avoid duplicate listeners and ensures the player camera takes over.
    /// Attach to your Player prefab.
    /// </summary>
    public class MenuCameraDisabler : MonoBehaviour
    {
#if MIRROR
        void Start()
        {
            // If this is not the local player, bail (works whether NetworkIdentity or NetworkBehaviour is used)
            var ni = GetComponent<NetworkIdentity>();
            if (ni && !ni.isLocalPlayer) return;

            // Disable extra listeners on startup
            var listeners = FindObjectsOfType<AudioListener>(true);
            foreach (var al in listeners)
            {
                if (al.gameObject == gameObject) continue; // keep on the player
                al.enabled = false;
            }

            var cams = FindObjectsOfType<Camera>(true);
            foreach (var cam in cams)
            {
                if (cam.gameObject == gameObject) continue;
                // If it's tagged as Menu/MainMenu, or lives under a Canvas called Login, disable
                if (cam.GetComponentInParent<Canvas>() || cam.tag == "MainCamera")
                {
                    cam.enabled = false;
                }
            }
        }
#endif
    }
}
