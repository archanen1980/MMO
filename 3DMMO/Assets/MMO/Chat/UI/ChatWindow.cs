using System;
using System.Collections.Generic;
using System.Reflection;              // reflection helpers (right-click catcher wiring)
using System.Text;
using System.Text.RegularExpressions; // regex for item link recolor
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MMO.Chat;        // ChatChannel, ChatMessage
using MMO.Chat.UI;     // ChatLine
using MMO.Common.UI;   // UIContextMenu
using MMO.Inventory.UI; // ItemTooltipComposer

namespace MMO.Chat.UI
{
    /// <summary>
    /// Lightweight, readable chat window with tabs, context menus, rename/filter popups,
    /// bracket hotkeys for tab switching, and minimal allocations.
    /// </summary>
    public class ChatWindow : MonoBehaviour
    {
        // ----------------- Inspector Wiring -----------------

        [Header("Chat List")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private ChatLine linePrefab;

        [Header("Input Bar")]
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private TMP_Dropdown channelDropdown;
        [SerializeField] private Button sendButton;

        [Header("Tabs UI")]
        [Tooltip("Container that holds the tab Toggle objects (e.g., Header/TabScroll/Viewport/TabBarContent)")]
        [SerializeField] private Transform tabBar;
        [Tooltip("Toggle prefab for each tab (must have a raycastable Graphic)")]
        [SerializeField] private Toggle tabButtonPrefab;
        [SerializeField] private ToggleGroup tabToggleGroup;

        [Header("Optional Legacy Filters Panel")]
        [SerializeField] private GameObject legacyFiltersPanel;
        [SerializeField] private Toggle systemT, generalT, sayT, whisperT, partyT, guildT, tradeT, lootT, combatT, globalT;

        [Header("Behavior")]
        [SerializeField] private int maxVisibleLines = 300;
        [SerializeField] private int maxHistoryLines = 1000;
        [SerializeField] private bool autoScrollToBottom = true;
        [SerializeField] private bool use24HourTimestamps = true;
        [SerializeField] private bool showTimestamps = false;

        [Header("Hotkeys")]
        [SerializeField] private KeyCode focusKey = KeyCode.Return;
        [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
        [SerializeField] private KeyCode quickSlashKey = KeyCode.Slash;
        [SerializeField] private KeyCode previousTabKey = KeyCode.LeftBracket;  // '['
        [SerializeField] private KeyCode nextTabKey = KeyCode.RightBracket; // ']'

        [Header("Send Settings")]
        [SerializeField] private bool keepFocusAfterSend = false;

        [Header("Cursor / Gameplay Integration")]
        [SerializeField] private bool manageCursorWhenChatFocused = true;
        [Tooltip("Optional: components to disable while typing (e.g., FPS look/move scripts).")]
        [SerializeField] private Behaviour[] disableWhileChatFocused;

        [Header("Popup Layer")]
        [Tooltip("Optional override for where popups/menus appear. If empty, a top canvas is auto-created.")]
        [SerializeField] private RectTransform popupRootOverride;
        [SerializeField] private int popupSortingOrder = 5000;

        [Header("ItemDef Lookup (for coloring item links)")]
        [Tooltip("Optional object with TryGetById(string, out ItemDef) or GetByIdOrNull(string). Leave null to use Resources only.")]
        [SerializeField] private UnityEngine.Object optionalItemLookup;
        [Tooltip("Folder under Resources/ that contains ItemDef assets.")]
        [SerializeField] private string resourcesItemsFolder = "Items";

        // ----------------- Tabs Model -----------------

        [Serializable]
        public class Tab
        {
            public string label;
            public ChatChannel channelMask;
        }

        [SerializeField]
        private List<Tab> tabs = new()
        {
            new Tab { label = "All",     channelMask = ChatChannel.All },
            new Tab { label = "General", channelMask = ChatChannel.General | ChatChannel.Say | ChatChannel.Whisper | ChatChannel.Global },
            new Tab { label = "Group",   channelMask = ChatChannel.Party   | ChatChannel.Guild | ChatChannel.Say     | ChatChannel.Whisper },
            new Tab { label = "Loot",    channelMask = ChatChannel.Loot    | ChatChannel.System },
            new Tab { label = "Combat",  channelMask = ChatChannel.Combat  | ChatChannel.System },
        };

        private int activeTabIndex = 0;

        // ----------------- State -----------------

        private readonly List<ChatLine> visibleLines = new();
        private readonly List<ChatMessage> history = new(); // oldest..newest

        private bool suppressEnterRefocus;

        // Chat focus mouse state
        private CursorLockMode cachedLockMode;
        private bool cachedCursorVisible;
        private bool cursorManagedThisFocus;

        // Strong popup cursor guard
        private bool popupCursorForce;
        private Coroutine popupCursorRoutine;
        private CursorLockMode popupPrevLock;
        private bool popupPrevVisible;

        public static event Action<bool> OnChatFocusChanged;

        // PlayerPrefs keys (position/size + active tab)
        private const string PrefKeyTab = "chat.tab";
        private const string PrefKeyPosX = "chat.posx";
        private const string PrefKeyPosY = "chat.posy";
        private const string PrefKeyW = "chat.w";
        private const string PrefKeyH = "chat.h";

        // Regex to find TMP item links: <link="item:ID">LABEL</link>
        private static readonly Regex s_itemLinkRx =
            new Regex("<link\\s*=\\s*\"item:([^\"]+)\">(?<label>.*?)</link>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        // ----------------- Unity Lifecycle -----------------

        private void Awake()
        {
            BuildTabs();
            BuildChannelDropdown();

            if (sendButton) sendButton.onClick.AddListener(SendFromInput);
            if (inputField)
            {
                inputField.onSubmit.AddListener(_ => SendFromInput());
                inputField.onFocusSelectAll = false;
            }

            // Restore persisted state
            activeTabIndex = PlayerPrefs.GetInt(PrefKeyTab, 0);
            SelectTab(activeTabIndex);

            var rt = (RectTransform)transform;
            var pos = rt.anchoredPosition;
            pos.x = PlayerPrefs.GetFloat(PrefKeyPosX, pos.x);
            pos.y = PlayerPrefs.GetFloat(PrefKeyPosY, pos.y);
            rt.anchoredPosition = pos;

            var size = rt.sizeDelta;
            size.x = PlayerPrefs.GetFloat(PrefKeyW, size.x);
            size.y = PlayerPrefs.GetFloat(PrefKeyH, size.y);
            rt.sizeDelta = size;

            ConfigureChatListLayout();
            RefreshLayoutAndMask(true);
            UpdateTabOutlines();

            _ = EnsurePopupLayerExists(); // prepare popup layer once
        }

        private void OnEnable()
        {
            ChatClient.OnMessage += HandleIncomingMessage;
            RefreshLayoutAndMask(true);
        }

        private void OnDisable()
        {
            ChatClient.OnMessage -= HandleIncomingMessage;

            PlayerPrefs.SetInt(PrefKeyTab, activeTabIndex);
            var rt = (RectTransform)transform;
            PlayerPrefs.SetFloat(PrefKeyPosX, rt.anchoredPosition.x);
            PlayerPrefs.SetFloat(PrefKeyPosY, rt.anchoredPosition.y);
            PlayerPrefs.SetFloat(PrefKeyW, rt.sizeDelta.x);
            PlayerPrefs.SetFloat(PrefKeyH, rt.sizeDelta.y);

            RestoreMouseAfterChat();
            EndPopupCursorGuard(); // <— previously referenced as StopPopupCursorGuard
        }

        private void Update()
        {
            if (!inputField) return;

            // Focus chat
            if ((Input.GetKeyDown(focusKey) || Input.GetKeyDown((KeyCode)271)) && !suppressEnterRefocus)
            {
                if (!inputField.isFocused) { FocusChatInput(); return; }
            }

            // Quick slash to type commands
            if (Input.GetKeyDown(quickSlashKey) && !inputField.isFocused)
            {
                FocusChatInput();
                inputField.text = "/";
                inputField.caretPosition = inputField.text.Length;
            }

            // Cancel chat focus
            if (Input.GetKeyDown(cancelKey) && inputField.isFocused)
                UnfocusChatInput();

            // Tab cycling via hotkeys (brackets), but not while a popup is open or typing
            if (!popupCursorForce && inputField && !inputField.isFocused)
            {
                if (Input.GetKeyDown(previousTabKey)) CycleTab(-1);
                if (Input.GetKeyDown(nextTabKey)) CycleTab(+1);
            }
        }

        // ----------------- Tabs -----------------

        private void BuildTabs()
        {
            if (!tabBar || !tabButtonPrefab) return;

            // Ensure a ToggleGroup for single selection
            var group = tabToggleGroup ? tabToggleGroup : tabBar.GetComponent<ToggleGroup>();
            if (!group) group = tabBar.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            tabToggleGroup = group;

            // Clear existing children (editor play reentry)
            for (int i = tabBar.childCount - 1; i >= 0; --i)
                Destroy(tabBar.GetChild(i).gameObject);

            // Create toggles
            for (int i = 0; i < tabs.Count; i++)
            {
                var tabModel = tabs[i];
                var toggle = Instantiate(tabButtonPrefab, tabBar);

                // Label
                var label = toggle.GetComponentInChildren<TMP_Text>();
                if (label) label.text = tabModel.label;

                // Join the group
                toggle.group = group;

                // Ensure raycastable
                var graphic = toggle.targetGraphic as Graphic ?? toggle.GetComponent<Graphic>() ?? toggle.gameObject.AddComponent<Image>();
                graphic.raycastTarget = true;
                toggle.targetGraphic = graphic;

                // Outline for active tab
                var outline = graphic.GetComponent<Outline>() ?? graphic.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, 0.9f);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.enabled = false;

                int index = i;
                toggle.onValueChanged.AddListener(isOn =>
                {
                    if (isOn)
                    {
                        SelectTab(index);
                        UpdateTabOutlines();
                    }
                });

                toggle.SetIsOnWithoutNotify(i == activeTabIndex);

                // Right-click support on the tab itself
                WireRightClickHandlers(toggle, index);
            }

            UpdateTabOutlines();
        }

        private void UpdateTabOutlines()
        {
            if (!tabBar) return;

            for (int i = 0, logical = 0; i < tabBar.childCount; i++)
            {
                var child = tabBar.GetChild(i);
                var toggle = child.GetComponent<Toggle>();
                if (!toggle) continue;

                var targetGO = toggle.targetGraphic ? toggle.targetGraphic.gameObject : toggle.gameObject;
                var outline = targetGO.GetComponent<Outline>() ?? targetGO.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, 0.9f);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.enabled = (logical == activeTabIndex);
                logical++;
            }
        }

        private void SelectTab(int index)
        {
            activeTabIndex = Mathf.Clamp(index, 0, tabs.Count - 1);

            // Update optional legacy filter toggles
            ApplyLegacyFilterToggles(tabs[activeTabIndex].channelMask);

            // Refresh messages for the active tab
            RebuildVisibleFromHistory();

            // Sync UI selection quietly
            int logical = 0;
            for (int i = 0; i < tabBar.childCount; i++)
            {
                var toggle = tabBar.GetChild(i).GetComponent<Toggle>();
                if (!toggle) continue;
                toggle.SetIsOnWithoutNotify(logical == activeTabIndex);
                logical++;
            }

            UpdateTabOutlines();
        }

        private void CycleTab(int direction)
        {
            if (tabs == null || tabs.Count == 0) return;

            int next = activeTabIndex + (direction >= 0 ? 1 : -1);
            if (next < 0) next = tabs.Count - 1;
            else if (next >= tabs.Count) next = 0;

            SelectTab(next);
        }

        /// <summary>
        /// Called by TabLeftClickCatcher to select a tab by its Toggle instance.
        /// </summary>
        public void SelectTabByToggle(Toggle toggle)
        {
            if (!toggle || !tabBar) return;

            // find logical index among direct Toggle children of tabBar
            int idx = -1, logical = 0;
            for (int i = 0; i < tabBar.childCount; i++)
            {
                var childToggle = tabBar.GetChild(i).GetComponent<Toggle>();
                if (!childToggle) continue;
                if (childToggle == toggle) { idx = logical; break; }
                logical++;
            }
            if (idx < 0) return;

            // force UI state quietly
            logical = 0;
            for (int i = 0; i < tabBar.childCount; i++)
            {
                var t = tabBar.GetChild(i).GetComponent<Toggle>();
                if (!t) continue;
                t.SetIsOnWithoutNotify(logical == idx);
                logical++;
            }

            SelectTab(idx);
            UpdateTabOutlines();
        }

        // ----------------- Context Menus (invoked externally) -----------------

        /// <summary>
        /// Called by your right-click catchers. Context should be "TabBar" or "Tab".
        /// </summary>
        public void ShowContextMenu(string context, Vector2 screenPosition, int? tabIndexOrNull)
        {
            var popupLayer = EnsurePopupLayerExists(); if (!popupLayer) return;
            var canvas = popupLayer.GetComponent<Canvas>();
            var camera = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            UIContextMenu.Show(popupLayer, camera, screenPosition, menu =>
            {
                switch (context)
                {
                    case "TabBar":
                        menu.AddButton("New Tab", () =>
                        {
                            tabs.Add(new Tab { label = "New Tab", channelMask = ChatChannel.All });
                            BuildTabs();
                            SelectTab(tabs.Count - 1);
                        });
                        menu.AddSeparator(1f);
                        menu.AddCancelButton();
                        break;

                    case "Tab":
                        if (!tabIndexOrNull.HasValue) { menu.AddCancelButton(); break; }
                        int idx = tabIndexOrNull.Value;

                        menu.AddButton("Filter…", () => ShowFilterPopup(idx, screenPosition));
                        menu.AddButton("Rename…", () => ShowRenamePopup(idx, screenPosition));
                        menu.AddSeparator(1f);
                        menu.AddCancelButton();
                        break;

                    default:
                        menu.AddCancelButton();
                        break;
                }
            });
        }

        // ----------------- Filter Popup (Apply/Cancel) -----------------

        private void ShowFilterPopup(int tabIndex, Vector2 screenPosition)
        {
            var popupLayer = EnsurePopupLayerExists(); if (!popupLayer) return;
            var canvas = popupLayer.GetComponent<Canvas>();
            var camera = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            StartPopupCursorGuard();

            // Backdrop
            var backdropGO = new GameObject("FilterBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            var backdrop = (RectTransform)backdropGO.transform;
            backdrop.SetParent(popupLayer, false);
            backdrop.SetAsLastSibling();
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
            backdropGO.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var backdropButton = backdropGO.GetComponent<Button>();

            // Panel
            var panelGO = new GameObject("FilterPanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter),
                typeof(LayoutElement));
            var panel = (RectTransform)panelGO.transform; panel.SetParent(backdrop, false);
            panelGO.transform.SetAsLastSibling();

            var panelImage = panelGO.GetComponent<Image>();
            panelImage.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

            var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 10;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;

            var fit = panelGO.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var le = panelGO.GetComponent<LayoutElement>();
            le.minWidth = 420f; le.preferredWidth = 480f;

            // Position near cursor
            panel.pivot = new Vector2(0, 1);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, screenPosition, camera, out var local);
            panel.anchoredPosition = local;

            // Title
            var titleGO = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            var title = titleGO.GetComponent<TextMeshProUGUI>();
            ((RectTransform)titleGO.transform).SetParent(panel, false);
            title.text = "Filter Channels";
            title.fontSize = 18;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = Color.white;
            title.raycastTarget = false;

            AddSeparator(panel, 1f, new Color(1, 1, 1, 0.15f));

            // ----------------- NEW: All / None quick toggles row -----------------
            var quickRowGO = new GameObject("QuickTogglesRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var quickRow = (RectTransform)quickRowGO.transform; quickRow.SetParent(panel, false);
            var qhl = quickRowGO.GetComponent<HorizontalLayoutGroup>();
            qhl.childAlignment = TextAnchor.MiddleLeft;
            qhl.spacing = 10;
            qhl.childControlWidth = false; qhl.childForceExpandWidth = false;
            qhl.childControlHeight = true; qhl.childForceExpandHeight = false;
            quickRowGO.GetComponent<LayoutElement>().minHeight = 30f;

            // We'll gather the per-channel toggles to control them from All/None.
            var channelToggles = new List<(ChatChannel ch, Toggle tg)>();

            // Helper to create a small toggle that behaves like a "momentary action"
            Toggle MakeMomentaryToggle(Transform parent, string label, Action onMomentary)
            {
                var go = new GameObject(label + "_Toggle", typeof(RectTransform), typeof(Toggle), typeof(Image), typeof(LayoutElement));
                var rt = (RectTransform)go.transform; rt.SetParent(parent, false);

                var bg = go.GetComponent<Image>(); bg.color = new Color(1, 1, 1, 0.05f);
                var le2 = go.GetComponent<LayoutElement>(); le2.minWidth = 80f; le2.minHeight = 26f;

                // check mark
                var checkGO = new GameObject("Check", typeof(RectTransform), typeof(Image));
                var check = (RectTransform)checkGO.transform; check.SetParent(rt, false);
                check.anchorMin = new Vector2(0, 0.5f); check.anchorMax = new Vector2(0, 0.5f);
                check.pivot = new Vector2(0, 0.5f);
                check.anchoredPosition = new Vector2(6f, 0f);
                check.sizeDelta = new Vector2(14f, 14f);
                var checkImg = checkGO.GetComponent<Image>(); checkImg.color = Color.white;

                // label
                var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
                lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1);
                lrt.offsetMin = new Vector2(24f, 0); lrt.offsetMax = new Vector2(0, 0);
                var tmp = lgo.GetComponent<TextMeshProUGUI>();
                tmp.text = label; tmp.fontSize = 14; tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.color = Color.white; tmp.raycastTarget = false;

                var t = go.GetComponent<Toggle>();
                t.graphic = checkImg;
                t.targetGraphic = bg;
                var colors = t.colors;
                colors.normalColor = new Color(1, 1, 1, 0.05f);
                colors.highlightedColor = new Color(1, 1, 1, 0.12f);
                colors.pressedColor = new Color(1, 1, 1, 0.18f);
                t.colors = colors;
                t.navigation = new Navigation { mode = Navigation.Mode.None };

                t.isOn = false;
                t.onValueChanged.AddListener(isOn =>
                {
                    if (!isOn) return;
                    onMomentary?.Invoke();
                    // reset off without triggering again
                    t.SetIsOnWithoutNotify(false);
                });

                return t;
            }

            // We'll compute an "all singles" mask (only one-bit flags).
            ChatChannel AllSinglesMask()
            {
                ChatChannel m = 0;
                foreach (ChatChannel ch in Enum.GetValues(typeof(ChatChannel)))
                {
                    int v = (int)ch;
                    if (v != 0 && (v & (v - 1)) == 0) m |= ch;
                }
                return m;
            }

            // Working mask for this popup
            int idxForMask = tabIndex;
            ChatChannel workingMask = tabs[idxForMask].channelMask;

            // Create All / None action toggles (momentary)
            var allToggle = MakeMomentaryToggle(quickRow, "All", () =>
            {
                // turn ON every per-channel toggle without firing per-toggle guards
                var all = AllSinglesMask();
                foreach (var pair in channelToggles)
                    pair.tg.SetIsOnWithoutNotify(true);
                workingMask = all;
            });

            var noneToggle = MakeMomentaryToggle(quickRow, "None", () =>
            {
                foreach (var pair in channelToggles)
                    pair.tg.SetIsOnWithoutNotify(false);
                workingMask = 0;
            });

            AddSeparator(panel, 1f, new Color(1, 1, 1, 0.12f));
            // --------------------------------------------------------------------

            // Grid of per-channel checkboxes (single-bit channels only)
            var gridGO = new GameObject("ChannelsGrid",
                typeof(RectTransform),
                typeof(GridLayoutGroup),
                typeof(LayoutElement));
            var grid = (RectTransform)gridGO.transform; grid.SetParent(panel, false);
            var glg = gridGO.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(200f, 30f);
            glg.spacing = new Vector2(10f, 8f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            gridGO.GetComponent<LayoutElement>().minWidth = 400f;

            foreach (ChatChannel ch in Enum.GetValues(typeof(ChatChannel)))
            {
                int v = (int)ch;
                if (v == 0 || (v & (v - 1)) != 0) continue; // single-bit only

                bool initialOn = (workingMask & ch) != 0;

                // Create and capture the toggle so All/None can manipulate it silently
                var tg = MakeChannelCheckbox(grid, ch.ToString(), initialOn, (toggle, isOn) =>
                {
                    if (isOn)
                    {
                        workingMask |= ch;
                    }
                    else
                    {
                        var candidate = workingMask & ~ch;
                        if ((int)candidate == 0)
                        {
                            // Must have at least one channel selected when toggling individually.
                            // (All/None bypass this via SetIsOnWithoutNotify and direct mask edits.)
                            toggle.isOn = true;
                            return;
                        }
                        workingMask = candidate;
                    }
                });

                channelToggles.Add((ch, tg));
            }

            AddSeparator(panel, 1f, new Color(1, 1, 1, 0.15f));

            // Buttons row
            var rowGO = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var row = (RectTransform)rowGO.transform; row.SetParent(panel, false);
            var hl = rowGO.GetComponent<HorizontalLayoutGroup>();
            hl.childAlignment = TextAnchor.MiddleRight;
            hl.spacing = 8;
            hl.childControlWidth = false; hl.childForceExpandWidth = false;
            hl.childControlHeight = true; hl.childForceExpandHeight = false;
            rowGO.GetComponent<LayoutElement>().minHeight = 32f;

            MakeButton(row, "Cancel", CloseWithoutApply, 96f);
            MakeButton(row, "Apply", ApplyAndClose, 96f);

            // Layout + clamp
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            ClampChildInside(panel, popupLayer, 8f);

            backdropButton.onClick.AddListener(CloseWithoutApply);

            // Local helpers
            void CloseWithoutApply()
            {
                ClearEventSelection();
                EndPopupCursorGuard();
                if (backdropGO) Destroy(backdropGO);
            }

            void ApplyAndClose()
            {
                tabs[idxForMask].channelMask = workingMask;
                if (idxForMask == activeTabIndex)
                {
                    ApplyLegacyFilterToggles(workingMask);
                    RebuildVisibleFromHistory();
                }
                CloseWithoutApply();
            }
        }

        // ----------------- Rename Popup -----------------

        private void ShowRenamePopup(int tabIndex, Vector2 screenPosition)
        {
            var popupLayer = EnsurePopupLayerExists(); if (!popupLayer) return;
            var canvas = popupLayer.GetComponent<Canvas>();
            var camera = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

            StartPopupCursorGuard();

            // Backdrop
            var backdropGO = new GameObject("RenameBackdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            var backdrop = (RectTransform)backdropGO.transform;
            backdrop.SetParent(popupLayer, false);
            backdrop.SetAsLastSibling();
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = Vector2.zero; backdrop.offsetMax = Vector2.zero;
            backdropGO.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var backdropButton = backdropGO.GetComponent<Button>();

            // Panel
            var panelGO = new GameObject("RenamePanel",
                typeof(RectTransform),
                typeof(Image),
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter),
                typeof(LayoutElement));
            var panel = (RectTransform)panelGO.transform; panel.SetParent(backdrop, false);
            panelGO.transform.SetAsLastSibling();

            var panelImage = panelGO.GetComponent<Image>();
            panelImage.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

            var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.spacing = 8;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true; vlg.childForceExpandHeight = false;

            var fit = panelGO.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var le = panelGO.GetComponent<LayoutElement>();
            le.minWidth = 260f; le.preferredWidth = 320f;

            panel.pivot = new Vector2(0, 1);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, screenPosition, camera, out var local);
            panel.anchoredPosition = local;

            // Input background
            var inputBGGO = new GameObject("NameInputBG", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var inputBgrt = (RectTransform)inputBGGO.transform; inputBgrt.SetParent(panel, false);
            inputBGGO.GetComponent<Image>().color = new Color(1, 1, 1, 0.06f);
            var inputBGle = inputBGGO.GetComponent<LayoutElement>();
            inputBGle.minWidth = 240f; inputBGle.preferredWidth = 280f;
            inputBGle.minHeight = 32f; inputBGle.preferredHeight = 34f;

            // Input field
            var input = inputBGGO.AddComponent<TMP_InputField>();
            input.characterLimit = 32;
            input.text = tabs[tabIndex].label;
            input.customCaretColor = true; input.caretColor = Color.white; input.caretWidth = 2; input.caretBlinkRate = 0.8f;
            input.selectionColor = new Color(1f, 1f, 1f, 0.25f);
#if UNITY_EDITOR || UNITY_STANDALONE
            input.shouldHideMobileInput = true;
#endif

            // Text viewport + text + placeholder
            var vpGO = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var vprt = (RectTransform)vpGO.transform; vprt.SetParent(inputBgrt, false);
            vprt.anchorMin = new Vector2(0, 0); vprt.anchorMax = new Vector2(1, 1);
            vprt.offsetMin = new Vector2(8, 6); vprt.offsetMax = new Vector2(-8, -6);

            var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRT = (RectTransform)textGO.transform; textRT.SetParent(vprt, false);
            textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one; textRT.offsetMin = Vector2.zero; textRT.offsetMax = Vector2.zero;
            var text = textGO.GetComponent<TextMeshProUGUI>();
            text.enableWordWrapping = false; text.alignment = TextAlignmentOptions.MidlineLeft; text.fontSize = 16; text.raycastTarget = false;

            var phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var phRT = (RectTransform)phGO.transform; phRT.SetParent(vprt, false);
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one; phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;
            var placeholder = phGO.GetComponent<TextMeshProUGUI>();
            placeholder.text = "Tab name"; placeholder.fontSize = 16; placeholder.color = new Color(1, 1, 1, 0.35f); placeholder.raycastTarget = false;

            input.textViewport = vprt;
            input.textComponent = text;
            input.placeholder = placeholder;

            // Buttons row
            var rowGO = new GameObject("ButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var row = (RectTransform)rowGO.transform; row.SetParent(panel, false);
            var hl = rowGO.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 8;
            hl.childControlWidth = false; hl.childForceExpandWidth = false;
            hl.childControlHeight = true; hl.childForceExpandHeight = false;
            rowGO.GetComponent<LayoutElement>().minHeight = 32f;

            void MakeBtn(string label, Action onClick, float minW = 88f)
            {
                var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                var rt = (RectTransform)go.transform; rt.SetParent(row, false);

                go.GetComponent<Image>().color = new Color(1, 1, 1, 0.06f);
                var le2 = go.GetComponent<LayoutElement>(); le2.minWidth = minW; le2.minHeight = 28f; le2.preferredWidth = minW;

                var btn = go.GetComponent<Button>();
                var colors = btn.colors;
                colors.highlightedColor = new Color(1, 1, 1, 0.12f);
                colors.pressedColor = new Color(1, 1, 1, 0.18f);
                btn.colors = colors;
                btn.onClick.AddListener(() => onClick?.Invoke());

                var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
                var l = lgo.GetComponent<TextMeshProUGUI>();
                l.text = label; l.fontSize = 14; l.alignment = TextAlignmentOptions.Center; l.color = Color.white; l.raycastTarget = false;
            }

            void CommitAndClose(string value)
            {
                string newName = string.IsNullOrWhiteSpace(value) ? tabs[tabIndex].label : value.Trim();
                tabs[tabIndex].label = newName;
                BuildTabs();
                SelectTab(tabIndex);
                Close();
            }

            void Close()
            {
                ClearEventSelection();
                EndPopupCursorGuard();
                if (backdropGO) Destroy(backdropGO);
            }

            input.onSubmit.AddListener(_ => CommitAndClose(input.text));
            MakeBtn("Rename", () => CommitAndClose(input.text));
            MakeBtn("Cancel", Close);

            backdropButton.onClick.AddListener(Close);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            ClampChildInside(panel, popupLayer, 8f);

            // Ensure visible caret and focus
            var es = EventSystem.current;
            if (es) es.SetSelectedGameObject(input.gameObject);
            StartCoroutine(FocusInputNextFrame(input));
        }

        // ----------------- Messages -----------------

        private void HandleIncomingMessage(ChatMessage message)
        {
            history.Add(message);
            if (history.Count > maxHistoryLines) history.RemoveAt(0);

            if ((tabs[activeTabIndex].channelMask & message.channel) == 0) return;
            AddVisibleLine(FormatMessage(message));
        }

        private void RebuildVisibleFromHistory()
        {
            // Clear visible
            foreach (var ln in visibleLines) if (ln) Destroy(ln.gameObject);
            visibleLines.Clear();

            var mask = tabs[activeTabIndex].channelMask;
            for (int i = history.Count - 1; i >= 0; --i)
                if ((mask & history[i].channel) != 0)
                    AddVisibleLine(FormatMessage(history[i]));

            RefreshLayoutAndMask(true);
            if (autoScrollToBottom && scrollRect)
                scrollRect.verticalNormalizedPosition = BottomNormalizedPosition();
        }

        private string FormatMessage(ChatMessage message)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(message.unixTimeMs).ToLocalTime().DateTime;
            string stamp = use24HourTimestamps ? time.ToString("HH:mm") : time.ToString("h:mm tt");

            string channelHex = message.channel switch
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

            var sb = new StringBuilder(256);
            if (showTimestamps)
                sb.Append("<color=#6B7685>[</color>").Append(stamp).Append("<color=#6B7685>]</color> ");

            sb.Append("<color=").Append(channelHex).Append(">").Append("[").Append(message.channel).Append("]</color> ");
            if (!string.IsNullOrEmpty(message.from))
                sb.Append("<b>").Append(message.from).Append(":</b> ");

            // Colorize any <link="item:...">...</link> by rarity (no underline)
            sb.Append(ColorizeItemLinks(message.text));

            return sb.ToString();
        }

        private void AddVisibleLine(string richText)
        {
            if (!linePrefab || !content) return;

            var line = Instantiate(linePrefab, content);
            line.Set(richText);

            // Newest appears visually at bottom with reverse arrangement (index 0)
            line.transform.SetSiblingIndex(0);
            visibleLines.Insert(0, line);

            // Prune oldest
            while (visibleLines.Count > maxVisibleLines)
            {
                int last = visibleLines.Count - 1;
                if (visibleLines[last]) Destroy(visibleLines[last].gameObject);
                visibleLines.RemoveAt(last);
            }

            RefreshLayoutAndMask(false);
            if (autoScrollToBottom && scrollRect)
                scrollRect.verticalNormalizedPosition = BottomNormalizedPosition();
        }

        // ----------------- Send -----------------

        public void SendFromInput()
        {
            if (!inputField) return;
            string text = inputField.text;
            if (string.IsNullOrWhiteSpace(text)) return;

            var channel = channelDropdown ? DropdownToChannel(channelDropdown.value) : ChatChannel.General;

            // Whisper shorthand: /w name message
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

        private ChatChannel DropdownToChannel(int index) => index switch
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

        private void AfterSend()
        {
            inputField.text = "";
            if (keepFocusAfterSend)
            {
                FocusChatInput();
            }
            else
            {
                suppressEnterRefocus = true;
                UnfocusChatInput();
                StartCoroutine(ClearEnterRefocusNextFrame());
            }
        }

        private System.Collections.IEnumerator ClearEnterRefocusNextFrame()
        {
            yield return null;
            suppressEnterRefocus = false;
        }

        private void BuildChannelDropdown()
        {
            if (!channelDropdown) return;
            channelDropdown.ClearOptions();
            channelDropdown.AddOptions(new List<string> { "General", "Say", "Party", "Guild", "Trade", "Whisper", "Global" });
            channelDropdown.value = 0;
        }

        // ----------------- Legacy Filters Panel (optional) -----------------

        private void ApplyLegacyFilterToggles(ChatChannel mask)
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
            if (legacyFiltersPanel) legacyFiltersPanel.SetActive(false); // hidden in this fresh build
        }

        // ----------------- Layout & Mask -----------------

        private void ConfigureChatListLayout()
        {
            if (!scrollRect || !content) return;

            var viewport = scrollRect.viewport ? scrollRect.viewport : scrollRect.transform as RectTransform;
            if (viewport)
            {
                var img = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0); img.raycastTarget = true;
                if (!viewport.GetComponent<RectMask2D>()) viewport.gameObject.AddComponent<RectMask2D>();
                viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.anchoredPosition = Vector2.zero;
                viewport.offsetMin = viewport.offsetMax = Vector2.zero;
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
            content.anchorMin = new Vector2(0f,0f);
            content.anchorMax = new Vector2(1f,0f);
            content.pivot     = new Vector2(0.5f,1f);
            content.anchoredPosition = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;  vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var csf = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
#endif

            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.horizontal = false; scrollRect.vertical = true;

            // Remove any legacy spacer
            for (int i = content.childCount - 1; i >= 0; i--)
                if (content.GetChild(i).name == "__SpacerTop")
                    DestroyImmediate(content.GetChild(i).gameObject);

            if (autoScrollToBottom)
                scrollRect.verticalNormalizedPosition = BottomNormalizedPosition();
        }

        private float BottomNormalizedPosition()
        {
            // For top-pivot content (new Unity path), bottom = 0
            // For bottom-pivot content (old Unity path), bottom = 1
            return Mathf.Approximately(content ? content.pivot.y : 1f, 1f) ? 0f : 1f;
        }

        private void RefreshLayoutAndMask(bool snapBottom)
        {
            if (!content) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            var refresher = scrollRect ? scrollRect.GetComponent<ChatScrollMaskRefresher>() : null;
            if (refresher) refresher.ForceClipRefresh(snapBottom);
        }

        // ----------------- Cursor / Focus Helpers -----------------

        private void FocusChatInput()
        {
            if (!inputField) return;
            var es = EventSystem.current; if (!es) return;

            es.SetSelectedGameObject(null);
            StartCoroutine(FocusInputNextFrame(inputField));
            CaptureMouseForChat();
        }

        private System.Collections.IEnumerator FocusInputNextFrame(TMP_InputField field)
        {
            yield return null;
            if (!field) yield break;

            field.interactable = true;
            field.ActivateInputField();
            field.Select();
            field.caretPosition = field.text?.Length ?? 0;
            field.MoveTextEnd(false);
            field.ForceLabelUpdate();
        }

        private void UnfocusChatInput()
        {
            if (!inputField) return;
            inputField.DeactivateInputField();

            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject == inputField.gameObject)
                es.SetSelectedGameObject(null);

            RestoreMouseAfterChat();
        }

        private void CaptureMouseForChat()
        {
            if (cursorManagedThisFocus || !manageCursorWhenChatFocused) return;

            cachedLockMode = Cursor.lockState;
            cachedCursorVisible = Cursor.visible;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            cursorManagedThisFocus = true;

            if (disableWhileChatFocused != null)
                foreach (var b in disableWhileChatFocused) if (b) b.enabled = false;

            OnChatFocusChanged?.Invoke(true);
        }

        private void RestoreMouseAfterChat()
        {
            if (!cursorManagedThisFocus || !manageCursorWhenChatFocused) return;

            Cursor.lockState = cachedLockMode;
            Cursor.visible = cachedCursorVisible;
            cursorManagedThisFocus = false;

            if (disableWhileChatFocused != null)
                foreach (var b in disableWhileChatFocused) if (b) b.enabled = true;

            OnChatFocusChanged?.Invoke(false);
        }

        private void StartPopupCursorGuard()
        {
            popupPrevLock = Cursor.lockState;
            popupPrevVisible = Cursor.visible;

            popupCursorForce = true;
            if (popupCursorRoutine == null)
                popupCursorRoutine = StartCoroutine(PopupCursorKeepVisible());

            if (disableWhileChatFocused != null)
                foreach (var b in disableWhileChatFocused) if (b) b.enabled = false;

            OnChatFocusChanged?.Invoke(true);
        }

        private void EndPopupCursorGuard()
        {
            popupCursorForce = false;
            if (popupCursorRoutine != null)
            {
                StopCoroutine(popupCursorRoutine);
                popupCursorRoutine = null;
            }

            Cursor.lockState = popupPrevLock;
            Cursor.visible = popupPrevVisible;

            if (disableWhileChatFocused != null)
                foreach (var b in disableWhileChatFocused) if (b) b.enabled = true;

            OnChatFocusChanged?.Invoke(false);
        }

        // Back-compat wrapper in case anything still calls StopPopupCursorGuard
        private void StopPopupCursorGuard() => EndPopupCursorGuard();

        private System.Collections.IEnumerator PopupCursorKeepVisible()
        {
            while (popupCursorForce)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                yield return new WaitForEndOfFrame();
            }
        }

