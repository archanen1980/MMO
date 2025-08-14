// Assets/Hughes_Jeremiah_Assets/MMO/UI/ChatCanvasRuntimeUI.cs
using System.Collections;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MMO.Shared;
using MMO.Gameplay;

namespace MMO.UI
{
    /// <summary>
    /// Global chat UI (uGUI) built at runtime.
    /// - Hidden until connected AND local player exists.
    /// - Panel bottom-left with scrollable log + single-line input.
    /// - Enter to focus; Enter (focused) to send; Esc to unfocus.
    /// - Unity 6-safe font (OS font → LegacyRuntime.ttf).
    /// - Viewport uses RectMask2D, stretches to ScrollRect; ScrollRect bottom sits above input.
    /// - Content is bottom-anchored & auto-heights to preferred text.
    /// </summary>
    public class ChatCanvasRuntimeUI : MonoBehaviour
    {
        [Header("Build")]
        public bool buildUIOnAwake = true;
        public bool dontDestroyOnLoad = true;

        [Header("Layout")]
        public Vector2 panelSize = new Vector2(520, 220);
        public int fontSize = 14;
        [Range(0, 1)] public float backgroundAlpha = 0.35f;
        public int maxLines = 200;
        public Vector2 padding = new Vector2(6, 6);

        [Header("Hotkeys")]
        public KeyCode focusKey = KeyCode.Return;
        public KeyCode unfocusKey = KeyCode.Escape;

        [Header("Font")]
        [Tooltip("Optional: assign a Font asset. If null, uses an OS font; fallback LegacyRuntime.ttf.")]
        public Font uiFont;

        // Layout constants
        const float INPUT_HEIGHT = 36f;
        const float PANEL_MARGIN = 6f;
        const float GAP = 6f;

        // Runtime refs
        Canvas _canvas;
        InputField _input;
        ScrollRect _scroll;
        Text _logText;
        RectTransform _contentRT;
        RectTransform _viewportRT;
        RectTransform _scrollRT;
        GameObject _panelGO;

        // State
        readonly StringBuilder _log = new StringBuilder(4096);
        int _lineCount = 0;
        bool _hasFocus = false;
        bool _handlerRegistered = false;
        bool _uiVisible = false;
        Font _resolvedFont;

        void Awake()
        {
            if (buildUIOnAwake) BuildUI();
            EnsureHandlerRegistered();
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        }

        void OnEnable() => EnsureHandlerRegistered();
        void OnDisable() { /* keep handler registered across loads */ }

        void Update()
        {
            UpdateVisibility(); // show/hide based on connection + local player

            if (!_uiVisible || _input == null) return;

            if (!_hasFocus && Input.GetKeyDown(focusKey)) { FocusInput(); return; }
            if (_hasFocus && Input.GetKeyDown(KeyCode.Return)) { TrySend(); return; }
            if (_hasFocus && Input.GetKeyDown(unfocusKey)) { UnfocusInput(); return; }
        }

        // --- Visibility gating ---
        void UpdateVisibility()
        {
            bool ready = NetworkClient.isConnected && NetworkClient.localPlayer != null;
            if (ready == _uiVisible) return;

            _uiVisible = ready;
            if (_panelGO != null) _panelGO.SetActive(_uiVisible);

            if (!_uiVisible)
            {
                // Drop focus so Enter doesn't type into a hidden field
                UnfocusInput();
            }
        }

        // --- Network handler ---
        void EnsureHandlerRegistered()
        {
            if (_handlerRegistered) return;
            NetworkClient.UnregisterHandler<ChatMessage>(); // avoid duplicates
            NetworkClient.RegisterHandler<ChatMessage>(OnChatMessage);
            _handlerRegistered = true;
        }

        void OnChatMessage(ChatMessage msg)
        {
            AppendLine($"<b>{Escape(msg.from)}:</b> {Escape(msg.text)}");
        }

        // --- Sending ---
        void TrySend()
        {
            string txt = _input.text ?? "";
            txt = txt.Trim();

            if (string.IsNullOrEmpty(txt)) { UnfocusInput(); return; }
            if (!NetworkClient.isConnected) { AppendLine("<i>(not connected)</i>"); _input.text = ""; return; }

            var lp = NetworkClient.localPlayer;
            if (lp == null) { AppendLine("<i>(no player yet)</i>"); _input.text = ""; return; }
            if (!lp.TryGetComponent(out ChatBehaviour chat)) { AppendLine("<i>(chat component missing on player)</i>"); _input.text = ""; return; }

            chat.CmdSendChat(txt);
            _input.text = "";
            _input.ActivateInputField();
        }

