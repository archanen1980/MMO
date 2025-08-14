// Assets/MMO/UI/LoginCanvasRuntimeUI.cs (AUTO-HIDE ON CONNECT / SERVER START)
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Mirror;
using MMO.Net;

namespace MMO.UI
{
    /// <summary>
    /// uGUI + TextMeshPro login built at runtime, centered.
    /// - Sprite-backed visuals (no built-in resource dependency; generates sprites if none assigned).
    /// - Auto-hides the login canvas when connected (client) or when server starts.
    /// - Reappears automatically when you disconnect/stop server.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public class LoginCanvasRuntimeUI : MonoBehaviour
    {
        [Header("Defaults")]
        public string defaultAddress = "localhost";
        public ushort defaultPort = 7777;
        public string defaultUsername = "";

        [Header("Sprites (optional)")]
        public Sprite panelSprite;
        public Sprite inputBackgroundSprite;
        public Sprite buttonSprite;

        [Header("Colors")]
        public Color panelColor = new Color(0f, 0f, 0f, 0.75f);
        public Color inputBgColor = new Color(1,1,1,0.10f);
        public Color buttonColor = new Color(1,1,1,0.18f);

        [Header("Behavior")]
        public bool autoHide = true;   // if true, hides when connected or server active

        // runtime refs
        private TelepathyTransport _transport;
        private MmoNetworkManager _nm;
        private NameAuthenticator _auth;
        private GameObject _canvasRoot; // whole canvas to toggle

        // UI refs
        private TMP_InputField _addrInput;
        private TMP_InputField _portInput;
        private TMP_InputField _userInput;

        void Awake()
        {
            _nm = GetComponent<MmoNetworkManager>() ?? FindObjectOfType<MmoNetworkManager>();
            if (_nm == null)
            {
                Debug.LogError("[UGUI] No MmoNetworkManager in scene.");
                enabled = false;
                return;
            }

            _transport = _nm.GetComponent<TelepathyTransport>() ?? FindObjectOfType<TelepathyTransport>();
            if (_transport == null) _transport = _nm.gameObject.AddComponent<TelepathyTransport>();

            if (string.IsNullOrWhiteSpace(defaultUsername))
                defaultUsername = $"Arch_{UnityEngine.Random.Range(100, 999)}";

            // Ensure sprites exist (generate simple 9-sliced if none provided)
            EnsureRuntimeSprites();

            EnsureEventSystemCompat();
            BuildCanvas();
        }

        void Start()
        {
            _nm.transport = _transport;
            if (NetworkManager.singleton != null)
                NetworkManager.singleton.transport = _transport;

            RefreshVisibility(force:true);
        }

        void Update()
        {
            RefreshVisibility();
        }

        // --- Visibility control ---
        void RefreshVisibility(bool force=false)
        {
            if (!autoHide || _canvasRoot == null) return;

            bool connectedOrServer = NetworkClient.isConnected || NetworkServer.active;
            bool wantShow = !connectedOrServer;

            if (force || _canvasRoot.activeSelf != wantShow)
                _canvasRoot.SetActive(wantShow);
        }

        // --- Sprite helpers ---
        void EnsureRuntimeSprites()
        {
            if (panelSprite == null)  panelSprite  = CreateSolidSprite(32, panelColor,  new Vector4(8,8,8,8), "PanelSprite");
            if (inputBackgroundSprite == null) inputBackgroundSprite = CreateSolidSprite(32, inputBgColor, new Vector4(8,8,8,8), "InputSprite");
            if (buttonSprite == null) buttonSprite = CreateSolidSprite(32, buttonColor, new Vector4(8,8,8,8), "ButtonSprite");
        }

        static Sprite CreateSolidSprite(int size, Color color, Vector4 borderPixels, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var c = (Color32)color;
            var arr = new Color32[size * size];
            for (int i = 0; i < arr.Length; i++) arr[i] = c;
            tex.SetPixels32(arr);
            tex.Apply(false, true); // make non-readable

            var spr = Sprite.Create(tex, new Rect(0,0,size,size), new Vector2(0.5f,0.5f), 100f, 0, SpriteMeshType.FullRect, borderPixels);
            spr.name = name;
            return spr;
        }

        // --- EventSystem setup that works for both input backends ---
        void EnsureEventSystemCompat()
        {
            var es = FindObjectOfType<EventSystem>();
            if (es == null)
                es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

            // Prefer New Input System if available
            var newMod = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newMod != null)
            {
                if (es.GetComponent(newMod) == null) es.gameObject.AddComponent(newMod);
                var old = es.GetComponent<StandaloneInputModule>(); if (old) Destroy(old);
            }
            else
            {
                if (es.GetComponent<StandaloneInputModule>() == null) es.gameObject.AddComponent<StandaloneInputModule>();
            }
        }

