using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Economy.UI
{
    /// Attach to your "CurrencyMessageBox" root (the same GO that has CanvasGroup).
    /// Assign scrollRect, content, and entryPrefab.
    public class CurrencyMessageBox : MonoBehaviour
    {
        public static CurrencyMessageBox Instance { get; private set; }

        [Header("Wiring")]
        [SerializeField] ScrollRect scrollRect;
        [SerializeField] RectTransform content;
        [SerializeField] CurrencyMessageEntry entryPrefab;

        [Header("Behavior")]
        [SerializeField] int maxEntries = 50;
        [SerializeField] bool autoScrollToBottom = true;
        [SerializeField] float showSeconds = 3.0f;
        [SerializeField] float fadeSeconds = 0.35f;

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Post(string text, Sprite icon = null, Color? color = null)
        {
            if (!entryPrefab || !content) return;

            var entry = Instantiate(entryPrefab, content);
            entry.Setup(text, icon, color ?? Color.white);
            entry.BeginAutoFade(showSeconds, fadeSeconds);

            // prune old
            while (content.childCount > maxEntries)
                Destroy(content.GetChild(0).gameObject);

            if (autoScrollToBottom && scrollRect)
                Canvas.ForceUpdateCanvases(); // ensure size updated before scrolling
            scrollRect.verticalNormalizedPosition = 0f;
        }

        public void PostCurrency(MMO.Economy.CurrencyDef def, long delta)
        {
            if (!def) return;

            string sign = delta >= 0 ? "+" : "-";
            long abs = System.Math.Abs(delta);
            string body = def.isDenominated
                ? MMO.Economy.CurrencyMath.Format(def, abs)
                : (string.IsNullOrEmpty(def.shortCode) ? $"{abs} {def.displayName}" : $"{abs} {def.shortCode}");

            var color = delta >= 0 ? new Color(0.49f, 1f, 0.49f) : new Color(1f, 0.47f, 0.47f);
            Post(sign + body, def.icon, color);
        }
    }
}
