// Assets/MMO/Chat/UI/ChatWindow.cs
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MMO.Chat;        // ChatChannel, ChatMessage
using MMO.Common.UI;  // UIContextMenu

namespace MMO.Chat.UI
{
    public class ChatWindow : MonoBehaviour
    {
        [Header("Mouse / Focus")]
        [SerializeField] bool manageCursorWhenFocused = true;
        [SerializeField] Behaviour[] disableWhileFocused;

        [Header("Wiring")]
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] RectTransform content;
        [SerializeField] ChatLine linePrefab;

        [Header("Input bar")]
        [SerializeField] TMP_InputField inputField;
        [SerializeField] TMP_Dropdown channelDropdown;
        [SerializeField] Button sendButton;

        [Header("Tabs (Scrollable)")]
        [Tooltip("ScrollRect that contains the tab bar (horizontal).")]
        [SerializeField] ScrollRect tabScrollRect;
        [Tooltip("Viewport RectTransform inside the tab ScrollRect.")]
        [SerializeField] RectTransform tabViewport;
        [Tooltip("Content RectTransform inside the tab ScrollRect; tabs will be parented here.")]
        [SerializeField] RectTransform tabContent;
        [SerializeField] Toggle tabButtonPrefab;

        [Header("Filters panel (optional)")]
        [SerializeField] GameObject filtersPanel;
        [SerializeField] Toggle systemT, generalT, sayT, whisperT, partyT, guildT, tradeT, lootT, combatT, globalT;

        [Header("Behavior")]
        [SerializeField] int maxLines = 300;
        [SerializeField] bool autoScroll = true;
        [SerializeField] bool timestamp24h = true;

        [Header("Hotkeys")]
        [SerializeField] KeyCode focusKey = KeyCode.Return;
        [SerializeField] KeyCode cancelKey = KeyCode.Escape;
        [SerializeField] KeyCode quickSlashKey = KeyCode.Slash;

        [Header("Send settings")]
        [SerializeField] bool keepFocusAfterSend = false;
        bool _suppressEnterRefocus;
        [SerializeField] bool showTimestamps = false;

        [Header("Context Menu")]
        [SerializeField] RectTransform menuRootOverride; // assign your MainHudCanvas root if you want

        [Header("Tab Scrolling")]
        [Tooltip("Mouse wheel to horizontal scroll speed while pointer is over the tab bar.")]
        [Range(0.01f, 1f)]
        [SerializeField] float tabScrollWheelSpeed = 0.15f;

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

        public static event Action<bool> OnChatFocusChanged;

        // inline rename popup tracker
        GameObject _openPopupGO;

        void Awake()
        {
            EnsureIndex0IsBottomRig();
            EnsureTabBarScrollRig();

            BuildTabs();                   // builds toggle buttons under tabContent and right-click hooks
            EnsureTabBarHitArea();         // flexible spacer for empty-right-click
            AddViewportRightClickCatcher();// right-click anywhere in the visible bar
            BuildChannelDropdown();        // ← restored

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

            RestoreMouseAfterUI();
            CloseInlinePopup();
        }

