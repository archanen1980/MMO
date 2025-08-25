using UnityEngine;
using TMPro;
using UnityEngine.UI;
using MMO.Shared.Item; // ItemDef for icon resolution

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

        void Awake()
        {
            Instance = this;
            if (!listRoot) listRoot = transform as RectTransform;
        }

        /// <summary>
        /// Static entry point used by server-side TargetRpc.
        /// No sprite is passed over the network; we resolve icon on the client via Resources/Items.
        /// </summary>
        public static void PostLootToast(string itemId, string itemName, int amount, string rarityHex)
        {
            if (!Instance || !Instance.entryPrefab || !Instance.listRoot)
                return;

            var icon = ResolveIcon(itemId);
            Instance.Spawn(itemName, amount, rarityHex, icon);
        }

        static Sprite ResolveIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            // Try direct Resources path: Resources/Items/<itemId>
            var def = Resources.Load<ItemDef>($"Items/{itemId}");
            if (def && def.icon) return def.icon;

            // Fallback: scan all Items for matching def.itemId (case-insensitive)
            var all = Resources.LoadAll<ItemDef>("Items");
            foreach (var d in all)
                if (d && !string.IsNullOrEmpty(d.itemId) &&
                    string.Equals(d.itemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    return d.icon;

            return null;
        }

        void Spawn(string itemName, int amount, string rarityHex, Sprite icon)
        {
            var go = Instantiate(entryPrefab, listRoot);
            var entry = go.GetComponent<LootToastEntry>();
            if (!entry) entry = go.AddComponent<LootToastEntry>();

            entry.TryAutoWire();
            entry.Bind(itemName, amount, rarityHex, icon);
            entry.Play();
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

            if (nameText)
                nameText.text = string.IsNullOrEmpty(rarityHex)
                    ? itemName
                    : $"<color={NormalizeHex(rarityHex)}>{itemName}</color>";

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

            Destroy(gameObject);
        }

        static string NormalizeHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "#FFFFFF";
            return hex[0] == '#' ? hex : "#" + hex;
        }
    }
}
