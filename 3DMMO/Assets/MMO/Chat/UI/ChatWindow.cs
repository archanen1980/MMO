// Assets/MMO/Chat/UI/ChatWindow.cs
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MMO.Chat;        // ChatChannel, ChatMessage
using MMO.Chat.UI;     // ChatLine

namespace MMO.Chat.UI
{
    public class ChatWindow : MonoBehaviour
    {
        [Header("Mouse / Focus")]
        [SerializeField] bool manageCursorWhenFocused = true;   // unlock + show cursor when chat is focused
        [SerializeField] Behaviour[] disableWhileFocused;       // optional: drag your FPS controller, input scripts, etc. here

        [Header("Wiring")]
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] RectTransform content;
        [SerializeField] ChatLine linePrefab;

        [Header("Input bar")]
        [SerializeField] TMP_InputField inputField;
        [SerializeField] TMP_Dropdown channelDropdown;
        [SerializeField] Button sendButton;

        [Header("Tabs")]
        [SerializeField] Transform tabBar;
        [SerializeField] Toggle tabButtonPrefab;

        [Header("Filters panel (optional)")]
        [SerializeField] GameObject filtersPanel;
        [SerializeField] Toggle systemT, generalT, sayT, whisperT, partyT, guildT, tradeT, lootT, combatT, globalT;

        [Header("Behavior")]
        [SerializeField] int maxLines = 300;
        [SerializeField] bool autoScroll = true;   // keep view pinned to bottom
        [SerializeField] bool timestamp24h = true;

        [Header("Hotkeys")]
        [SerializeField] KeyCode focusKey = KeyCode.Return;     // Enter focuses when not focused
        [SerializeField] KeyCode cancelKey = KeyCode.Escape;    // Esc unfocuses
        [SerializeField] KeyCode quickSlashKey = KeyCode.Slash; // '/' focuses and preloads '/'

        [Header("Send settings")]
        [SerializeField] bool keepFocusAfterSend = false;
        bool _suppressEnterRefocus;
        [SerializeField] bool showTimestamps = false; // uncheck to hide timestamps

        [Serializable] public class Tab { public string label; public ChatChannel mask; }
        [SerializeField]
        List<Tab> tabs = new()
        {
            new Tab{ label="All",     mask=ChatChannel.All },
            new Tab{ label="General", mask=ChatChannel.General|ChatChannel.Say|ChatChannel.Whisper|ChatChannel.Global },
            new Tab{ label="Group",   mask=ChatChannel.Party|ChatChannel.Guild|ChatChannel.Say|ChatChannel.Whisper },
            new Tab{ label="Loot",    mask=ChatChannel.Loot|ChatChannel.System },
            new Tab{ label="Combat",  mask=ChatChannel.Combat|ChatChannel.System },
        };

        int _activeTab = 0;
        readonly List<ChatLine> _lines = new();

        // persistence
        const string KeyTab = "chat.tab";
        const string KeyPosX = "chat.posx";
        const string KeyPosY = "chat.posy";
        const string KeyW = "chat.w";
        const string KeyH = "chat.h";

        // Mouse/cursor state cache
        CursorLockMode _prevLockMode;
        bool _prevCursorVisible;
        bool _cursorManagedThisFocus;
        public static event Action<bool> OnChatFocusChanged; // notify others (true=focused)

        // context-menu instance tracker
        GameObject _openMenuGO;

        void Awake()
        {
            BuildTabs();
            BuildChannelDropdown();

            if (sendButton) sendButton.onClick.AddListener(SendFromInput);
            if (inputField)
            {
                inputField.onSubmit.AddListener(_ => SendFromInput());
                inputField.onFocusSelectAll = false;
            }

            _activeTab = PlayerPrefs.GetInt(KeyTab, 0);
            SelectTab(_activeTab);

            var rt = (RectTransform)transform;
            var pos = rt.anchoredPosition;
            pos.x = PlayerPrefs.GetFloat(KeyPosX, pos.x);
            pos.y = PlayerPrefs.GetFloat(KeyPosY, pos.y);
            rt.anchoredPosition = pos;
            var size = rt.sizeDelta;
            size.x = PlayerPrefs.GetFloat(KeyW, size.x);
            size.y = PlayerPrefs.GetFloat(KeyH, size.y);
            rt.sizeDelta = size;

            EnsureIndex0IsBottomRig();       // << make child index 0 render at the bottom

            // ---- TAB BAR RIGHT-CLICK: create new tabs on bar background ----
            if (tabBar)
            {
                // ensure the bar can receive raycasts even on "empty" space
                var img = tabBar.GetComponent<Image>() ?? tabBar.gameObject.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0); // transparent background
                img.raycastTarget = true;

                var trc = tabBar.gameObject.GetComponent<TabBarRightClickCatcher>();
                if (!trc) trc = tabBar.gameObject.AddComponent<TabBarRightClickCatcher>();
                trc.owner = this;
            }

