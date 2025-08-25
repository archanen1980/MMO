using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MMO.Loot.UI
{
    /// Attach this to your loot toast row (the object that holds the icon + one TMP_Text).
    /// Layout: [Icon(Image)] [TextMeshProUGUI oneLineText]
    /// - The TMP text is the only flexible child in a HorizontalLayoutGroup.
    /// - This script composes "Name   × N" into a single TMP text, ellipsizing the name
    ///   to make sure the count is always fully visible on the right.
    [RequireComponent(typeof(RectTransform))]
    public class LootToastSingleText : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] Image icon;                  // optional
        [SerializeField] TMP_Text oneLineText;        // required (single text field)

        [Header("Style")]
        [SerializeField] bool boldName = true;
        [SerializeField] string rarityHex = "#FFFFFF"; // set per item if you want
        [SerializeField] string countColorHex = "#FFFFFF";
        [SerializeField] float gapPixels = 16f;       // visual gap between name and count

        // cached inputs for recomposition on resize
        string _nameRaw;
        int _amount;
        Sprite _iconSprite;

        void Reset()
        {
            icon = GetComponentInChildren<Image>();
            oneLineText = GetComponentInChildren<TMP_Text>();
        }

        void Awake()
        {
            if (oneLineText)
            {
                oneLineText.enableWordWrapping = false;
                oneLineText.overflowMode = TextOverflowModes.Truncate;
                oneLineText.richText = true;
            }
        }

        /// Call this when you spawn/update the toast.
        public void Set(Sprite iconSprite, string itemName, int amount, string rarityHexHtml = null)
        {
            _iconSprite = iconSprite;
            _nameRaw = itemName ?? string.Empty;
            _amount = Mathf.Max(1, amount);
            if (!string.IsNullOrEmpty(rarityHexHtml)) rarityHex = rarityHexHtml;

            if (icon)
            {
                icon.sprite = _iconSprite;
                icon.enabled = _iconSprite != null;
            }

            Compose();
        }

        // If the row width changes (screen resize, layout change), recompose.
        void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled && oneLineText)
                Compose();
        }

        void Compose()
        {
            if (!oneLineText) return;

            // Build & measure the count segment (reserve its width).
            string countSeg = $"<color={countColorHex}>× {_amount}</color>";
            float countW = oneLineText.GetPreferredValues(countSeg, 0, 0).x;

            // Fixed pixel gap between name and count (TMP <space=> uses pixels).
            string gapSeg = $"<space={Mathf.Max(0f, gapPixels)}>";
            float gapW = gapPixels;

            // Available width for the entire text box.
            float totalW = (oneLineText.rectTransform.rect.width > 0f)
                         ? oneLineText.rectTransform.rect.width
                         : oneLineText.GetPreferredValues("W", 0, 0).x * 20f; // conservative fallback

            // Allot space for the name.
            float maxNameW = Mathf.Max(0f, totalW - countW - gapW);

            // Styled name wrappers (affect width, so include when measuring).
            string styledNamePrefix = $"<color={rarityHex}>" + (boldName ? "<b>" : "");
            string styledNameSuffix = (boldName ? "</b>" : "") + "</color>";

            // Ellipsize the raw name until the styled result fits.
            string finalName = FitWithEllipsis(_nameRaw, styledNamePrefix, styledNameSuffix, maxNameW, oneLineText);

            // Compose final line.
            oneLineText.text = $"{styledNamePrefix}{finalName}{styledNameSuffix}{gapSeg}{countSeg}";
        }

        /// Returns a version of 'raw' that fits within 'maxWidth' when wrapped with styledPrefix/suffix.
        static string FitWithEllipsis(string raw, string styledPrefix, string styledSuffix,
                                      float maxWidth, TMP_Text measurer)
        {
            if (string.IsNullOrEmpty(raw) || maxWidth <= 0f) return "…";

            // Fast path.
            string styled = styledPrefix + raw + styledSuffix;
            if (measurer.GetPreferredValues(styled, 0, 0).x <= maxWidth) return raw;

            // Binary search the largest prefix that fits when adding an ellipsis.
            int lo = 0, hi = raw.Length;
            const string ell = "…";

            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                string candidate = styledPrefix + raw.Substring(0, mid) + ell + styledSuffix;
                float w = measurer.GetPreferredValues(candidate, 0, 0).x;
                if (w <= maxWidth) lo = mid;
                else hi = mid - 1;
            }

            if (lo <= 0) return ell;
            return raw.Substring(0, lo) + ell;
        }

        // Optional helpers if you want to change colors at runtime:
        public void SetRarityColor(Color c) => rarityHex = "#" + ColorUtility.ToHtmlStringRGB(c);
        public void SetCountColor(Color c) => countColorHex = "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