        private void ClearEventSelection()
        {
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
        }

        // ----------------- Popup Layer -----------------

        private RectTransform EnsurePopupLayerExists()
        {
            if (popupRootOverride)
            {
                var c = popupRootOverride.GetComponent<Canvas>();
                if (!c)
                {
                    var child = popupRootOverride.Find("__PopupLayer") as RectTransform;
                    if (!child)
                    {
                        var go = new GameObject("__PopupLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                        child = (RectTransform)go.transform;
                        child.SetParent(popupRootOverride, false);
                        child.anchorMin = Vector2.zero; child.anchorMax = Vector2.one;
                        child.offsetMin = Vector2.zero; child.offsetMax = Vector2.zero;
                    }
                    ConfigurePopupCanvas(child.GetComponent<Canvas>());
                    child.SetAsLastSibling();
                    return child;
                }
                else
                {
                    ConfigurePopupCanvas(c);
                    popupRootOverride.SetAsLastSibling();
                    return popupRootOverride;
                }
            }

            var rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (!rootCanvas) return null;

            var parentRT = (RectTransform)rootCanvas.transform;
            var existing = parentRT.Find("__PopupLayer") as RectTransform;
            if (existing)
            {
                var c2 = existing.GetComponent<Canvas>() ?? existing.gameObject.AddComponent<Canvas>();
                ConfigurePopupCanvas(c2);
                if (!existing.GetComponent<GraphicRaycaster>()) existing.gameObject.AddComponent<GraphicRaycaster>();
                existing.SetAsLastSibling();
                return existing;
            }
            else
            {
                var go = new GameObject("__PopupLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                var rt = (RectTransform)go.transform;
                rt.SetParent(parentRT, false);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                var c3 = go.GetComponent<Canvas>();
                ConfigurePopupCanvas(c3);
                rt.SetAsLastSibling();
                return rt;
            }
        }

        private void ConfigurePopupCanvas(Canvas canvas)
        {
            var root = GetComponentInParent<Canvas>()?.rootCanvas;
            if (root)
            {
                canvas.renderMode = root.renderMode;
                canvas.worldCamera = root.worldCamera;
                canvas.planeDistance = root.planeDistance;
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = popupSortingOrder;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }

        // ----------------- Small UI Helpers -----------------

        private void AddSeparator(Transform parent, float height, Color color)
        {
            var go = new GameObject("Separator", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = color;
            var le = go.GetComponent<LayoutElement>(); le.minHeight = height; le.preferredHeight = height;
        }

        private void MakeButton(Transform parent, string label, Action onClick, float minWidth)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);

            go.GetComponent<Image>().color = new Color(1, 1, 1, 0.06f);
            var le = go.GetComponent<LayoutElement>(); le.minWidth = minWidth; le.minHeight = 28f; le.preferredWidth = minWidth;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1, 1, 1, 0.12f);
            colors.pressedColor = new Color(1, 1, 1, 0.18f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var l = lgo.GetComponent<TextMeshProUGUI>();
            l.text = label; l.fontSize = 14; l.alignment = TextAlignmentOptions.Center; l.color = Color.white; l.raycastTarget = false;
        }

        private Toggle MakeChannelCheckbox(Transform parent, string label, bool isOn, Action<Toggle, bool> onChanged)
        {
            var go = new GameObject("T_" + label, typeof(RectTransform), typeof(Toggle), typeof(Image), typeof(LayoutElement));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);

            var rowBg = go.GetComponent<Image>();
            rowBg.color = new Color(1, 1, 1, 0.03f);

            var le = go.GetComponent<LayoutElement>();
            le.minWidth = 180f; le.minHeight = 28f;

            // Checkbox frame
            var boxGO = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var box = (RectTransform)boxGO.transform; box.SetParent(rt, false);
            box.anchorMin = new Vector2(0, 0.5f); box.anchorMax = new Vector2(0, 0.5f);
            box.pivot = new Vector2(0, 0.5f);
            box.anchoredPosition = new Vector2(8f, 0f);
            box.sizeDelta = new Vector2(18f, 18f);
            boxGO.GetComponent<Image>().color = new Color(1, 1, 1, 0.22f);

            // Check fill
            var checkGO = new GameObject("Check", typeof(RectTransform), typeof(Image));
            var check = (RectTransform)checkGO.transform; check.SetParent(box, false);
            check.anchorMin = new Vector2(0.5f, 0.5f); check.anchorMax = new Vector2(0.5f, 0.5f);
            check.pivot = new Vector2(0.5f, 0.5f);
            check.anchoredPosition = Vector2.zero;
            check.sizeDelta = new Vector2(12f, 12f);
            var checkImg = checkGO.GetComponent<Image>();
            checkImg.color = Color.white;

            // Label
            var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lrt = (RectTransform)lgo.transform; lrt.SetParent(rt, false);
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(1, 1);
            lrt.offsetMin = new Vector2(32f, 0); lrt.offsetMax = new Vector2(0, 0);
            var tmp = lgo.GetComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 15; tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white; tmp.raycastTarget = false;

            var toggle = go.GetComponent<Toggle>();
            toggle.graphic = checkImg;
            toggle.targetGraphic = rowBg;

            var colors = toggle.colors;
            colors.normalColor = new Color(1, 1, 1, 0.03f);
            colors.highlightedColor = new Color(1, 1, 1, 0.08f);
            colors.pressedColor = new Color(1, 1, 1, 0.12f);
            colors.selectedColor = colors.highlightedColor;
            toggle.colors = colors;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };

            toggle.isOn = isOn;
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(toggle, v));
            return toggle;
        }

