using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LootToastRowLayout : MonoBehaviour
{
    [Header("Refs")]
    public Image icon;
    public TMP_Text nameText;
    public TMP_Text countText;

    LayoutElement nameLE, countLE;
    HorizontalLayoutGroup hlg;

    void Awake()
    {
        hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg)
        {
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;
        }

        nameLE = EnsureLE(nameText);
        countLE = EnsureLE(countText);

        // Name is the only flexible child and will ellipsize.
        if (nameLE) { nameLE.flexibleWidth = 1; nameLE.minWidth = 0; nameLE.layoutPriority = 0; }
        if (nameText)
        {
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Count keeps its width.
        if (countLE) { countLE.flexibleWidth = 0; countLE.layoutPriority = 1; }
        if (countText) countText.alignment = TextAlignmentOptions.MidlineRight;
    }

    public void Set(Sprite iconSprite, string itemName, string count)
    {
        if (icon) { icon.sprite = iconSprite; icon.enabled = iconSprite != null; }
        if (nameText) nameText.text = itemName ?? "";
        if (countText) countText.text = count ?? "";

        UpdateCountMinWidth();
        // Rebuild once so the clamp happens immediately
        LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
    }

    void OnEnable() => UpdateCountMinWidth();

    void UpdateCountMinWidth()
    {
        if (!countText || !countLE) return;
        float w = countText.GetPreferredValues(countText.text, 0, 0).x;
        countLE.minWidth = Mathf.Ceil(w) + 4f;   // small gutter
    }

    static LayoutElement EnsureLE(TMP_Text t)
        => t ? (t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>()) : null;
}