        void Update()
        {
            // hotkeys for chat input focus
            if (inputField)
            {
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

            // mouse wheel → horizontal scroll when hovering the tab bar
            if (tabScrollRect && tabViewport)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(tabViewport, Input.mousePosition))
                {
                    float dy = Input.mouseScrollDelta.y; // up/down wheel
                    if (Mathf.Abs(dy) > 0.001f)
                    {
                        float t = tabScrollRect.horizontalNormalizedPosition;
                        t = Mathf.Clamp01(t - dy * tabScrollWheelSpeed); // wheel up scrolls left
                        tabScrollRect.horizontalNormalizedPosition = t;
                    }
                }
            }
        }

        static KeyCode KeypadEnter() => (KeyCode)271; // KeyCode.KeypadEnter

        // ---------- Tabs (Scrollable bar) ----------
        void EnsureTabBarScrollRig()
        {
            if (!tabScrollRect || !tabViewport || !tabContent) return;

            // ScrollRect config
            tabScrollRect.horizontal = true;
            tabScrollRect.vertical = false;
            tabScrollRect.movementType = ScrollRect.MovementType.Clamped;
            tabScrollRect.viewport = tabViewport;
            tabScrollRect.content = tabContent;

            // Viewport: needs Image + RectMask2D
            var vpImg = tabViewport.GetComponent<Image>() ?? tabViewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0); vpImg.raycastTarget = true;
            if (!tabViewport.GetComponent<RectMask2D>()) tabViewport.gameObject.AddComponent<RectMask2D>();

            // Content: left anchored, auto-size horizontally
            tabContent.anchorMin = new Vector2(0f, 0.5f);
            tabContent.anchorMax = new Vector2(0f, 0.5f);
            tabContent.pivot = new Vector2(0f, 0.5f);
            tabContent.anchoredPosition = Vector2.zero;

            var hlg = tabContent.GetComponent<HorizontalLayoutGroup>() ?? tabContent.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = false;

            var csf = tabContent.GetComponent<ContentSizeFitter>() ?? tabContent.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; // expands width as tabs are added
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        void BuildTabs()
        {
            if (!tabContent || !tabButtonPrefab) return;

            // Clear all existing (including old spacer)
            for (int i = tabContent.childCount - 1; i >= 0; i--)
                Destroy(tabContent.GetChild(i).gameObject);

            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var tog = Instantiate(tabButtonPrefab, tabContent);
                tog.isOn = (i == _activeTab);
                var label = tog.GetComponentInChildren<TMP_Text>();
                if (label) label.text = t.label;
                int idx = i;
                tog.onValueChanged.AddListener(isOn => { if (isOn) SelectTab(idx); });

                // right-click catcher per tab (rename + channels menu)
                var rc = tog.gameObject.GetComponent<TabRightClickCatcher>() ?? tog.gameObject.AddComponent<TabRightClickCatcher>();
                rc.owner = this;
                rc.tabIndex = idx;
            }

            // spacer & catch empty-right-click
            EnsureTabBarHitArea();

            // Rebuild and keep scroll valid
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tabContent);

            // if active is near end or a new tab added, keep it visible
            ScrollToShowActiveTab();
        }

        void EnsureTabBarHitArea()
        {
            if (!tabContent) return;

            var hit = tabContent.Find("__TabBarHitArea");
            if (!hit)
            {
                var go = new GameObject("__TabBarHitArea",
                    typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(TabBarRightClickCatcher));
                var rt = (RectTransform)go.transform;
                rt.SetParent(tabContent, false);
                rt.SetAsLastSibling();

                var le = go.GetComponent<LayoutElement>();
                le.minWidth = 4f;
                le.flexibleWidth = 10000f; // absorb remaining width

                var img = go.GetComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = true;

                var rc = go.GetComponent<TabBarRightClickCatcher>();
                rc.owner = this;
            }
            else
            {
                hit.SetAsLastSibling();
                var le = hit.GetComponent<LayoutElement>() ?? hit.gameObject.AddComponent<LayoutElement>();
                le.minWidth = 4f; le.flexibleWidth = 10000f;
                var img = hit.GetComponent<Image>() ?? hit.gameObject.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
                var rc = hit.GetComponent<TabBarRightClickCatcher>() ?? hit.gameObject.AddComponent<TabBarRightClickCatcher>();
                rc.owner = this;
            }
        }

        void AddViewportRightClickCatcher()
        {
            if (!tabViewport) return;
            var img = tabViewport.GetComponent<Image>() ?? tabViewport.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
            var rc = tabViewport.GetComponent<TabBarRightClickCatcher>() ?? tabViewport.gameObject.AddComponent<TabBarRightClickCatcher>();
            rc.owner = this;
        }

        void ScrollToShowActiveTab()
        {
            if (!tabScrollRect || !tabContent) return;

            // If active is the last tab (typical after add), scroll to end
            if (_activeTab >= tabs.Count - 1)
            {
                tabScrollRect.horizontalNormalizedPosition = 1f;
                return;
            }

            // Otherwise, try to make sure its button is within viewport bounds
            if (_activeTab >= 0 && _activeTab < tabContent.childCount)
            {
                var tr = tabContent.GetChild(_activeTab) as RectTransform;
                if (tr)
                {
                    // compute positions in content space
                    float viewLeft = tabScrollRect.content.anchoredPosition.x; // negative when scrolled
                    float contentX = tr.anchoredPosition.x;
                    float itemLeft = contentX;
                    float itemRight = contentX + tr.rect.width;

                    float viewWidth = tabViewport.rect.width;
                    float viewRight = -viewLeft + viewWidth; // since anchoredPosition.x is negative for right scroll

                    // If item beyond right edge, scroll right; if before left, scroll left
                    if (itemRight > viewRight)
                    {
                        float delta = itemRight - viewRight + 10f; // small padding
                        tabScrollRect.content.anchoredPosition += new Vector2(-delta, 0f);
                    }
                    else if (itemLeft < -viewLeft)
                    {
                        float delta = (-viewLeft) - itemLeft + 10f;
                        tabScrollRect.content.anchoredPosition += new Vector2(delta, 0f);
                    }
                }
            }
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

        // ---------- Add/remove chat lines ----------
        void AddLine(string richText)
        {
            if (!linePrefab || !content) return;

            var line = Instantiate(linePrefab, content);
            line.Set(richText);

            line.transform.SetSiblingIndex(0);
            _lines.Insert(0, line);

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

            RestoreMouseAfterUI();
        }

        // ---------- Chat list rig (new lines at bottom) ----------
        void EnsureIndex0IsBottomRig()
        {
            if (!scrollRect || !content) return;

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
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 0f);
            content.anchoredPosition = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.LowerLeft;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.reverseArrangement = true;

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
#else
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 0f);
            content.pivot     = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;  vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