        // --- UI building ---
        void BuildUI()
        {
            // Ensure EventSystem
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                if (dontDestroyOnLoad) DontDestroyOnLoad(es);
            }

            // Canvas
            var canvasGO = new GameObject("ChatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = canvasGO.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            if (dontDestroyOnLoad) DontDestroyOnLoad(canvasGO);

            // Root panel
            _panelGO = CreateUI("ChatPanel", canvasGO.transform, out RectTransform prt);
            prt.anchorMin = new Vector2(0, 0);
            prt.anchorMax = new Vector2(0, 0);
            prt.pivot = new Vector2(0, 0);
            prt.anchoredPosition = new Vector2(10, 10);
            prt.sizeDelta = panelSize;
            _panelGO.SetActive(false); // start hidden; visibility managed at runtime

            var bg = _panelGO.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, backgroundAlpha);

            // ScrollRect (stretched; we leave space for input row at bottom)
            var scrollGO = CreateUI("ScrollRect", _panelGO.transform, out _scrollRT);
            _scrollRT.anchorMin = new Vector2(0, 0);
            _scrollRT.anchorMax = new Vector2(1, 1);
            _scrollRT.offsetMin = new Vector2(PANEL_MARGIN, PANEL_MARGIN);
            _scrollRT.offsetMax = new Vector2(-PANEL_MARGIN, -PANEL_MARGIN);
            _scroll = scrollGO.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            // Viewport (RectMask2D) — stretch to ScrollRect
            var viewport = CreateUI("Viewport", scrollGO.transform, out _viewportRT);
            viewport.gameObject.AddComponent<RectMask2D>();
            _viewportRT.anchorMin = Vector2.zero;
            _viewportRT.anchorMax = Vector2.one;
            _viewportRT.offsetMin = Vector2.zero;
            _viewportRT.offsetMax = Vector2.zero;
            _viewportRT.pivot = new Vector2(0, 1);
            _scroll.viewport = _viewportRT;

            // Content (bottom-anchored)
            var content = CreateUI("Content", viewport.transform, out _contentRT);
            _contentRT.anchorMin = new Vector2(0, 0);
            _contentRT.anchorMax = new Vector2(1, 0);
            _contentRT.pivot = new Vector2(0, 0);
            _contentRT.anchoredPosition = Vector2.zero;
            _contentRT.sizeDelta = new Vector2(0, 100f);
            _scroll.content = _contentRT;

            // Font
            _resolvedFont = ResolveFont();
            if (_resolvedFont == null)
                Debug.LogWarning("ChatCanvasRuntimeUI: No font resolved. Assign a font to 'uiFont' on the component.");

            // Log text
            var textGO = CreateUI("LogText", content.transform, out RectTransform trt);
            _logText = textGO.AddComponent<Text>();
            _logText.font = _resolvedFont;
            _logText.fontSize = fontSize;
            _logText.alignment = TextAnchor.LowerLeft;
            _logText.supportRichText = true;
            _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _logText.verticalOverflow = VerticalWrapMode.Overflow;
            _logText.color = Color.white;
            _logText.maskable = true;
            _logText.raycastTarget = false;

            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 1);
            trt.pivot = new Vector2(0, 0);
            trt.offsetMin = new Vector2(padding.x, padding.y);
            trt.offsetMax = new Vector2(-padding.x, -padding.y);
            _logText.text = "";

            // Input row (anchored to bottom of panel)
            var inputGO = CreateUI("InputField", _panelGO.transform, out RectTransform irt);
            irt.anchorMin = new Vector2(0, 0);
            irt.anchorMax = new Vector2(1, 0);
            irt.pivot = new Vector2(0.5f, 0);
            irt.sizeDelta = new Vector2(0, INPUT_HEIGHT);
            irt.anchoredPosition = new Vector2(0, PANEL_MARGIN);

