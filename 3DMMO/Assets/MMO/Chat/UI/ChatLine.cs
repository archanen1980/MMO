// Assets/MMO/Chat/UI/ChatLine.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Chat.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ChatLine : MonoBehaviour
    {
        [SerializeField] TMP_Text text;          // will auto-find if not assigned
        [SerializeField] float verticalPadding = 2f; // extra pixels to avoid clipping

        LayoutElement _le;
        RectTransform _rt;

        void Awake()
        {
            //text.maskable = true;

            EnsureRefs();
            PrepareTextDefaults();
        }

        void OnValidate()
        {
            if (!Application.isPlaying) EnsureRefs();
        }

        void EnsureRefs()
        {
            if (!_rt) _rt = GetComponent<RectTransform>();
            if (!_le) _le = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            if (!text) text = GetComponentInChildren<TMP_Text>(true);
        }

        void PrepareTextDefaults()
        {
            if (!text) return;
            text.enabled = true;
            text.richText = true;
            text.enableWordWrapping = true;
            text.enableAutoSizing = false; // important: stable height
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.maskable = true;
            // visible default color if prefab color was transparent
            if (text.color.a <= 0.001f)
                text.color = new Color32(0xE8, 0xED, 0xF2, 0xFF);
        }

        public void Set(string richText)
        {
            EnsureRefs();
            PrepareTextDefaults();

            if (!text) return;

            text.text = string.IsNullOrEmpty(richText) ? " " : richText;

            // Try immediate size using current width; if width=0, size next frame.
            if (!RefreshHeightImmediate())
                StartCoroutine(RefreshHeightNextFrame());
        }

        /// Try to size right now. Returns false if width is unknown (0), so we try again next frame.
        bool RefreshHeightImmediate()
        {
            float width = text.rectTransform.rect.width;
            if (width <= 0.5f) return false;

            text.ForceMeshUpdate();
            // Ask TMP what height it wants at this width
            var pref = text.GetPreferredValues(text.text, width, Mathf.Infinity);
            float h = Mathf.Ceil(pref.y + verticalPadding);

            _le.minHeight = _le.preferredHeight = h;
            return true;
        }

        IEnumerator RefreshHeightNextFrame()
        {
            // Wait a frame for layout/ScrollRect to set widths
            yield return null;

            // Do another immediate pass now that width should be valid
            if (!RefreshHeightImmediate())
            {
                // As a last resort, one more frame (rare)
                yield return null;
                RefreshHeightImmediate();
            }

            // Also rebuild parent content so items don’t overlay
            var parent = _rt.parent as RectTransform;
            if (parent) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }
    }
}