#endif

            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name == "__SpacerTop")
                    DestroyImmediate(content.GetChild(i).gameObject);

            if (autoScroll)
                scrollRect.verticalNormalizedPosition = BottomNormalizedForCurrentRig();
        }

        float BottomNormalizedForCurrentRig()
        {
            return Mathf.Approximately(content ? content.pivot.y : 1f, 1f) ? 0f : 1f;
        }

        void SafeRebuildAndMaskRefresh(bool snapBottom)
        {
            if (!content) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

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

            _prevLockMode = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorManagedThisFocus = true;

            if (disableWhileFocused != null)
                foreach (var b in disableWhileFocused) if (b) b.enabled = false;

            OnChatFocusChanged?.Invoke(true);
        }

        void RestoreMouseAfterUI()
        {
            if (!_cursorManagedThisFocus || !manageCursorWhenFocused) return;

            Cursor.lockState = _prevLockMode;
            Cursor.visible = _prevCursorVisible;
            _cursorManagedThisFocus = false;

            if (disableWhileFocused != null)
                foreach (var b in disableWhileFocused) if (b) b.enabled = true;

            OnChatFocusChanged?.Invoke(false);
        }

        public void SetShowTimestamps(bool on) { showTimestamps = on; }
        public void ToggleFiltersPanel() { if (filtersPanel) filtersPanel.SetActive(!filtersPanel.activeSelf); }
        public void OnFilterChanged() { /* filtering only affects new lines in this MVP */ }

        // =========================
        // Context menus & tab editing
        // =========================

        // Right-click on TAB BAR background → add new tab (+ Cancel)
        public void ShowTabBarMenu(Vector2 screenPos)
        {
            var overlay = GetOverlayRoot(); if (!overlay) return;
            var canvas = overlay.GetComponentInParent<Canvas>();
            var cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            UIContextMenu.Show(overlay, cam, screenPos, menu =>
            {
                menu.AddButton("Add New Tab", () =>
                {
                    tabs.Add(new Tab { label = "New Tab", mask = ChatChannel.All });
                    BuildTabs();
                    SelectTab(tabs.Count - 1);
                    ScrollToShowActiveTab();
                });

                menu.AddCancelButton();
            });
        }

        // Right-click a TAB → rename + choose channels (+ Cancel)
        public void ShowTabMenu(int tabIndex, Vector2 screenPos)
        {
            var overlay = GetOverlayRoot(); if (!overlay) return;
            var canvas = overlay.GetComponentInParent<Canvas>();
            var cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            int idx = tabIndex;
            Vector2 sp = screenPos;

            UIContextMenu.Show(overlay, cam, sp, menu =>
            {
                menu.AddButton("Rename…", () =>
                {
                    ShowInlineRenamePopup(idx, sp);
                });

                menu.AddSeparator(1f);

                foreach (ChatChannel c in Enum.GetValues(typeof(ChatChannel)))
                {
                    int v = (int)c;
                    if (v == 0 || (v & (v - 1)) != 0) continue;

                    bool on = (tabs[idx].mask & c) != 0;
                    menu.AddToggle(c.ToString(), on, toggled =>
                    {
                        var m = tabs[idx].mask;
                        if (toggled) m |= c; else m &= ~c;
                        if ((int)m == 0) m |= c; // keep at least one channel
                        tabs[idx].mask = m;
                        if (idx == _activeTab) SelectTab(_activeTab);
                    });
                }

                menu.AddCancelButton();
            });
        }

        // ---------- Inline rename popup (simple, no extra prefab needed) ----------
        void ShowInlineRenamePopup(int tabIndex, Vector2 screenPos)
        {
            CloseInlinePopup();

            var overlay = GetOverlayRoot(); if (!overlay) return;
            var canvas = overlay.GetComponentInParent<Canvas>();
            var cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            // Backdrop
            var backdropGO = new GameObject("RenameBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            var backdrop = (RectTransform)backdropGO.transform;
            backdrop.SetParent(overlay, false);
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
            var bdImg = backdropGO.GetComponent<Image>(); bdImg.color = new Color(0, 0, 0, 0);
            backdropGO.GetComponent<Button>().onClick.AddListener(CloseInlinePopup);

            // Panel
            var panelGO = new GameObject("RenamePanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var panel = (RectTransform)panelGO.transform; panel.SetParent(backdrop, false);
            var pImg = panelGO.GetComponent<Image>(); pImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8); vlg.spacing = 6;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = panelGO.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Position near cursor
            panel.pivot = new Vector2(0, 1);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screenPos, cam, out var local);
            panel.anchoredPosition = local;

            // Input row
            var inputBG = new GameObject("NameInputBG", typeof(RectTransform), typeof(Image));
            var inputBGRT = (RectTransform)inputBG.transform; inputBGRT.SetParent(panel, false);
            var ibg = inputBG.GetComponent<Image>(); ibg.color = new Color(1, 1, 1, 0.06f);

            var input = inputBG.AddComponent<TMP_InputField>();
            input.characterLimit = 32;
            input.text = tabs[tabIndex].label;

            // viewport
            var vp = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var vprt = (RectTransform)vp.transform; vprt.SetParent(inputBGRT, false);
            vprt.anchorMin = new Vector2(0, 0); vprt.anchorMax = new Vector2(1, 1);
            vprt.offsetMin = new Vector2(6, 4); vprt.offsetMax = new Vector2(-6, -4);

            // text
            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGO.transform; trt.SetParent(vprt, false);
            var txt = textGO.GetComponent<TextMeshProUGUI>();
            txt.enableWordWrapping = false; txt.alignment = TextAlignmentOptions.MidlineLeft; txt.fontSize = 14;
            input.textViewport = vprt;
            input.textComponent = txt;
            input.caretWidth = 2;

            // placeholder
            var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var prt = (RectTransform)phGO.transform; prt.SetParent(vprt, false);
            var ph = phGO.GetComponent<TextMeshProUGUI>();
            ph.text = "Tab name"; ph.fontSize = 14; ph.color = new Color(1, 1, 1, 0.35f);
            input.placeholder = ph;

            // Buttons row
            var btnRowGO = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var btnRow = (RectTransform)btnRowGO.transform; btnRow.SetParent(panel, false);
            var hl = btnRowGO.GetComponent<HorizontalLayoutGroup>(); hl.spacing = 6; hl.childForceExpandWidth = false;

            void MakeBtn(string name, Action onClick)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                var rt = (RectTransform)go.transform; rt.SetParent(btnRow, false);
                var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, 0.06f);
                var b = go.GetComponent<Button>();
                var colors = b.colors; colors.highlightedColor = new Color(1, 1, 1, 0.12f); colors.pressedColor = new Color(1, 1, 1, 0.18f);
                b.colors = colors;
                b.onClick.AddListener(() => onClick?.Invoke());

                var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
                var l = lgo.GetComponent<TextMeshProUGUI>(); l.text = name; l.fontSize = 14; l.color = Color.white; l.raycastTarget = false;
            }

            void Commit(string val)
            {
                var newName = string.IsNullOrWhiteSpace(val) ? tabs[tabIndex].label : val.Trim();
                tabs[tabIndex].label = newName;
                BuildTabs();
                SelectTab(tabIndex);
                ScrollToShowActiveTab();
                CloseInlinePopup();
            }

            MakeBtn("Rename", () => Commit(input.text));
            MakeBtn("Cancel", CloseInlinePopup);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            ClampRectInsideParent(panel, overlay, 8f);

            _openPopupGO = backdropGO;
        }

        void CloseInlinePopup()
        {
            if (_openPopupGO)
            {
                Destroy(_openPopupGO);
                _openPopupGO = null;
            }
        }

        RectTransform GetOverlayRoot()
        {
            if (menuRootOverride) return menuRootOverride;
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return null;
            var root = canvas.rootCanvas;
            return root ? (RectTransform)root.transform : (RectTransform)canvas.transform;
        }

        void ClampRectInsideParent(RectTransform child, RectTransform parent, float padding)
        {
            var p = parent.rect;
            var size = child.rect.size;
            var pos = child.anchoredPosition;
            var pivot = child.pivot;

            float minX = p.xMin + padding + pivot.x * size.x;
            float maxX = p.xMax - padding - (1f - pivot.x) * size.x;
            float minY = p.yMin + padding + pivot.y * size.y;
            float maxY = p.yMax - padding - (1f - pivot.y) * size.y;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            child.anchoredPosition = pos;
        }

        // ---------- Filters / channels ----------
        void BuildChannelDropdown()
        {
            if (!channelDropdown) return;
            channelDropdown.ClearOptions();
            channelDropdown.AddOptions(new List<string>{
                "General","Say","Party","Guild","Trade","Whisper","Global"
            });
            channelDropdown.value = 0;
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

        void SelectTab(int idx)
        {
            _activeTab = Mathf.Clamp(idx, 0, tabs.Count - 1);
            ApplyFilterToggles(tabs[_activeTab].mask);
            RefreshAllVisible();
        }
    }
}