            var inputBG = inputGO.AddComponent<Image>();
            inputBG.color = new Color(0, 0, 0, backgroundAlpha + 0.1f);
            inputBG.raycastTarget = true;

            _input = inputGO.AddComponent<InputField>();
            _input.lineType = InputField.LineType.SingleLine;

            var placeholderGO = CreateUI("Placeholder", inputGO.transform, out RectTransform phr);
            var placeholder = placeholderGO.AddComponent<Text>();
            placeholder.font = _resolvedFont;
            placeholder.fontSize = fontSize;
            placeholder.text = "Type message and press Enter…";
            placeholder.color = new Color(1, 1, 1, 0.45f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.maskable = true;
            placeholder.raycastTarget = false;

            var textGO2 = CreateUI("Text", inputGO.transform, out RectTransform txr);
            var textComp = textGO2.AddComponent<Text>();
            textComp.font = _resolvedFont;
            textComp.fontSize = fontSize;
            textComp.color = Color.white;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.maskable = true;
            textComp.raycastTarget = false;

            _input.targetGraphic = inputBG;
            _input.textComponent = textComp;
            _input.placeholder = placeholder;

            // Inner padding
            phr.anchorMin = new Vector2(0, 0);
            phr.anchorMax = new Vector2(1, 1);
            phr.offsetMin = new Vector2(8, 6);
            phr.offsetMax = new Vector2(-8, -6);

            txr.anchorMin = new Vector2(0, 0);
            txr.anchorMax = new Vector2(1, 1);
            txr.offsetMin = new Vector2(8, 6);
            txr.offsetMax = new Vector2(-8, -6);

            // Move ScrollRect bottom just above input
            float bottomOffset = PANEL_MARGIN + INPUT_HEIGHT + GAP;
            _scrollRT.offsetMin = new Vector2(PANEL_MARGIN, bottomOffset);
            _scrollRT.offsetMax = new Vector2(-PANEL_MARGIN, -PANEL_MARGIN);

            // Layout settle
            StartCoroutine(InitializeAfterFrame());
        }

        IEnumerator InitializeAfterFrame()
        {
            yield return null;
            UpdateContentHeight();
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
            UpdateVisibility(); // re-evaluate now that sizes are valid
        }

        Font ResolveFont()
        {
            if (uiFont != null) return uiFont;
            try
            {
                string[] candidates = { "Arial", "Segoe UI", "Helvetica", "Liberation Sans", "DejaVu Sans", "Tahoma" };
                var f = Font.CreateDynamicFontFromOSFont(candidates, Mathf.Max(14, fontSize));
                if (f != null) { Debug.Log("ChatCanvasRuntimeUI: Using OS dynamic font."); return f; }
            }
            catch { }
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null) { Debug.Log("ChatCanvasRuntimeUI: Using LegacyRuntime.ttf."); return f; }
            }
            catch { }
            return null;
        }

        GameObject CreateUI(string name, Transform parent, out RectTransform rt)
        {
            var go = new GameObject(name, typeof(RectTransform));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return go;
        }

        void FocusInput()
        {
            _hasFocus = true;
            _input?.ActivateInputField();
            _input?.Select();
        }

        void UnfocusInput()
        {
            _hasFocus = false;
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void AppendLine(string line)
        {
            _log.AppendLine(line);
            _lineCount++;
            if (_lineCount > maxLines)
            {
                int cut = Mathf.Min(_log.Length, 2048);
                _log.Remove(0, cut);
                _lineCount = Mathf.Max(0, _lineCount - 50);
            }

            if (_logText != null)
            {
                _logText.text = _log.ToString();
                _logText.color = Color.white;
                _logText.canvasRenderer.SetAlpha(1f);
            }

            UpdateContentHeight();

            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        void UpdateContentHeight()
        {
            if (_contentRT == null || _logText == null || _viewportRT == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_logText.rectTransform);

            float textPrefH = Mathf.Ceil(_logText.preferredHeight) + padding.y * 2f;
            float viewportH = _viewportRT.rect.height;
            if (viewportH <= 0f) return;

            float newH = Mathf.Max(viewportH, textPrefH);
            var size = _contentRT.sizeDelta;
            if (!Mathf.Approximately(size.y, newH))
                _contentRT.sizeDelta = new Vector2(size.x, newH);
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
