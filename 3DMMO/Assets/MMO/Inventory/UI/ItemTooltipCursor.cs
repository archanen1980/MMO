// Assets/MMO/Inventory/UI/ItemTooltipCursor.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

namespace MMO.Inventory.UI
{
    /// Super-simple, robust tooltip:
    /// - Creates/uses a dedicated Screen Space Overlay canvas (MMO_TooltipOverlay).
    /// - Positions panel.position = mouse + offset (screen pixels), clamped to screen.
    /// - Disables all raycasts so it never blocks hovering the slots.
    [DefaultExecutionOrder(30000)]
    public class ItemTooltipCursor : MonoBehaviour
    {
        public static ItemTooltipCursor Instance { get; private set; }

        [Header("Wiring")]
        [SerializeField] private RectTransform panel;          // this RectTransform (root of the tooltip)
        [SerializeField] private CanvasGroup group;            // on panel
        [SerializeField] private LayoutElement panelLE;        // on panel
        [SerializeField] private Image iconImage;              // optional
        [SerializeField] private TMP_Text titleText;           // required
        [SerializeField] private TMP_Text bodyText;            // optional

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

            // Move tooltip under the overlay canvas so positioning is in pure screen pixels.
            panel.SetParent(sOverlayRT, false);

            // Fixed geometry for simple math: top-left pivot
            panel.pivot = new Vector2(0f, 1f);
            panel.anchorMin = panel.pivot;
            panel.anchorMax = panel.pivot;

            // Never intercept pointer
            if (group) { group.blocksRaycasts = false; group.interactable = false; }
            foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            Hide();
            SceneManager.activeSceneChanged += (_, __) => Hide();
        }

        void EnsureOverlayCanvas()
        {
            if (sOverlay) return;

            var go = GameObject.Find("MMO_TooltipOverlay");
            if (!go)
            {
                go = new GameObject("MMO_TooltipOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                DontDestroyOnLoad(go);
            }

            sOverlay = go.GetComponent<Canvas>();
            sOverlay.renderMode = RenderMode.ScreenSpaceOverlay;
            sOverlay.sortingOrder = sortingOrder; // render above other UI
            sOverlay.pixelPerfect = false;

            // We don't need this to ever catch clicks
            var gr = go.GetComponent<GraphicRaycaster>();
            if (gr) gr.enabled = false;

            sOverlayRT = go.transform as RectTransform;
            sOverlayRT.anchorMin = Vector2.zero;
            sOverlayRT.anchorMax = Vector2.one;
            sOverlayRT.pivot = new Vector2(0.5f, 0.5f);
            sOverlayRT.offsetMin = Vector2.zero;
            sOverlayRT.offsetMax = Vector2.zero;
        }

        public struct Payload { public Sprite Icon; public string Title; public string Body; }

        /// Show and start following the cursor.
        public void ShowAtCursor(Payload p)
        {
            if (iconImage)
            {
                iconImage.sprite = p.Icon;
                iconImage.enabled = p.Icon != null;
            }
            if (titleText) titleText.text = string.IsNullOrEmpty(p.Title) ? "Item" : p.Title;
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
            float titleW = titleText ? titleText.GetPreferredValues(titleText.text, 0, 0).x : 0f;
            float bodyW = bodyText ? bodyText.GetPreferredValues(bodyText.text, 0, 0).x : 0f;
            float target = Mathf.Clamp(Mathf.Max(titleW, bodyW), minWidth, maxWidth);
            panelLE.preferredWidth = target;
            panelLE.minWidth = minWidth;
        }

        void PositionAtCursor()
        {
            // Desired top-left in SCREEN PIXELS
            Vector2 desired = (Vector2)Input.mousePosition + cursorOffset;

            // Screen rect (overlay canvas covers entire display)
            float sw = Screen.width;
            float sh = Screen.height;

            // Panel size in SCREEN PIXELS
            Vector2 pxSize = RectTransformUtility.PixelAdjustRect(panel, sOverlay).size;

            // Clamp with top-left pivot
            float x = Mathf.Clamp(desired.x, screenPadding, sw - pxSize.x - screenPadding);
            float y = Mathf.Clamp(desired.y, screenPadding + pxSize.y, sh - screenPadding);

            // For a Screen Space Overlay canvas, panel.position is in screen pixels.
            panel.position = new Vector3(x, y, 0f);
        }
    }
}
