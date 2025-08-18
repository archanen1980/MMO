// Assets/MMO/Common/UX/HudCursorHold.cs
using UnityEngine;
using UnityEngine.Events;

[DefaultExecutionOrder(10000)] // run after most input scripts so we can override cursor state
public class HudCursorHold : MonoBehaviour
{
    [Header("Key")]
    public KeyCode holdKey = KeyCode.LeftControl;

    [Header("Lifecycle")]
    public bool dontDestroyOnLoad = true;

    [Header("Disable these behaviours while cursor is free (optional)")]
    public Behaviour[] disableWhileFree; // e.g., your Look/Movement, PlayerInput, Cinemachine providers, etc.

    [Header("Events")]
    public UnityEvent<bool> onStateChanged; // true=free cursor, false=restored

    // --- Static so other UI can respect the hold (e.g., ChatWindow) ---
    static bool _active;
    public static bool IsActive => _active;

    struct Entry { public Behaviour b; public bool wasEnabled; }
    Entry[] _entries = System.Array.Empty<Entry>();

    bool _entered;
    CursorLockMode _prevLock;
    bool _prevVisible;

    void Awake()
    {
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        if (disableWhileFree != null && disableWhileFree.Length > 0)
        {
            _entries = new Entry[disableWhileFree.Length];
            for (int i = 0; i < disableWhileFree.Length; i++)
                _entries[i] = new Entry { b = disableWhileFree[i], wasEnabled = disableWhileFree[i] ? disableWhileFree[i].enabled : false };
        }
    }

    void OnDisable()
    {
        if (_entered) ExitHold();
    }

    void Update()
    {
        if (Input.GetKeyDown(holdKey)) EnterHold();
        if (Input.GetKeyUp(holdKey)) ExitHold();
    }

    void LateUpdate()
    {
        if (!_entered) return;
        // Enforce each frame in case something else tries to relock the cursor.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EnterHold()
    {
        if (_entered) return;
        _entered = true;
        _active = true;

        _prevLock = Cursor.lockState;
        _prevVisible = Cursor.visible;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        for (int i = 0; i < _entries.Length; i++)
        {
            var b = _entries[i].b;
            if (!b) continue;
            _entries[i].wasEnabled = b.enabled;
            b.enabled = false; // temporarily disable gameplay controls
        }

        onStateChanged?.Invoke(true);
    }

    void ExitHold()
    {
        if (!_entered) return;
        _entered = false;
        _active = false;

        for (int i = 0; i < _entries.Length; i++)
        {
            var b = _entries[i].b;
            if (!b) continue;
            b.enabled = _entries[i].wasEnabled; // restore prior enable state
        }

        Cursor.lockState = _prevLock;
        Cursor.visible = _prevVisible;

        onStateChanged?.Invoke(false);
    }
}