        private void ClampChildInside(RectTransform child, RectTransform parent, float padding)
        {
            var pr = parent.rect;
            var size = child.rect.size;
            var pos = child.anchoredPosition;
            var pivot = child.pivot;

            float minX = pr.xMin + padding + pivot.x * size.x;
            float maxX = pr.xMax - padding - (1f - pivot.x) * size.x;
            float minY = pr.yMin + padding + pivot.y * size.y;
            float maxY = pr.yMax - padding - (1f - pivot.y) * size.y;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            child.anchoredPosition = pos;
        }

        // --------------- Right-click wiring for tab toggles ---------------

        // Attach TabRightClickCatcher if available; otherwise add an EventTrigger fallback.
        private void WireRightClickHandlers(Toggle toggle, int tabIndex)
        {
            if (!toggle) return;

            // Try to use your existing TabRightClickCatcher
            var catcherType = Type.GetType("MMO.Chat.UI.TabRightClickCatcher, Assembly-CSharp", throwOnError: false);
            if (catcherType != null)
            {
                var comp = toggle.GetComponent(catcherType) ?? toggle.gameObject.AddComponent(catcherType);

                // Best-effort: set ChatWindow/Toggle refs and tab index by reflection
                TryAssignRefByType(comp, typeof(ChatWindow), this);
                TryAssignRefByType(comp, typeof(Toggle), toggle);
                TryAssignIntByCommonNames(comp, tabIndex, "tabIndex", "index", "Idx", "TabIndex");
                return;
            }

            // Fallback: EventTrigger -> right-click opens the "Tab" menu for this toggle
            var trigger = toggle.gameObject.GetComponent<EventTrigger>() ?? toggle.gameObject.AddComponent<EventTrigger>();

            // Remove any existing PointerClick entries we might have added before (defensive)
            trigger.triggers.RemoveAll(e => e != null && e.eventID == EventTriggerType.PointerClick);

            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener((BaseEventData bed) =>
            {
                var ped = bed as PointerEventData;
                if (ped != null && ped.button == PointerEventData.InputButton.Right)
                {
                    // compute logical index among Toggle children
                    int logical = 0, idx = -1;
                    for (int i = 0; i < tabBar.childCount; i++)
                    {
                        var t = tabBar.GetChild(i).GetComponent<Toggle>();
                        if (!t) continue;
                        if (t == toggle) { idx = logical; break; }
                        logical++;
                    }
                    if (idx >= 0) ShowContextMenu("Tab", ped.position, idx);
                }
            });
            trigger.triggers.Add(entry);
        }

