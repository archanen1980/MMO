using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using MMO.Shared.Item;      // ItemDef (for the static Show helpers)
using MMO.Inventory.UI;     // ItemTooltipComposer

namespace MMO.Inventory.UI
{
    /// Super-simple, robust tooltip:
    /// - Dedicated Screen Space Overlay canvas (MMO_TooltipOverlay).
    /// - Follows cursor, clamped to screen.
    /// - No raycast blocking.
    /// - RichText enabled so colored names render.
    [DefaultExecutionOrder(30000)]
    public class ItemTooltipCursor : MonoBehaviour
    {
        public static ItemTooltipCursor Instance { get; private set; }

        [Header("Wiring")]
        [SerializeField] private RectTransform panel;          // root
        [SerializeField] private CanvasGroup group;            // on panel
        [SerializeField] private LayoutElement panelLE;        // on panel
        [SerializeField] private Image iconImage;              // optional
        [SerializeField] private TMP_Text titleText;           // required (rich text)
        [SerializeField] private TMP_Text rarityText;          // NEW: rarity label
        [SerializeField] private TMP_Text bodyText;            // optional (rich text)

        [Header("Sizing")]
        [SerializeField] private float minWidth = 260f;
        [SerializeField] private float maxWidth = 420f;

        [Header("Placement")]
        [Tooltip("Pixels from cursor; (+x right, +y up). For 'down-right' use (16, -16).")]
        [SerializeField] private Vector2 cursorOffset = new Vector2(16f, -16f);
        [SerializeField] private float screenPadding = 12f;
        [SerializeField] private int sortingOrder = 500;

        static Canvas sOverlay;
        static RectTransform sOverlayRT;
        bool _visible;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            EnsureOverlayCanvas();

            if (!panel) panel = transform as RectTransform;
            if (!panelLE) panelLE = GetComponent<LayoutElement>();
            if (!group) group = GetComponent<CanvasGroup>();

            if (panel && sOverlayRT) panel.SetParent(sOverlayRT, false);

            // Top-left pivot for simple clamping math
            if (panel)
            {
                panel.pivot = new Vector2(0f, 1f);
                panel.anchorMin = panel.pivot;
                panel.anchorMax = panel.pivot;
            }

            // Never intercept pointer
            if (group) { group.blocksRaycasts = false; group.interactable = false; }
            foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            // Rich text so colored names render
            if (titleText) { titleText.richText = true; titleText.raycastTarget = false; }
            if (rarityText) { rarityText.richText = false; rarityText.raycastTarget = false; }
            if (bodyText) { bodyText.richText = true; bodyText.raycastTarget = false; }

            Hide();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        void OnActiveSceneChanged(Scene _, Scene __) => Hide();

        void EnsureOverlayCanvas()
        {
            if (sOverlay) { sOverlay.sortingOrder = sortingOrder; return; }

            var go = GameObject.Find("MMO_TooltipOverlay");
            if (!go)
            {
                go = new GameObject("MMO_TooltipOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                DontDestroyOnLoad(go);
            }

            sOverlay = go.GetComponent<Canvas>();
            sOverlay.renderMode = RenderMode.ScreenSpaceOverlay;
            sOverlay.sortingOrder = sortingOrder;
            sOverlay.pixelPerfect = false;

            var gr = go.GetComponent<GraphicRaycaster>();
            if (gr) gr.enabled = false;

            sOverlayRT = go.transform as RectTransform;
            sOverlayRT.anchorMin = Vector2.zero;
            sOverlayRT.anchorMax = Vector2.one;
            sOverlayRT.pivot = new Vector2(0.5f, 0.5f);
            sOverlayRT.offsetMin = Vector2.zero;
            sOverlayRT.offsetMax = Vector2.zero;
        }

        public struct Payload
        {
            public Sprite Icon;
            public string Title;       // rich text (colored name)
            public string Body;        // optional
            public string RarityName;  // plain text label e.g. "Legendary"
            public Color RarityColor; // applied to rarityText.color
        }

        /// Show and start following the cursor.
        public void ShowAtCursor(Payload p)
        {
            if (!panel) return;

            if (iconImage)
            {
                iconImage.sprite = p.Icon;
                iconImage.enabled = p.Icon != null;
            }

            if (titleText) titleText.text = string.IsNullOrEmpty(p.Title) ? "Item" : p.Title;

            if (rarityText)
            {
                if (!string.IsNullOrEmpty(p.RarityName))
                {
                    rarityText.text = p.RarityName;
                    rarityText.color = (p.RarityColor.a > 0f) ? p.RarityColor : Color.white;
                    rarityText.gameObject.SetActive(true);
                }
                else
                {
                    rarityText.text = "";
                    rarityText.gameObject.SetActive(false);
                }
            }

            if (bodyText) bodyText.text = p.Body ?? string.Empty;

            AdjustWidthToContent();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            _visible = true;
            if (group) group.alpha = 1f;

            PositionAtCursor();
        }

        public void Hide()
        {
            _visible = false;
            if (group) group.alpha = 0f;
        }

        void LateUpdate()
        {
            if (_visible) PositionAtCursor();
        }

        void AdjustWidthToContent()
        {
            if (!panelLE) return;

            float titleW = titleText ? titleText.GetPreferredValues(titleText.text ?? "", 0, 0).x : 0f;
            float rarityW = rarityText ? rarityText.GetPreferredValues(rarityText.text ?? "", 0, 0).x : 0f;
            float bodyW = bodyText ? bodyText.GetPreferredValues(bodyText.text ?? "", 0, 0).x : 0f;

            float target = Mathf.Clamp(Mathf.Max(titleW, rarityW, bodyW), minWidth, maxWidth);
            panelLE.preferredWidth = target;
            panelLE.minWidth = minWidth;
        }

        void PositionAtCursor()
        {
            if (!panel || sOverlay == null) return;

            Vector2 desired = (Vector2)Input.mousePosition + cursorOffset;
            float sw = Screen.width;
            float sh = Screen.height;

            Vector2 pxSize = RectTransformUtility.PixelAdjustRect(panel, sOverlay).size;

            float x = Mathf.Clamp(desired.x, screenPadding, sw - pxSize.x - screenPadding);
            float y = Mathf.Clamp(desired.y, screenPadding + pxSize.y, sh - screenPadding);

            panel.position = new Vector3(x, y, 0f);
        }

        // ===== Unified helpers (composer-backed) =====

        public static void ShowAtCursor(ItemDef def)
        {
            if (!EnsureInstance()) return;
            var payload = ItemTooltipComposer.Build(def);
            Instance.ShowAtCursor(payload);
        }

        public static void ShowAtCursor(string itemId, UnityEngine.Object optionalLookup = null, string resourcesFolder = "Items")
        {
            if (!EnsureInstance()) return;
            var payload = ItemTooltipComposer.Build(itemId, optionalLookup, resourcesFolder);
            Instance.ShowAtCursor(payload);
        }

        public static void HideIfAny()
        {
            if (Instance) Instance.Hide();
        }

        static bool EnsureInstance()
        {
            if (Instance) return true;
            Instance = FindObjectOfType<ItemTooltipCursor>(true);
            if (!Instance)
            {
                Debug.LogWarning("[ItemTooltipCursor] No instance in scene.");
                return false;
            }
            return true;
        }
    }
}