            SafeRebuildAndMaskRefresh(true);
        }

        void OnEnable()
        {
            ChatClient.OnMessage += OnMessage;
            SafeRebuildAndMaskRefresh(true);
        }

        void OnDisable()
        {
            ChatClient.OnMessage -= OnMessage;

            PlayerPrefs.SetInt(KeyTab, _activeTab);
            var rt = (RectTransform)transform;
            PlayerPrefs.SetFloat(KeyPosX, rt.anchoredPosition.x);
            PlayerPrefs.SetFloat(KeyPosY, rt.anchoredPosition.y);
            PlayerPrefs.SetFloat(KeyW, rt.sizeDelta.x);
            PlayerPrefs.SetFloat(KeyH, rt.sizeDelta.y);
            RestoreMouseAfterUI(); // safety, in case the object is disabled while focused
            CloseAnyMenu();
        }

        void Update()
        {
            if (!inputField) return;

            if ((Input.GetKeyDown(focusKey) || Input.GetKeyDown(KeypadEnter())) && !_suppressEnterRefocus)
            {
                if (!inputField.isFocused) { FocusInput(); return; }
            }

            if (Input.GetKeyDown(quickSlashKey) && !inputField.isFocused)
            {
                FocusInput();
                inputField.text = "/";
                inputField.caretPosition = inputField.text.Length;
            }

            if (Input.GetKeyDown(cancelKey) && inputField.isFocused)
                UnfocusInput();
        }

        static KeyCode KeypadEnter() => (KeyCode)271; // KeyCode.KeypadEnter

        // ---------- UI building ----------
        void BuildTabs()
        {
            if (!tabBar || !tabButtonPrefab) return;
            foreach (Transform c in tabBar) Destroy(c.gameObject);
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var tog = Instantiate(tabButtonPrefab, tabBar);
                tog.isOn = (i == 0);
                var label = tog.GetComponentInChildren<TMP_Text>();
                if (label) label.text = t.label;
                int idx = i;
                tog.onValueChanged.AddListener(isOn => { if (isOn) SelectTab(idx); });

                // right-click catcher per tab (rename + channels)
                var rc = tog.gameObject.GetComponent<TabRightClickCatcher>();
                if (!rc) rc = tog.gameObject.AddComponent<TabRightClickCatcher>();
                rc.owner = this;
                rc.tabIndex = idx;
            }
        }

        void BuildChannelDropdown()
        {
            if (!channelDropdown) return;
            channelDropdown.ClearOptions();
            channelDropdown.AddOptions(new List<string>{
                "General","Say","Party","Guild","Trade","Whisper","Global"
            });
            channelDropdown.value = 0;
        }

        void SelectTab(int idx)
        {
            _activeTab = Mathf.Clamp(idx, 0, tabs.Count - 1);
            ApplyFilterToggles(tabs[_activeTab].mask);
            RefreshAllVisible();
        }

        void ApplyFilterToggles(ChatChannel mask)
        {
            void Set(Toggle t, ChatChannel c) { if (t) t.isOn = (mask & c) != 0; }
            Set(systemT, ChatChannel.System);
            Set(generalT, ChatChannel.General);
            Set(sayT, ChatChannel.Say);
            Set(whisperT, ChatChannel.Whisper);
            Set(partyT, ChatChannel.Party);
            Set(guildT, ChatChannel.Guild);
            Set(tradeT, ChatChannel.Trade);
            Set(lootT, ChatChannel.Loot);
            Set(combatT, ChatChannel.Combat);
            Set(globalT, ChatChannel.Global);
        }

        ChatChannel CurrentMaskFromToggles()
        {
            ChatChannel m = 0;
            void AddIf(Toggle t, ChatChannel c) { if (t && t.isOn) m |= c; }
            AddIf(systemT, ChatChannel.System);
            AddIf(generalT, ChatChannel.General);
            AddIf(sayT, ChatChannel.Say);
            AddIf(whisperT, ChatChannel.Whisper);
            AddIf(partyT, ChatChannel.Party);
            AddIf(guildT, ChatChannel.Guild);
            AddIf(tradeT, ChatChannel.Trade);
            AddIf(lootT, ChatChannel.Loot);
            AddIf(combatT, ChatChannel.Combat);
            AddIf(globalT, ChatChannel.Global);
            return m;
        }

        // ---------- Messaging ----------
        void OnMessage(ChatMessage m)
        {
            ChatChannel visibleMask = (filtersPanel != null) ? CurrentMaskFromToggles() : tabs[_activeTab].mask;
            if ((visibleMask & m.channel) == 0) return;

            AddLine(Format(m));
        }

        string Format(ChatMessage m)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(m.unixTimeMs).ToLocalTime().DateTime;
            string ts = timestamp24h ? t.ToString("HH:mm") : t.ToString("h:mm tt");

            string chanHex = ChanHex(m.channel);
            string chan = m.channel.ToString();

            var sb = new StringBuilder(256);

            if (showTimestamps)
                sb.Append("<color=#6B7685>[</color>").Append(ts).Append("<color=#6B7685>]</color> ");

            sb.Append("<color=").Append(chanHex).Append(">").Append("[").Append(chan).Append("]</color> ");
            if (!string.IsNullOrEmpty(m.from))
                sb.Append("<b>").Append(m.from).Append(":</b> ");
            sb.Append(m.text);
            return sb.ToString();
        }

        static string ChanHex(ChatChannel c) => c switch
        {
            ChatChannel.System => "#F3C969",
            ChatChannel.General => "#FFFFFF",
            ChatChannel.Say => "#A8D5FF",
            ChatChannel.Whisper => "#C784FF",
            ChatChannel.Party => "#63B3FF",
            ChatChannel.Guild => "#4FE0C6",
            ChatChannel.Trade => "#E6B95C",
            ChatChannel.Loot => "#7EE784",
            ChatChannel.Combat => "#FF7A7A",
            ChatChannel.Global => "#A8B2FF",
            _ => "#FFFFFF"
        };

        void AddLine(string richText)
        {
            if (!linePrefab || !content) return;

            var line = Instantiate(linePrefab, content);
            line.Set(richText);

            // NEWEST at index 0 (which is visually at the BOTTOM with this rig)
            line.transform.SetSiblingIndex(0);
            _lines.Insert(0, line); // newest-first list

            // prune OLDEST (end of list)
            while (_lines.Count > maxLines)
            {
                int last = _lines.Count - 1;
                if (_lines[last]) Destroy(_lines[last].gameObject);
                _lines.RemoveAt(last);
            }

            SafeRebuildAndMaskRefresh(false);

            if (autoScroll && scrollRect)
                scrollRect.verticalNormalizedPosition = BottomNormalizedForCurrentRig();
        }

        void RefreshAllVisible()
        {
            foreach (var ln in _lines) if (ln) Destroy(ln.gameObject);
            _lines.Clear();

            SafeRebuildAndMaskRefresh(true);
            if (autoScroll && scrollRect)
                scrollRect.verticalNormalizedPosition = BottomNormalizedForCurrentRig();
        }

        // ---------- Input send ----------
        public void SendFromInput()
        {
            if (!inputField) return;
            string text = inputField.text;
            if (string.IsNullOrWhiteSpace(text)) return;

            var channel = DropdownToChannel(channelDropdown ? channelDropdown.value : 0);

            if (text.StartsWith("/w ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("/whisper ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    ChatClient.Send(ChatChannel.Whisper, parts[2], parts[1]);
                    AfterSend();
                    return;
                }
            }

            ChatClient.Send(channel, text);
            AfterSend();
        }

        ChatChannel DropdownToChannel(int idx) => idx switch
        {
            0 => ChatChannel.General,
            1 => ChatChannel.Say,
            2 => ChatChannel.Party,
            3 => ChatChannel.Guild,
            4 => ChatChannel.Trade,
            5 => ChatChannel.Whisper,
            6 => ChatChannel.Global,
            _ => ChatChannel.General
        };

        void AfterSend()
        {
            inputField.text = "";

            if (keepFocusAfterSend) FocusInput();
            else
            {
                _suppressEnterRefocus = true;
                UnfocusInput();
                StartCoroutine(ClearEnterSuppressNextFrame());
            }
        }

        System.Collections.IEnumerator ClearEnterSuppressNextFrame()
        {
            yield return null;
            _suppressEnterRefocus = false;
        }

        // ---------- Focus helpers ----------
        void FocusInput()
        {
            if (!inputField) return;
            var es = EventSystem.current;
            if (!es) return;

            es.SetSelectedGameObject(null);
            StartCoroutine(FocusNextFrame());

            // UNLOCK + SHOW cursor while chat is focused
            CaptureMouseForUI();
        }

        System.Collections.IEnumerator FocusNextFrame()
        {
            yield return null;
            if (!inputField) yield break;
            inputField.interactable = true;
            inputField.ActivateInputField();
            inputField.Select();
            inputField.caretPosition = inputField.text?.Length ?? 0;
            inputField.MoveTextEnd(false);
        }

        void UnfocusInput()
        {
            if (!inputField) return;
            inputField.DeactivateInputField();

            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject == inputField.gameObject)
                es.SetSelectedGameObject(null);

            // RELock / restore previous cursor state when leaving chat
            RestoreMouseAfterUI();
        }

        // ---------- Rig so "index 0" renders at the bottom ----------
        void EnsureIndex0IsBottomRig()
        {
            if (!scrollRect || !content) return;

            // Viewport: must have Image + RectMask2D and stretch to area
            var vp = scrollRect.viewport ? scrollRect.viewport : scrollRect.transform as RectTransform;
            if (vp)
            {
                var img = vp.GetComponent<Image>() ?? vp.gameObject.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
                if (!vp.GetComponent<RectMask2D>()) vp.gameObject.AddComponent<RectMask2D>();
                vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
                vp.pivot = new Vector2(0.5f, 0.5f);
                vp.anchoredPosition = Vector2.zero;
                vp.offsetMin = vp.offsetMax = Vector2.zero;
            }

#if UNITY_2019_3_OR_NEWER
            // NEWER UNITY: Top-stretch + reverseArrangement = true
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 0f);
            content.anchoredPosition = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.LowerLeft;          // bottom-left
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.reverseArrangement = true;                      // <-- makes index 0 render at the bottom

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
#else
            // OLDER UNITY: Bottom-stretch + normal order (LowerLeft)
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 0f);
            content.pivot     = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            //vlg.childAlignment = TextAnchor.LowerLeft;
            vlg.childControlWidth = true;  vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