        private static void TryAssignRefByType(object targetComponent, Type refType, object value)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = targetComponent.GetType();

            // fields
            foreach (var f in t.GetFields(BF))
                if (refType.IsAssignableFrom(f.FieldType))
                {
                    f.SetValue(targetComponent, value);
                    return;
                }

            // properties
            foreach (var p in t.GetProperties(BF))
                if (p.CanWrite && refType.IsAssignableFrom(p.PropertyType))
                {
                    p.SetValue(targetComponent, value);
                    return;
                }
        }

        private static void TryAssignIntByCommonNames(object targetComponent, int value, params string[] names)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = targetComponent.GetType();

            foreach (var n in names)
            {
                var f = t.GetField(n, BF);
                if (f != null && (f.FieldType == typeof(int))) { f.SetValue(targetComponent, value); return; }

                var p = t.GetProperty(n, BF);
                if (p != null && p.CanWrite && (p.PropertyType == typeof(int))) { p.SetValue(targetComponent, value); return; }
            }
        }

        // ----------------- Item link recolor helper -----------------

        private string ColorizeItemLinks(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            return s_itemLinkRx.Replace(raw, m =>
            {
                string id = m.Groups[1].Value;
                // resolve ItemDef (via optional resolver or Resources)
                var def = ItemTooltipComposer.Resolve(id, optionalItemLookup, resourcesItemsFolder);
                if (def)
                {
                    // composer returns <link="item:id"><color=#HEX>Name</color></link> (no underline when false)
                    return ItemTooltipComposer.FormatChatLink(def, underline: false);
                }

                // Fallback: keep original label but at least remove underline and use a neutral color
                string label = m.Groups["label"].Value;
                return $"<link=\"item:{id}\"><color=#FFFFFF>{label}</color></link>";
            });
        }
    }
}
