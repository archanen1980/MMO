using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(GridLayoutGroup))]
public class AutoGridByColumns : MonoBehaviour
{
    public int columns = 7;
    public bool keepSquare = true;

    GridLayoutGroup grid;
    RectTransform rt;

    void OnEnable() { Apply(); }
    void OnRectTransformDimensionsChange() { Apply(); }

    void Apply()
    {
        if (!grid) grid = GetComponent<GridLayoutGroup>();
        if (!rt) rt = GetComponent<RectTransform>();
        if (columns < 1) return;

        var pad = grid.padding;
        float w = rt.rect.width - pad.left - pad.right - grid.spacing.x * (columns - 1);
        if (w <= 0) return;

        float cell = Mathf.Floor(w / columns);
        if (keepSquare) grid.cellSize = new Vector2(cell, cell);
        else grid.cellSize = new Vector2(cell, grid.cellSize.y);
    }
}
