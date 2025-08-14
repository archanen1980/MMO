using Mirror;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Replicated character display name. Server sets it; clients read it.
    /// </summary>
    public class PlayerName : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnNameChanged))]
        public string displayName = "";

        /// <summary>Server-only setter so we keep authority.</summary>
        [Server]
        public void ServerSetDisplayName(string name)
        {
            displayName = name ?? "";
        }

        void OnNameChanged(string _, string newName)
        {
            // Nice for hierarchy debugging
            gameObject.name = $"Player[{newName}]";
        }
    }
}
