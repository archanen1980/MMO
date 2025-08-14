using Mirror;
using UnityEngine;
using MMO.Shared;

namespace MMO.Gameplay
{
    /// <summary>
    /// Local input → MoveIntent. Mouse X rotates the player. Space sends a one-frame jump edge.
    /// </summary>
    [RequireComponent(typeof(MmoPlayer))]
    public class PlayerInputClient : NetworkBehaviour
    {
        public static MoveIntent LastSentIntent;

        [Header("Look")]
        public float mouseXSensitivity = 2.25f;   // Mouse X → yaw (no RMB needed)

        [Header("Cursor")]
        public bool lockCursorOnStart = true;

        float _yaw;
        MmoPlayer _player;

        void Awake() => _player = GetComponent<MmoPlayer>();

        public override void OnStartLocalPlayer()
        {
            _yaw = transform.eulerAngles.y;
            if (lockCursorOnStart) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void Update()
        {
            if (!isLocalPlayer) return;

            // Rotate player with Mouse X
            float mx = Input.GetAxis("Mouse X");
            if (Mathf.Abs(mx) > 0.0001f) _yaw += mx * mouseXSensitivity;

            // WASD + sprint
            float h = (Input.GetKey(KeyCode.A) ? -1f : 0f) + (Input.GetKey(KeyCode.D) ? 1f : 0f);
            float v = (Input.GetKey(KeyCode.S) ? -1f : 0f) + (Input.GetKey(KeyCode.W) ? 1f : 0f);
            bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Jump edge (only when key goes down this frame)
            bool jumpEdge = Input.GetKeyDown(KeyCode.Space);

            var intent = new MoveIntent
            {
                horizontal = h,
                vertical = v,
                yaw = _yaw,
                sprint = sprint,
                jump = jumpEdge
            };

            LastSentIntent = intent;          // for client-side prediction
            _player.CmdSetMoveIntent(intent); // server gets it this frame
        }

        public override void OnStopLocalPlayer()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
