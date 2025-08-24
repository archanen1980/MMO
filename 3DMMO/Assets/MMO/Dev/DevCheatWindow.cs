// DevCheatWindow.cs
// Self-contained development cheats overlay (remove this file to strip cheats).
// F10 toggles a tiny UI to grant items by numeric ID + amount.
// Prefers calling Mirror Commands on the local player's PlayerInventory.

using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.Dev
{
    [DefaultExecutionOrder(-5000)]
    public class DevCheatWindow : MonoBehaviour
    {
        // ------------ Singleton / Auto-create ------------
        public static DevCheatWindow Instance { get; private set; }

        [Header("Lifecycle")]
        [Tooltip("If true, auto-create a singleton DevCheatWindow at runtime if none exists.")]
        [SerializeField] private bool autoCreateIfMissing = true;

        // ------------ Hotkeys & Behavior ------------
        [Header("Behavior")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F10;
        [SerializeField] private bool openOnStart = false;
        [SerializeField] private int defaultAmount = 1;
        [SerializeField] private bool lockAndShowCursorWhenOpen = true;
        [Tooltip("Optional components to disable while the cheat window is open (e.g., FPS controller).")]
        [SerializeField] private Behaviour[] disableWhileOpen;

        // ------------ Inventory Hooking ------------
        [Header("Inventory Hooking")]
        [Tooltip("If assigned, used directly. Otherwise this window auto-finds at runtime.")]
        [SerializeField] private MonoBehaviour inventoryTarget;
        [Tooltip("Optional: fully-qualified type for static API, e.g. MMO.Inventory.PlayerInventory, Assembly-CSharp")]
        [SerializeField] private string staticInventoryType = "";

        [Header("Auto-Find (Runtime Player)")]
        [Tooltip("Try to resolve your player’s inventory at runtime automatically.")]
        [SerializeField] private bool autoFindInventoryAtRuntime = true;
        [Tooltip("Player tag to search if Mirror local player isn’t available.")]
        [SerializeField] private string playerTag = "Player";
        [Tooltip("Optional hint: class name of your inventory component (full or simple name).")]
        [SerializeField] private string inventoryComponentTypeHint = "PlayerInventory";

        // ------------ UI References ------------
        private Canvas _canvas;
        private RectTransform _panel;
        private TMP_InputField _itemIdInput;
        private TMP_InputField _amountInput;
        private TextMeshProUGUI _feedback;
        private Button _giveBtn, _closeBtn, _clearBtn;

        private bool _isOpen;
        private CursorLockMode _prevLock;
        private bool _prevVisible;

        // Public event hook (alternate integration path if you want)
        public static event Action<int, int> OnCheatGiveItem;

        // Prefer Mirror Commands first; then common helper names
        private static readonly string[] CandidateMethods =
        {
            "CmdCheatAddItem", "CmdCheatAddItemS", "CmdAddItem", // Commands (instance)
            "CheatGiveItem","GiveItem","TryGiveItem","AddItem","TryAddItem","GrantItem"
        };

        // ------------ Bootstrapping ------------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureCreated()
        {
            if (FindObjectOfType<DevCheatWindow>(true)) return;
            var go = new GameObject("__DevCheatWindow_Auto");
            var comp = go.AddComponent<DevCheatWindow>();
            comp.autoCreateIfMissing = true;
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                if (autoCreateIfMissing)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            BuildUi();
            SetOpen(openOnStart);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                SetOpen(!_isOpen);
            }
        }

        private void OnDisable()
        {
            if (_isOpen)
                RestoreCursor();
        }

        // ------------ UI Construction ------------
        private void BuildUi()
        {
            // Canvas (overlay)
            var canvasGO = new GameObject("DevCheatsCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60000; // top-most
            canvasGO.GetComponent<GraphicRaycaster>().ignoreReversedGraphics = true;

            // Panel
            var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _panel = (RectTransform)panelGO.transform;
            _panel.SetParent(canvasGO.transform, false);
            _panel.pivot = new Vector2(1, 1);
            _panel.anchorMin = _panel.anchorMax = new Vector2(1, 1); // top-right
            _panel.anchoredPosition = new Vector2(-16f, -16f);
            var panelImg = panelGO.GetComponent<Image>();
            panelImg.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 8;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;

            var fitter = panelGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title
            AddLabel(_panel, "Dev Cheats", 18, FontStyles.Bold);

            // Row: Item ID
            _itemIdInput = AddLabeledIntInput(_panel, "Item ID", "");
            // Row: Amount
            _amountInput = AddLabeledIntInput(_panel, "Amount", defaultAmount.ToString());

            // Buttons row
            var row = AddRow(_panel, 3, 8f);
            _giveBtn = AddButton(row, "Give", OnClickGive, 80f);
            _clearBtn = AddButton(row, "Clear", () =>
            {
                _itemIdInput.text = "";
                _amountInput.text = defaultAmount.ToString();
                SetFeedback("");
            }, 80f);
            _closeBtn = AddButton(row, "Close", () => SetOpen(false), 80f);

            // Feedback
            _feedback = AddLabel(_panel, "", 14, FontStyles.Italic);
            _feedback.color = new Color(0.8f, 0.9f, 1f, 0.9f);

            // Start hidden
            canvasGO.SetActive(false);
        }

        // ------------ UI Helpers ------------
        private static RectTransform AddRow(RectTransform parent, int minChilds, float spacing)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var hl = go.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = spacing;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = false; hl.childForceExpandWidth = false;
            hl.childControlHeight = true; hl.childForceExpandHeight = false;
            go.GetComponent<LayoutElement>().minHeight = 30f;
            return rt;
        }

        private static TextMeshProUGUI AddLabel(RectTransform parent, string text, int size, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.fontStyle = style;
            tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return tmp;
        }

        private static Button AddButton(RectTransform parent, string label, Action onClick, float minWidth)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);

            go.GetComponent<Image>().color = new Color(1, 1, 1, 0.08f);
            var le = go.GetComponent<LayoutElement>(); le.minWidth = minWidth; le.minHeight = 28f; le.preferredWidth = minWidth;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1, 1, 1, 0.16f);
            colors.pressedColor = new Color(1, 1, 1, 0.22f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var l = lgo.GetComponent<TextMeshProUGUI>();
            l.text = label; l.fontSize = 14; l.alignment = TextAlignmentOptions.Center; l.color = Color.white; l.raycastTarget = false;

            return btn;
        }

        private static TMP_InputField AddLabeledIntInput(RectTransform parent, string label, string defaultValue)
        {
            // Row container
            var row = AddRow(parent, 2, 8f);

            // Label
            var title = AddLabel(row, label, 14);
            var titleLE = title.gameObject.AddComponent<LayoutElement>();
            titleLE.minWidth = 70f;

            // Input background
            var bgGO = new GameObject(label + "_BG", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var bgRT = (RectTransform)bgGO.transform; bgRT.SetParent(row, false);
            bgRT.sizeDelta = new Vector2(140f, 28f);
            var bgImg = bgGO.GetComponent<Image>(); bgImg.color = new Color(1, 1, 1, 0.06f);
            var bgLE = bgGO.GetComponent<LayoutElement>(); bgLE.minWidth = 140f; bgLE.minHeight = 28f;

            // TMP_InputField
            var vpGO = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var vpRT = (RectTransform)vpGO.transform; vpRT.SetParent(bgRT, false);
            vpRT.anchorMin = new Vector2(0, 0); vpRT.anchorMax = new Vector2(1, 1);
            vpRT.offsetMin = new Vector2(8, 4); vpRT.offsetMax = new Vector2(-8, -4);

            var txtGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var txtRT = (RectTransform)txtGO.transform; txtRT.SetParent(vpRT, false);
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one; txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            var tmp = txtGO.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.MidlineLeft; tmp.fontSize = 14; tmp.raycastTarget = false;

            var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var phRT = (RectTransform)phGO.transform; phRT.SetParent(vpRT, false);
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one; phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
            var ph = phGO.GetComponent<TextMeshProUGUI>();
            ph.text = "0"; ph.fontSize = 14; ph.color = new Color(1, 1, 1, 0.35f); ph.raycastTarget = false;

            var input = bgGO.AddComponent<TMP_InputField>();
            input.textViewport = vpRT;
            input.textComponent = tmp;
            input.placeholder = ph;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.text = defaultValue ?? "";
            input.customCaretColor = true; input.caretColor = Color.white; input.caretWidth = 2; input.caretBlinkRate = 0.8f;
#if UNITY_EDITOR || UNITY_STANDALONE
            input.shouldHideMobileInput = true;
#endif
            return input;
        }

        // ------------ Open/Close & Cursor ------------
        private void SetOpen(bool value)
        {
            if (_isOpen == value) return;
            _isOpen = value;

            _canvas.gameObject.SetActive(_isOpen);

            if (_isOpen && lockAndShowCursorWhenOpen)
            {
                _prevLock = Cursor.lockState;
                _prevVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                if (disableWhileOpen != null)
                    foreach (var b in disableWhileOpen) if (b) b.enabled = false;

                // Try to resolve inventory when opening
                if (autoFindInventoryAtRuntime && !inventoryTarget) EnsureInventoryTarget();

                // Focus first field next frame
                StartCoroutine(FocusInputNextFrame(_itemIdInput));
            }
            else if (!_isOpen && lockAndShowCursorWhenOpen)
            {
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevVisible;

            if (disableWhileOpen != null)
                foreach (var b in disableWhileOpen) if (b) b.enabled = true;

            var es = EventSystem.current;
            if (es) es.SetSelectedGameObject(null);
        }

        private System.Collections.IEnumerator FocusInputNextFrame(TMP_InputField field)
        {
            yield return null;
            if (!field) yield break;
            field.ActivateInputField();
            field.Select();
            field.caretPosition = field.text?.Length ?? 0;
            field.MoveTextEnd(false);
            field.ForceLabelUpdate();
        }

        // ------------ Actions ------------
        private void OnClickGive()
        {
            if (autoFindInventoryAtRuntime && !inventoryTarget) EnsureInventoryTarget();

            if (!int.TryParse(_itemIdInput.text, out int itemId))
            {
                SetFeedback("<color=#ff8080>Invalid Item ID</color>");
                return;
            }
            if (!int.TryParse(_amountInput.text, out int amount) || amount <= 0)
            {
                SetFeedback("<color=#ff8080>Invalid Amount</color>");
                return;
            }

            bool ok = TryInvokeInventory(itemId, amount);
            if (ok)
            {
                SetFeedback($"Gave item <b>{itemId}</b> x <b>{amount}</b>.");
                // Keep focus in amount for quick repeats
                StartCoroutine(FocusInputNextFrame(_amountInput));
            }
            else
            {
                SetFeedback("<color=#ff8080>No inventory hook found. See console.</color>");
                Debug.LogWarning("[DevCheatWindow] No inventory method found. " +
                                 "Subscribe to OnCheatGiveItem, assign Inventory Target, use static type," +
                                 " or ensure PlayerInventory has CmdCheatAddItem(int,int).");
            }
        }

        private void SetFeedback(string msg)
        {
            if (_feedback) _feedback.text = msg ?? "";
        }

        // ------------ Inventory Invocation ------------
        private bool TryInvokeInventory(int itemId, int amount)
        {
            bool handled = false;

            // Option 1: Event
            var evt = OnCheatGiveItem;
            if (evt != null)
            {
                try { evt.Invoke(itemId, amount); handled = true; }
                catch (Exception e) { Debug.LogException(e); }
            }

            // Option 2: Inspector/Auto-Find target (instance methods – includes Mirror Commands)
            if (!handled && inventoryTarget)
                handled = InvokeOnObject(inventoryTarget, itemId, amount);

            // Option 3: Static helper by type name
            if (!handled && !string.IsNullOrWhiteSpace(staticInventoryType))
            {
                var t = Type.GetType(staticInventoryType, throwOnError: false);
                if (t != null) handled = InvokeOnType(t, itemId, amount);
                else Debug.LogWarning($"[DevCheatWindow] StaticInventoryType '{staticInventoryType}' not found.");
            }

            // Option 4: Common guesses (non-fatal)
            if (!handled)
            {
                string[] guesses =
                {
                    "MMO.Inventory.PlayerInventory, Assembly-CSharp",
                    "MMO.Inventory.InventoryService, Assembly-CSharp",
                    "InventoryManager, Assembly-CSharp",
                    "PlayerInventory, Assembly-CSharp"
                };
                foreach (var g in guesses)
                {
                    var t = Type.GetType(g, false);
                    if (t != null && InvokeOnType(t, itemId, amount)) { handled = true; break; }
                }
            }

            return handled;
        }

        private bool InvokeOnObject(object obj, int id, int amount)
        {
            var t = obj.GetType();

            // First, try (int,int) signatures (e.g., CmdCheatAddItem)
            foreach (var name in CandidateMethods)
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int) }, null);
                if (m != null) return CallBoolish(m, obj, id, amount);
            }

            // Then, try (string,int) for CmdCheatAddItemS or similar
            foreach (var name in CandidateMethods)
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(int) }, null);
                if (m != null) return CallBoolish(m, obj, id.ToString(), amount);
            }

            return false;
        }

        private bool InvokeOnType(Type t, int id, int amount)
        {
            foreach (var name in CandidateMethods)
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                    new[] { typeof(int), typeof(int) }, null);
                if (m != null) return CallBoolish(m, null, id, amount);
            }
            foreach (var name in CandidateMethods)
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(int) }, null);
                if (m != null) return CallBoolish(m, null, id.ToString(), amount);
            }
            return false;
        }

        private bool CallBoolish(MethodInfo m, object target, params object[] args)
        {
            try
            {
                var result = m.Invoke(target, args);
                if (result is bool b) return b;
                return true; // void assumed success
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        // ------------ Auto-Find (Mirror local player → tag → scan) ------------
        private void EnsureInventoryTarget()
        {
            // 1) Mirror local player via reflection (no compile-time dependency)
            try
            {
                var netClientType = Type.GetType("Mirror.NetworkClient, Mirror", throwOnError: false);
                if (netClientType != null)
                {
                    var activeProp = netClientType.GetProperty("active", BindingFlags.Static | BindingFlags.Public);
                    var localPlayerProp = netClientType.GetProperty("localPlayer", BindingFlags.Static | BindingFlags.Public);
                    bool isActive = activeProp != null && (bool)activeProp.GetValue(null);
                    var localPlayer = localPlayerProp != null ? localPlayerProp.GetValue(null) : null;
                    if (isActive && localPlayer != null)
                    {
                        var goProp = localPlayer.GetType().GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public);
                        var go = goProp != null ? goProp.GetValue(localPlayer) as GameObject : null;
                        if (go)
                        {
                            var comp = FindInventoryComponentOn(go);
                            if (comp) { inventoryTarget = comp; return; }
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // 2) Tagged player
            var tagged = SafeFindWithTag(playerTag);
            if (tagged)
            {
                var comp = FindInventoryComponentOn(tagged);
                if (comp) { inventoryTarget = comp; return; }
            }

            // 3) Anywhere in scene: first component that exposes candidate methods
            var all = FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mb in all)
            {
                if (IsInventoryLike(mb.GetType())) { inventoryTarget = mb; return; }
            }
        }

        private GameObject SafeFindWithTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            try { return GameObject.FindGameObjectWithTag(tag); }
            catch { return null; } // tag might not exist in TagManager
        }

        private MonoBehaviour FindInventoryComponentOn(GameObject go)
        {
            if (!go) return null;

            // If a type hint is provided, try exact/full name match first
            if (!string.IsNullOrWhiteSpace(inventoryComponentTypeHint))
            {
                var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var c in comps)
                {
                    var t = c.GetType();
                    if (string.Equals(t.FullName, inventoryComponentTypeHint, StringComparison.Ordinal) ||
                        string.Equals(t.Name, inventoryComponentTypeHint, StringComparison.Ordinal))
                    {
                        if (IsInventoryLike(t)) return c;
                    }
                }
            }

            // Otherwise, pick the first component exposing any candidate method
            {
                var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var c in comps) if (IsInventoryLike(c.GetType())) return c;
            }

            return null;
        }

        private bool IsInventoryLike(Type t)
        {
            foreach (var name in CandidateMethods)
            {
                // instance (int,int)
                if (t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                                new[] { typeof(int), typeof(int) }, null) != null) return true;
                // instance (string,int)
                if (t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                                new[] { typeof(string), typeof(int) }, null) != null) return true;
            }
            return false;
        }
    }
}