        void BuildCanvas()
        {
            // Canvas + scaler
            _canvasRoot = new GameObject("LoginCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Panel
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_canvasRoot.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(520, 360);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            var pimg = panel.GetComponent<Image>();
            ApplySpriteToImage(pimg, panelSprite, panelColor);

            // Vertical layout
            var v = panel.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.spacing = 12;
            v.padding = new RectOffset(16, 16, 16, 16);

            // Title
            AddLabel(panel.transform, "MMO Login", 28, TextAlignmentOptions.Center);

            // Inputs
            _addrInput = AddLabeledTMPInput(panel.transform, "Address", defaultAddress, TMP_InputField.ContentType.Standard);
            _portInput = AddLabeledTMPInput(panel.transform, "Port", defaultPort.ToString(), TMP_InputField.ContentType.IntegerNumber);
            _userInput = AddLabeledTMPInput(panel.transform, "Username", defaultUsername, TMP_InputField.ContentType.Alphanumeric);

            // Buttons row
            var row = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10; h.childForceExpandHeight = false; h.childForceExpandWidth = true;

            AddButton(row.transform, "Connect", OnClickConnect);
            AddButton(row.transform, "Host", OnClickHost);
            AddButton(row.transform, "Server", OnClickServer);
            AddButton(row.transform, "Disconnect", OnClickDisconnect);

            // Status
            AddLabel(panel.transform, "Status: idle", 16, TextAlignmentOptions.Left);
        }

        void ApplySpriteToImage(Image img, Sprite s, Color tint)
        {
            img.sprite = s;
            img.color = tint;
            img.raycastTarget = true;
            if (s != null && s.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
            else img.type = Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.useSpriteMesh = false;
        }

        TMP_Text AddLabel(Transform parent, string text, int size, TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 34);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        TMP_InputField AddLabeledTMPInput(Transform parent, string label, string value, TMP_InputField.ContentType contentType)
        {
            var block = new GameObject(label + "Block", typeof(RectTransform), typeof(VerticalLayoutGroup));
            block.transform.SetParent(parent, false);
            var v = block.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandWidth = true;

            AddLabel(block.transform, label, 18, TextAlignmentOptions.Left);

            var fieldGO = new GameObject(label + "Field", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            fieldGO.transform.SetParent(block.transform, false);
            var img = fieldGO.GetComponent<Image>();
            ApplySpriteToImage(img, inputBackgroundSprite, inputBgColor);

            var le = fieldGO.GetComponent<LayoutElement>();
            le.minHeight = 42; le.preferredHeight = 42;

            var fieldRT = fieldGO.GetComponent<RectTransform>();
            fieldRT.sizeDelta = new Vector2(0, 42);

            var textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(fieldGO.transform, false);
            var taRT = textArea.GetComponent<RectTransform>();
            taRT.anchorMin = new Vector2(0, 0);
            taRT.anchorMax = new Vector2(1, 1);
            taRT.offsetMin = new Vector2(12, 8);
            taRT.offsetMax = new Vector2(-12, -8);

            var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            phGO.transform.SetParent(textArea.transform, false);
            var phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = new Vector2(0, 0); phRT.anchorMax = new Vector2(1, 1);
            phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
            var ph = phGO.GetComponent<TextMeshProUGUI>();
            ph.text = "Enter " + label.ToLower();
            ph.fontSize = 20;
            ph.color = new Color(1,1,1,0.45f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.enableWordWrapping = false;

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(textArea.transform, false);
            var txtRT = textGO.GetComponent<RectTransform>();
            txtRT.anchorMin = new Vector2(0, 0); txtRT.anchorMax = new Vector2(1, 1);
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;
            var txt = textGO.GetComponent<TextMeshProUGUI>();
            txt.text = value;
            txt.fontSize = 20;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.enableWordWrapping = false;
            txt.overflowMode = TextOverflowModes.ScrollRect;

            var input = fieldGO.AddComponent<TMP_InputField>();
            input.textViewport = taRT;
            input.textComponent = txt;
            input.placeholder = ph;
            input.text = value;
            input.caretWidth = 2;
            input.contentType = contentType;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.scrollSensitivity = 1;
            input.characterLimit = 64;

            return input;
        }

        Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            ApplySpriteToImage(img, buttonSprite, buttonColor);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 40);

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGO.transform.SetParent(go.transform, false);
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            var txt = textGO.GetComponent<TextMeshProUGUI>();
            txt.text = label; txt.alignment = TextAlignmentOptions.Center; txt.fontSize = 20;

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = buttonColor;
            colors.highlightedColor = buttonColor * 1.2f;
            colors.pressedColor = buttonColor * 0.8f;
            colors.selectedColor = buttonColor * 1.15f;
            colors.disabledColor = new Color(buttonColor.r, buttonColor.g, buttonColor.b, 0.35f);
            btn.colors = colors;

            btn.onClick.AddListener(() => { Debug.Log($"[UGUI] Button clicked: {label}"); });
            btn.onClick.AddListener(onClick);
            btn.interactable = true;
            return btn;
        }

        // -------- Button handlers --------
        void OnClickConnect()
        {
            if (_nm == null) { Debug.LogError("[UGUI] NetworkManager is null"); return; }
            string addr = _addrInput?.text ?? defaultAddress;
            string user = string.IsNullOrWhiteSpace(_userInput?.text) ? defaultUsername : _userInput.text.Trim();
            ushort port = (_portInput != null && ushort.TryParse(_portInput.text, out var p)) ? p : defaultPort;

            _transport.port = port;
            _nm.networkAddress = addr;

            _auth = _nm.GetComponent<NameAuthenticator>() ?? _nm.gameObject.AddComponent<NameAuthenticator>();
            _auth.pendingUsername = user;

            if (NetworkServer.active) _nm.StopHost();
            Debug.Log($"[UGUI] StartClient → {addr}:{port} user='{_auth.pendingUsername}'");
            _nm.StartClient();
        }

        void OnClickHost()
        {
            if (_nm == null) { Debug.LogError("[UGUI] NetworkManager is null"); return; }
            ushort port = (_portInput != null && ushort.TryParse(_portInput.text, out var p)) ? p : defaultPort;
            _transport.port = port;

            string user = string.IsNullOrWhiteSpace(_userInput?.text) ? defaultUsername : _userInput.text.Trim();
            _auth = _nm.GetComponent<NameAuthenticator>() ?? _nm.gameObject.AddComponent<NameAuthenticator>();
            _auth.pendingUsername = user;

            if (NetworkClient.isConnected) _nm.StopClient();
            Debug.Log($"[UGUI] StartHost on port {port} user='{_auth.pendingUsername}'");
            _nm.StartHost();
        }

        void OnClickServer()
        {
            if (_nm == null) { Debug.LogError("[UGUI] NetworkManager is null"); return; }
            ushort port = (_portInput != null && ushort.TryParse(_portInput.text, out var p)) ? p : defaultPort;
            _transport.port = port;
            if (NetworkClient.isConnected) _nm.StopClient();
            Debug.Log($"[UGUI] StartServer on port {port}");
            _nm.StartServer();
        }

        void OnClickDisconnect()
        {
            if (_nm == null) return;

            if (NetworkServer.active && NetworkClient.isConnected)      { Debug.Log("[UGUI] StopHost");   _nm.StopHost();   }
            else if (NetworkServer.active)                               { Debug.Log("[UGUI] StopServer"); _nm.StopServer(); }
            else if (NetworkClient.isConnected)                           { Debug.Log("[UGUI] StopClient"); _nm.StopClient(); }
        }
    }
}
