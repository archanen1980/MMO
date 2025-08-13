using Mirror;
using UnityEngine;

namespace MMO.Gameplay
{
    public class PlayerName : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnNameChanged))] public string displayName = "";
        [SerializeField] TextMesh _worldLabel;
        public void SetDisplayName(string name){ if (!isServer) return; displayName = name; }
        void OnNameChanged(string _, string newName){ if (_worldLabel) _worldLabel.text = newName; gameObject.name = $"Player[{newName}]"; }
    }
}