#endif

            // ScrollRect basics
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // Remove any spacer left from previous experiments
            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name == "__SpacerTop")
                    DestroyImmediate(content.GetChild(i).gameObject);

            // Start pinned to bottom for this rig
            if (autoScroll)
                scrollRect.verticalNormalizedPosition = BottomNormalizedForCurrentRig();
        }

        float BottomNormalizedForCurrentRig()
        {
            // For top-pivot content (new Unity path), bottom = 0
            // For bottom-pivot content (old Unity path), bottom = 1
            return Mathf.Approximately(content ? content.pivot.y : 1f, 1f) ? 0f : 1f;
        }

        void SafeRebuildAndMaskRefresh(bool snapBottom)
        {
            if (!content) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            // Optional: refresh mask if you added ChatScrollMaskRefresher to the Scroll View
            if (scrollRect)
            {
                var refresher = scrollRect.GetComponent<ChatScrollMaskRefresher>();
                if (refresher) refresher.ForceClipRefresh(snapBottom);
            }
        }

        // Mouse control helpers
        void CaptureMouseForUI()
        {
            if (_cursorManagedThisFocus || !manageCursorWhenFocused) return;

            // cache current state
            _prevLockMode = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;

            // unlock + show cursor so user can click tabs, toggles, etc.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorManagedThisFocus = true;

            // optionally disable gameplay components (look, movement, etc.)
            if (disableWhileFocused != null)
                foreach (var b in disableWhileFocused) if (b) b.enabled = false;

            OnChatFocusChanged?.Invoke(true);
        }

        void RestoreMouseAfterUI()
        {
            if (!_cursorManagedThisFocus || !manageCursorWhenFocused) return;

            // restore previous cursor state
            Cursor.lockState = _prevLockMode;
            Cursor.visible = _prevCursorVisible;
            _cursorManagedThisFocus = false;

            // re-enable gameplay components
            if (disableWhileFocused != null)
                foreach (var b in disableWhileFocused) if (b) b.enabled = true;

            OnChatFocusChanged?.Invoke(false);
        }

        public void SetShowTimestamps(bool on) { showTimestamps = on; }
        // optional UI hooks
        public void ToggleFiltersPanel() { if (filtersPanel) filtersPanel.SetActive(!filtersPanel.activeSelf); }
        public void OnFilterChanged() { /* filtering only affects new lines in this MVP */ }

        // =========================
        // Context menus & tab editing
        // =========================

        // Right-click on TAB BAR background → add new tab
        public void ShowTabBarMenu(Vector2 screenPos)
        {
            CloseAnyMenu();
            var ov = GetOverlayRoot();
            if (!ov) return;

            var backdrop = CreateBackdrop(ov);
            var menu = CreateMenuPanel(backdrop, screenPos);

            CreateMenuButton(menu, "Add New Tab", () =>
            {
                tabs.Add(new Tab { label = "New Tab", mask = ChatChannel.All });
                BuildTabs();
                SelectTab(tabs.Count - 1);
                CloseAnyMenu();
            });

            _openMenuGO = backdrop.gameObject;
        }

        // Right-click a TAB → rename + choose channels
        public void ShowTabMenu(int tabIndex, Vector2 screenPos)
        {
            CloseAnyMenu();
            var ov = GetOverlayRoot();
            if (!ov) return;

            var backdrop = CreateBackdrop(ov);
            var menu = CreateMenuPanel(backdrop, screenPos);

            // --- Rename section ---
            CreateMenuLabel(menu, "Rename");
            CreateRenameRow(menu, tabs[tabIndex].label, newName =>
            {
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    tabs[tabIndex].label = newName.Trim();
                    BuildTabs();
                    SelectTab(tabIndex);
                }
            });

            // --- Channels section ---
            CreateMenuLabel(menu, "Channels");
            var current = tabs[tabIndex].mask;

            foreach (ChatChannel c in Enum.GetValues(typeof(ChatChannel)))
            {
                int v = (int)c;
                if (v == 0 || (v & (v - 1)) != 0) continue; // only single-bit flags

                bool on = (current & c) != 0;
                CreateCheckButton(menu, c.ToString(), on, toggled =>
                {
                    var m = tabs[tabIndex].mask;
                    if (toggled) m |= c; else m &= ~c;
                    if ((int)m == 0) m |= c; // keep at least one channel
                    tabs[tabIndex].mask = m;
                    if (tabIndex == _activeTab) SelectTab(_activeTab);
                });
            }

            _openMenuGO = backdrop.gameObject;
        }

        // ---------- Small UI helpers for menus ----------
        RectTransform GetOverlayRoot()
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas ? (RectTransform)canvas.transform : null;
        }

        RectTransform CreateBackdrop(RectTransform parent)
        {
            var go = new GameObject("MenuBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>(); img.color = new Color(0, 0, 0, 0);
            go.GetComponent<Button>().onClick.AddListener(CloseAnyMenu);
            return rt;
        }

        RectTransform CreateMenuPanel(RectTransform parent, Vector2 screenPos)
        {
            var go = new GameObject("MenuPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            var img = go.GetComponent<Image>(); img.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

            var vg = go.GetComponent<VerticalLayoutGroup>();
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            vg.padding = new RectOffset(8, 8, 8, 8); vg.spacing = 6;

            var fit = go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // position at mouse
            var ov = parent as RectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(ov, screenPos, ov.GetComponentInParent<Canvas>()?.worldCamera, out var local);
            rt.pivot = new Vector2(0, 1);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = local;

            return rt;
        }

        void CreateMenuLabel(RectTransform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 14; tmp.color = new Color(1, 1, 1, 0.85f);
        }

        void CreateMenuButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);

            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, 0.06f);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = img.color;
            colors.highlightedColor = new Color(1, 1, 1, 0.12f);
            colors.pressedColor = new Color(1, 1, 1, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;

            btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(CloseAnyMenu);

            // text
            var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)tgo.transform; trt.SetParent(rt, false);
            var tmp = tgo.GetComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 14; tmp.color = Color.white; tmp.enableWordWrapping = false; tmp.raycastTarget = false;
        }

        void CreateCheckButton(RectTransform parent, string label, bool initial, Action<bool> onToggle)
        {
            string Caption(bool on) => (on ? "✓ " : "⃞ ") + label;

            bool state = initial;
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);

            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, 0.06f);

            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = img.color;
            colors.highlightedColor = new Color(1, 1, 1, 0.12f);
            colors.pressedColor = new Color(1, 1, 1, 0.18f);
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;

            var tgo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)tgo.transform; trt.SetParent(rt, false);
            var tmp = tgo.GetComponent<TextMeshProUGUI>();
            tmp.text = Caption(state); tmp.fontSize = 14; tmp.color = Color.white; tmp.enableWordWrapping = false; tmp.raycastTarget = false;

            btn.onClick.AddListener(() =>
            {
                state = !state;
                tmp.text = Caption(state);
                onToggle?.Invoke(state);
            });
        }

        void CreateRenameRow(RectTransform parent, string currentName, Action<string> apply)
        {
            // row container
            var row = new GameObject("RenameRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)row.transform; rt.SetParent(parent, false);

            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.spacing = 6; hl.padding = new RectOffset(0, 0, 0, 0);

            // input background
            var inputGO = new GameObject("NameInput", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)inputGO.transform; irt.SetParent(rt, false);
            var bg = inputGO.GetComponent<Image>(); bg.color = new Color(1, 1, 1, 0.06f);

            // TMP_InputField structure
            var field = inputGO.AddComponent<TMP_InputField>();
            field.characterLimit = 32;
            field.text = currentName;

            // viewport
            var vp = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var vprt = (RectTransform)vp.transform; vprt.SetParent(irt, false);
            vprt.anchorMin = new Vector2(0, 0); vprt.anchorMax = new Vector2(1, 1);
            vprt.offsetMin = new Vector2(6, 4); vprt.offsetMax = new Vector2(-6, -4);

            // text
            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGO.transform; trt.SetParent(vprt, false);
            var txt = textGO.GetComponent<TextMeshProUGUI>();
            txt.enableWordWrapping = false; txt.alignment = TextAlignmentOptions.MidlineLeft; txt.fontSize = 14;
            field.textViewport = vprt;
            field.textComponent = txt;
            field.caretWidth = 2;

            // placeholder
            var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var prt = (RectTransform)phGO.transform; prt.SetParent(vprt, false);
            var ph = phGO.GetComponent<TextMeshProUGUI>();
            ph.text = "Tab name"; ph.fontSize = 14; ph.color = new Color(1, 1, 1, 0.35f);
            field.placeholder = ph;

            // OK button
            CreateMenuButton(rt, "Rename", () =>
            {
                var newName = string.IsNullOrWhiteSpace(field.text) ? currentName : field.text.Trim();
                apply?.Invoke(newName);
                CloseAnyMenu();
            });

            // commit on Enter too
            field.onSubmit.AddListener(s =>
            {
                var newName = string.IsNullOrWhiteSpace(s) ? currentName : s.Trim();
                apply?.Invoke(newName);
                CloseAnyMenu();
            });
        }

        void CloseAnyMenu()
        {
            if (_openMenuGO)
            {
                Destroy(_openMenuGO);
                _openMenuGO = null;
            }
        }

    }
}
