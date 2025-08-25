// Assets/MMO/Loot/UI/LootToastItemView.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MMO.Shared.Item;   // ItemDef
using MMO.Inventory.UI; // ItemTooltipComposer for rarity color/name

namespace MMO.Loot.UI
{
    /// <summary>
    /// Visual row for a single loot toast (icon + name + amount).
    /// Handles appear/hold/fade animation and rarity coloring.
    /// </summary>
    public class LootToastItemView : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private RectTransform animRoot; // optional: child moved during anim; if null uses this RectTransform
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private Image rarityStripe;     // optional thin stripe or background accent
        [SerializeField] private CanvasGroup group;      // if null, auto-add

        [Header("Visual Tweaks")]
        [Tooltip("Multiplies the rarity color for the stripe (0..1).")]
        [SerializeField, Range(0f, 1f)] private float stripeIntensity = 0.9f;

        Coroutine _co;

        void Reset()
        {
            group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            animRoot = animRoot ? animRoot : (RectTransform)transform;
        }

        void Awake()
        {
            if (!group) group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (!animRoot) animRoot = transform as RectTransform;
        }

        /// <summary>Starts the toast animation. Caller provides onFinished callback for pooling.</summary>
        public void Play(ItemDef def, int amount, float hold, float fade, float fadeRise, float appearOffset, System.Action onFinished)
        {
            if (_co != null) StopCoroutine(_co);

            // Populate visuals (rarity color & name via composer)
            var payload = ItemTooltipComposer.Build(def); // Title has <color=...>Name</color> (no underline)
            if (icon)
            {
                icon.sprite = def ? def.icon : null;
                icon.enabled = icon.sprite != null;
            }
            if (nameText)
            {
                nameText.richText = true;
                nameText.text = payload.Title; // already wrapped in <color=rarity>
            }
            if (amountText)
            {
                amountText.text = amount > 1 ? $"x{amount}" : string.Empty;
                amountText.enabled = amount > 1;
            }
            if (rarityStripe)
            {
                var c = payload.RarityColor;
                c.a *= stripeIntensity;
                rarityStripe.color = c;
                rarityStripe.enabled = true;
            }

            gameObject.SetActive(true);
            _co = StartCoroutine(RunAnim(hold, fade, fadeRise, appearOffset, onFinished));
        }

        public void ForceFinish()
        {
            if (_co != null) { StopCoroutine(_co); _co = null; }
        }

        IEnumerator RunAnim(float hold, float fade, float fadeRise, float appearOffset, System.Action onFinish)
        {
            // Ensure components
            if (!group) group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (!animRoot) animRoot = transform as RectTransform;

            // Appear: slide up from offset and fade in
            float t = 0f;
            Vector2 basePos = animRoot.anchoredPosition;
            group.alpha = 0f;
            animRoot.anchoredPosition = basePos + new Vector2(0f, -Mathf.Abs(appearOffset));

            const float appearTime = 0.25f;
            while (t < appearTime)
            {
                t += Time.unscaledDeltaTime; // unaffected by game pause
                float a = Mathf.Clamp01(t / appearTime);
                group.alpha = a;
                animRoot.anchoredPosition = Vector2.Lerp(basePos + new Vector2(0, -Mathf.Abs(appearOffset)), basePos, Smooth(a));
                yield return null;
            }
            group.alpha = 1f;
            animRoot.anchoredPosition = basePos;

            // Hold
            float holdT = 0f;
            while (holdT < Mathf.Max(0f, hold))
            {
                holdT += Time.unscaledDeltaTime;
                yield return null;
            }

            // Fade out + slight rise
            float fadeT = 0f;
            Vector2 fadeStart = animRoot.anchoredPosition;
            Vector2 fadeEnd = fadeStart + new Vector2(0f, Mathf.Abs(fadeRise));
            while (fadeT < Mathf.Max(0.01f, fade))
            {
                fadeT += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(fadeT / Mathf.Max(0.01f, fade));
                group.alpha = 1f - a;
                animRoot.anchoredPosition = Vector2.Lerp(fadeStart, fadeEnd, Smooth(a));
                yield return null;
            }
            group.alpha = 0f;

            _co = null;
            onFinish?.Invoke();
        }

        static float Smooth(float x) => x * x * (3f - 2f * x); // smoothstep
    }
}
