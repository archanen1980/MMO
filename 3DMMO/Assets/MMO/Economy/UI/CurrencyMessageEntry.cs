using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Economy.UI
{
    public class CurrencyMessageEntry : MonoBehaviour
    {
        [SerializeField] Image icon;
        [SerializeField] TMP_Text label;

        public void Setup(string text, Sprite s, Color color)
        {
            if (label) { label.text = text; label.color = color; }
            if (icon)
            {
                icon.sprite = s;
                icon.enabled = s != null;
            }
            // never block pointer
            foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        public void BeginAutoFade(float showSeconds, float fadeSeconds)
        {
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(showSeconds, fadeSeconds));
        }

        IEnumerator FadeRoutine(float show, float fade)
        {
            if (show > 0) yield return new WaitForSecondsRealtime(show);
            var group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < fade)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
