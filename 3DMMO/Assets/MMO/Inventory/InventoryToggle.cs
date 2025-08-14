using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MMO.Inventory
{
    /// <summary>
    /// Toggles a target Inventory panel (GameObject) on key/gamepad.
    /// Works even if the panel starts inactive because THIS component lives on an active object.
    /// Default bindings:
    ///   - Toggle: I key, Gamepad Start/Y
    ///   - Close:  Esc, Gamepad B
    /// Optional: require a local player connection before opening, cursor lock/unlock, notify events.
    /// </summary>
    public class InventoryToggle : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The panel GameObject to show/hide (your InventoryPanel root).")]
        [SerializeField] GameObject panel;

        [Header("Behavior")]
        [Tooltip("Block opening unless a local player is spawned (Mirror).")]
        [SerializeField] bool requireLocalPlayer = true;

        [Tooltip("Unlock and show the cursor when panel is open; restore when closed.")]
        [SerializeField] bool manageCursor = true;

        [Tooltip("Close with Escape / Gamepad B.")]
        [SerializeField] bool closeOnEscape = true;

        [Tooltip("Prevent toggling while typing in any TMP_InputField / InputField.")]
        [SerializeField] bool ignoreWhenTyping = true;

        // Input actions (no asset required)
        InputAction toggleAction;
        InputAction closeAction;

        // Remember prior cursor state
        CursorLockMode prevLock;
        bool prevVisible;

        void Awake()
        {
            // Create input actions in code
            toggleAction = new InputAction("InventoryToggle", InputActionType.Button);
            toggleAction.AddBinding("<Keyboard>/i");
            toggleAction.AddBinding("<Gamepad>/start");
            toggleAction.AddBinding("<Gamepad>/y");

            closeAction = new InputAction("InventoryClose", InputActionType.Button);
            closeAction.AddBinding("<Keyboard>/escape");
            closeAction.AddBinding("<Gamepad>/b");

            toggleAction.performed += _ => TryToggle();
            closeAction.performed += _ => { if (closeOnEscape && IsOpen()) SetOpen(false); };
        }

        void OnEnable()
        {
            toggleAction.Enable();
            closeAction.Enable();
        }

        void OnDisable()
        {
            toggleAction.Disable();
            closeAction.Disable();
        }

        bool IsOpen() => panel && panel.activeSelf;

        void TryToggle()
        {
            if (!panel) { Debug.LogWarning("InventoryToggle: No panel assigned."); return; }
            if (ignoreWhenTyping && IsTypingInInput()) return;
            if (!IsOpen())
            {
                if (requireLocalPlayer && !HasLocalPlayer()) return;
                SetOpen(true);
            }
            else
            {
                SetOpen(false);
            }
        }

        void SetOpen(bool open)
        {
            if (!panel) return;

            // Manage cursor state (optional)
            if (manageCursor)
            {
                if (open)
                {
                    prevLock = Cursor.lockState;
                    prevVisible = Cursor.visible;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = prevLock;
                    Cursor.visible = prevVisible;
                }
            }

            panel.SetActive(open);
        }

        bool HasLocalPlayer()
        {
            return NetworkClient.active &&
                   NetworkClient.connection != null &&
                   NetworkClient.connection.identity != null;
        }

        bool IsTypingInInput()
        {
            var es = EventSystem.current;
            if (!es) return false;
            var go = es.currentSelectedGameObject;
            if (!go) return false;
            // If any input field is focused, treat as typing
            return go.GetComponent<TMP_InputField>() != null ||
                   go.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
}
