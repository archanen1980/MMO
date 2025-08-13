// Assets/MMO/Gameplay/Player/PlayerInputClient.cs
using Mirror;
using UnityEngine;
using MMO.Shared;

namespace MMO.Gameplay
{
    /// <summary>
    /// Reads local input and sends MoveIntent to the server.
    /// - Mouse X rotates the player (updates yaw).
    /// - Mouse Y is NOT used here (camera handles pitch).
    /// - WASD moves relative to the facing (strafe on A/D).
    /// </summary>
    [RequireComponent(typeof(MmoPlayer))]
    public class PlayerInputClient : NetworkBehaviour
    {
        public static MoveIntent LastSentIntent; // used by prediction

        [Header("Look")]
        [Tooltip("Degrees per mouse unit for horizontal look (Mouse X).")]
        public float mouseXSensitivity = 2.25f;

        [Tooltip("If true, only rotate when the mouse button is held.")]
        public bool requireMouseButtonForRotate = false;

        [Tooltip("0=Left, 1=Right, 2=Middle")]
        public int rotateMouseButton = 1; // Right Mouse Button by default

        [Header("Cursor")]
        public bool lockCursorOnStart = true;

        private float _yaw;
        private MmoPlayer _player;

        void Awake()
        {
            _player = GetComponent<MmoPlayer>();
        }

        public override void OnStartLocalPlayer()
        {
            // Initialize yaw from current facing
            _yaw = transform.eulerAngles.y;

            if (lockCursorOnStart)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            if (!isLocalPlayer) return;

            // --- Mouse X → rotate player (adjust yaw) ---
            bool rotating = !requireMouseButtonForRotate || Input.GetMouseButton(rotateMouseButton);
            if (rotating)
            {
                float mx = Input.GetAxis("Mouse X");
                if (Mathf.Abs(mx) > 0.0001f)
                    _yaw += mx * mouseXSensitivity;
            }

            // --- WASD movement (strafe on A/D, forward/back on W/S) ---
            float h = (Input.GetKey(KeyCode.A) ? -1f : 0f) + (Input.GetKey(KeyCode.D) ? 1f : 0f);
            float v = (Input.GetKey(KeyCode.S) ? -1f : 0f) + (Input.GetKey(KeyCode.W) ? 1f : 0f);
            bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            var intent = new MoveIntent
            {
                horizontal = h,
                vertical = v,
                yaw = _yaw,   // absolute facing in degrees
                sprint = sprint
            };

            LastSentIntent = intent;
            _player.CmdSetMoveIntent(intent);
        }

        public override void OnStopLocalPlayer()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
