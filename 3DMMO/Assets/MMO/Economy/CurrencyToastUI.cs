using System.Collections;
using UnityEngine;
using TMPro;

namespace MMO.Economy.UI
{
    public class CurrencyToastUI : MonoBehaviour
    {
        [Header("Where to spawn toasts")]
        public RectTransform container;       // VerticalLayoutGroup recommended
        public GameObject toastPrefab;        // a prefab with TMP_Text somewhere
        public float showSeconds = 2f;
        public float fadeSeconds = 0.35f;

        public void Show(string text)
        {
            if (!container || !toastPrefab) return;
            var go = Instantiate(toastPrefab, container);
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp) tmp.text = text;

            // make it ignore raycasts
            foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                g.raycastTarget = false;

            StartCoroutine(FadeOutAndDestroy(go));
        }

        IEnumerator FadeOutAndDestroy(GameObject go)
        {
            yield return new WaitForSecondsRealtime(showSeconds);
            var group = go.GetComponent<CanvasGroup>();
            if (!group) group = go.AddComponent<CanvasGroup>();
            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / fadeSeconds);
                yield return null;
            }
            Destroy(go);
        }
    }
}
