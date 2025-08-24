// Assets/Resources/UI/Common/UIContextMenu.cs
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MMO.Common.UI
{
    public class UIContextMenu : MonoBehaviour
    {
        [Header("Wiring")]
        public CanvasGroup group;
        public Button blocker;
        public RectTransform panel;
        public RectTransform content;
        public Button templateButton;
        public Toggle templateToggle;

        [Header("Behavior")]
        public float edgePadding = 8f;
        public float fadeTime = 0.08f;
        bool _hasActionItemAdded;  // tracks if any buttons/toggles were added before Cancel
        RectTransform _root;
        Camera _cam;

        void Awake()
        {
            if (!group) group = GetComponent<CanvasGroup>();
            if (!blocker) blocker = transform.Find("Blocker")?.GetComponent<Button>();
            if (!panel) panel = transform.Find("Panel") as RectTransform;
            if (!content) content = panel ? panel.Find("Content") as RectTransform : null;

            // Ensure this popup is fully independent of parent UI
            var selfCanvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            var raycaster = GetComponent<GraphicRaycaster>() ?? gameObject.AddComponent<GraphicRaycaster>();
            selfCanvas.overrideSorting = true;
            selfCanvas.sortingOrder = 10000; // draw & raycast above HUD

            if (templateButton) templateButton.gameObject.SetActive(false);
            if (templateToggle) templateToggle.gameObject.SetActive(false);

            if (blocker)
            {
                blocker.onClick.AddListener(Close);
                blocker.transform.SetAsFirstSibling(); // behind panel
                var blkImg = blocker.GetComponent<Image>();
                if (blkImg) blkImg.raycastTarget = true;
            }

            if (group)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
                group.ignoreParentGroups = true; // don't inherit non-interactable parents
            }

            if (panel)
            {
                panel.SetAsLastSibling(); // above blocker
                var pImg = panel.GetComponent<Image>();
                if (pImg) pImg.raycastTarget = true;

                // Layout safety so it never stretches full-screen
                var vlg = panel.GetComponent<VerticalLayoutGroup>();
                if (vlg)
                {
                    vlg.childControlWidth = true; vlg.childForceExpandWidth = false;
                    vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
                }
            }

            // Guarantee the button template has a raycastable Graphic
            if (templateButton)
            {
                if (!templateButton.targetGraphic)
                {
                    var img = templateButton.GetComponent<Image>() ?? templateButton.gameObject.AddComponent<Image>();
                    img.color = new Color(1, 1, 1, 0.06f);
                    img.raycastTarget = true;
                    templateButton.targetGraphic = img;
                }
                else
                {
                    var g = templateButton.targetGraphic as Graphic;
                    if (g) g.raycastTarget = true;
                }

                // Labels should not steal raycasts
                var lbl = templateButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (lbl) lbl.raycastTarget = false;
            }

            if (templateToggle)
            {
                var g = templateToggle.targetGraphic as Graphic;
                if (g) g.raycastTarget = true;
                var lbl = templateToggle.GetComponentInChildren<TextMeshProUGUI>(true);
                if (lbl) lbl.raycastTarget = false;
            }
        }

        /// <summary>Spawn and show prefab at Resources/UI/Common/UIContextMenu.</summary>
        public static UIContextMenu Show(RectTransform rootCanvas, Camera uiOrWorldCam, Vector2 screenPos,
                                         Action<UIContextMenu> build)
        {
            var prefab = Resources.Load<UIContextMenu>("UI/Common/UIContextMenu")
                      ?? Resources.Load<GameObject>("UI/Common/UIContextMenu")?.GetComponent<UIContextMenu>();
            if (!prefab) { Debug.LogError("UIContextMenu prefab not found at Resources/UI/Common/UIContextMenu"); return null; }

            var menu = Instantiate(prefab, rootCanvas);
            menu.Init(rootCanvas, uiOrWorldCam);
            build?.Invoke(menu);
            menu.OpenAt(screenPos);
            return menu;
        }

        public void Init(RectTransform rootCanvas, Camera uiOrWorldCam)
        {
            _root = rootCanvas;
            _cam = uiOrWorldCam;
        }

        public void AddButton(string label, UnityAction onClick)
        {
            if (!templateButton) { Debug.LogError("UIContextMenu: templateButton not assigned"); return; }
            if (!content) { Debug.LogError("UIContextMenu: content not assigned"); return; }

            var b = Instantiate(templateButton, content);
            b.gameObject.SetActive(true);
            var txt = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt) txt.text = label;
            b.onClick.AddListener(onClick);
            b.onClick.AddListener(Close);

            _hasActionItemAdded = true; // <—
        }

        public void AddToggle(string label, bool initial, UnityAction<bool> onChange)
        {
            if (!templateToggle) { Debug.LogError("UIContextMenu: templateToggle not assigned"); return; }
            if (!content) { Debug.LogError("UIContextMenu: content not assigned"); return; }

            var t = Instantiate(templateToggle, content);
            t.gameObject.SetActive(true);
            var txt = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt) txt.text = label;
            t.isOn = initial;
            t.onValueChanged.AddListener(onChange);

            _hasActionItemAdded = true; // <—
        }

        public void AddSeparator(float h = 8f, float alpha = 0.15f)
        {
            if (!content) return;
            var go = new GameObject("Separator", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>(); rt.SetParent(content, false);
            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, alpha);
            var le = go.GetComponent<LayoutElement>(); le.minHeight = h; le.preferredHeight = h;
        }

        public void Close() { Destroy(gameObject); }

        // ---------- Internals ----------
        void OpenAt(Vector2 screenPos)
        {
            if (!_root || !panel) return;

            var rootCanvas = _root.GetComponentInParent<Canvas>();
            var cam = (rootCanvas && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? _cam : null;

            panel.pivot = new Vector2(0f, 1f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPos, cam, out var local);
            panel.anchoredPosition = local;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            ClampInside();

            StartCoroutine(FadeIn());
        }

        IEnumerator FadeIn()
        {
            if (!group) yield break;
            group.blocksRaycasts = true;   // enable interaction immediately
            group.interactable = true;

            float t = 0f;
            while (t < fadeTime)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(0f, 1f, Mathf.Clamp01(t / fadeTime));
                yield return null;
            }
            group.alpha = 1f;
        }

        void ClampInside()
        {
            var pr = _root.rect;
            var r = panel.rect;
            var pos = panel.anchoredPosition;

            float minX = pr.xMin + edgePadding + panel.pivot.x * r.width;
            float maxX = pr.xMax - edgePadding - (1f - panel.pivot.x) * r.width;
            float minY = pr.yMin + edgePadding + panel.pivot.y * r.height;
            float maxY = pr.yMax - edgePadding - (1f - panel.pivot.y) * r.height;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            panel.anchoredPosition = pos;
        }

        void Update()
        {
            // Close on Esc if the popup is active
            if (group && group.interactable && Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        public void AddCancelButton(string label = "Cancel")
        {
            if (!templateButton || !content) return;

            // Add a separator only if other actionable items were added first
           // if (_hasActionItemAdded)
            //    AddSeparator(4f, 0.15f);

            var b = Instantiate(templateButton, content);
            b.gameObject.SetActive(true);

            var txt = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (txt) txt.text = label;

            // Only close; no other action
            b.onClick.AddListener(Close);

            // Optional: make Cancel look slightly more subtle
            var g = b.targetGraphic as Graphic;
            if (g) g.color = new Color(g.color.r, g.color.g, g.color.b, Mathf.Min(g.color.a + 0.02f, 0.2f));
        }
    }
}
