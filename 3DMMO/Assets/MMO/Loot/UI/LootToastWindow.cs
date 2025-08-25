// Assets/MMO/Loot/UI/LootToastWindow.cs
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace MMO.Loot.UI
{
    [DisallowMultipleComponent]
    public class LootToastWindow : MonoBehaviour
    {
        public static LootToastWindow Instance { get; private set; }

        [Header("Wiring")]
        [Tooltip("Parent container where toasts are instantiated. If empty, uses this transform.")]
        [SerializeField] private RectTransform listRoot;

        [Tooltip("Prefab to spawn for each toast. Can be ANY prefab; a LootToastEntry will be added if missing.")]
        [SerializeField] private GameObject entryPrefab;

        [Header("Screen Clamp")]
        [Tooltip("If enabled, nudges this window fully on-screen after layout.")]
        [SerializeField] private bool clampToScreen = true;
        [SerializeField] private float screenPadding = 12f;

        void Awake()
        {
            Instance = this;
            if (!listRoot) listRoot = transform as RectTransform;
        }

        void OnEnable()
        {
            if (clampToScreen) StartCoroutine(CoClampAfterLayout());
        }

        System.Collections.IEnumerator CoClampAfterLayout()
        {
            // Wait for one frame so layout sizes are finalized
            yield return null;
            ClampWindowToScreen();
        }

        void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled && clampToScreen)
                ClampWindowToScreen();
        }

        void ClampWindowToScreen()
        {
            var rt = transform as RectTransform;
            if (!rt) return;

            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return;

            // Get window world corners
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float minX = corners[0].x; // bottom-left
            float minY = corners[0].y;
            float maxX = corners[2].x; // top-right
            float maxY = corners[2].y;

            float left = screenPadding;
            float right = Screen.width - screenPadding;
            float bottom = screenPadding;
            float top = Screen.height - screenPadding;

            float dx = 0f, dy = 0f;
            if (minX < left) dx += left - minX;
            if (maxX > right) dx -= maxX - right;
            if (minY < bottom) dy += bottom - minY;
            if (maxY > top) dy -= maxY - top;

            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f))
                return;

            Camera cam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : canvas.worldCamera;

            // Compute current pivot screen point and desired shifted screen point,
            // then convert to local space delta for anchoredPosition.
            Vector2 currScreen = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
            Vector2 nextScreen = currScreen + new Vector2(dx, dy);

            RectTransform parent = rt.parent as RectTransform;
            if (!parent) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, currScreen, cam, out var currLocal);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, nextScreen, cam, out var nextLocal);

            rt.anchoredPosition += (nextLocal - currLocal);
        }

        /// <summary>
        /// Static entry point used by server-side TargetRpc.
        /// </summary>
        public static void PostLootToast(string itemId, string itemName, int amount, string rarityHex, Sprite icon)
        {
            if (!Instance || !Instance.entryPrefab || !Instance.listRoot)
                return;

            Instance.Spawn(itemId, itemName, amount, rarityHex, icon);
        }

        void Spawn(string itemId, string itemName, int amount, string rarityHex, Sprite icon)
        {
            var go = Instantiate(entryPrefab, listRoot);
            var entry = go.GetComponent<LootToastEntry>();
            if (!entry) entry = go.AddComponent<LootToastEntry>();

            // Best-effort auto-wire if the fields weren’t set on the prefab
            entry.TryAutoWire();

            entry.Bind(itemName, amount, rarityHex, icon);
            entry.Play();

            if (clampToScreen) ClampWindowToScreen();
        }
    }

    /// <summary>
    /// Minimal toast entry behaviour.
    /// Prefab may pre-wire these, or we'll auto-find by common names at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class LootToastEntry : MonoBehaviour
    {
        [Header("Optional Wiring")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private CanvasGroup group;

        [Header("Timing")]
        [SerializeField] private float showSeconds = 2.0f;
        [SerializeField] private float fadeSeconds = 0.4f;
        [SerializeField] private float risePixels = 24f;

        RectTransform _rt;
        Vector2 _startPos;

        public void TryAutoWire()
        {
            _rt = transform as RectTransform;
            if (!group) group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            // Heuristics: look for children named commonly, else first match.
            if (!iconImage) iconImage = FindByName<Image>("Icon") ?? GetComponentInChildren<Image>(true);
            if (!nameText) nameText = FindByName<TMP_Text>("Name") ?? GetComponentInChildren<TMP_Text>(true);
            if (!amountText) amountText = FindByName<TMP_Text>("Amount");  // optional; null is fine

            T FindByName<T>(string contains) where T : Component
            {
                var comps = GetComponentsInChildren<T>(true);
                foreach (var c in comps)
                    if (c && c.name.ToLower().Contains(contains.ToLower()))
                        return c;
                return null;
            }
        }

        /// <summary>Populate UI. (No sizing — style it in the prefab.)</summary>
        public void Bind(string itemName, int amount, string rarityHex, Sprite icon)
        {
            if (iconImage)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            // Clean item name: replace '-' and '_' with spaces
            string cleanName = CleanName(itemName);

            if (nameText)
                nameText.text = string.IsNullOrEmpty(rarityHex)
                    ? cleanName
                    : $"<color={NormalizeHex(rarityHex)}>{cleanName}</color>";

            if (amountText)
                amountText.text = amount > 1 ? $"x{amount}" : string.Empty;
        }

        /// <summary>Simple rise + fade coroutine.</summary>
        public void Play()
        {
            if (!_rt) _rt = transform as RectTransform;
            _startPos = _rt.anchoredPosition;
            if (!group) group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            StopAllCoroutines();
            StartCoroutine(CoRun());
        }

        System.Collections.IEnumerator CoRun()
        {
            // Ensure visible
            group.alpha = 1f;

            // Hold
            float t = 0f;
            while (t < showSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }

            // Fade + rise
            t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / fadeSeconds);
                if (_rt) _rt.anchoredPosition = _startPos + new Vector2(0f, k * risePixels);
                group.alpha = 1f - k;
                yield return null;
            }

            // Done — destroy or pool as you prefer
            Destroy(gameObject);
        }

        static string NormalizeHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "#FFFFFF";
            return hex[0] == '#' ? hex : "#" + hex;
        }

        static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace('-', ' ').Replace('_', ' ');
            // collapse any accidental double spaces
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ").Trim();
        }
    }
}
