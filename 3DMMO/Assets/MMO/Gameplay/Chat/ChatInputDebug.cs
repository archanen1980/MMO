using UnityEngine;
using Mirror;

namespace MMO.Gameplay
{
    public class ChatInputDebug : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                var lp = NetworkClient.localPlayer;
                if (lp && lp.TryGetComponent(out ChatBehaviour chat))
                    chat.CmdSendChat("Hello world!");
            }
        }
    }
}
