using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(GridLayoutGroup))]
public class AutoGridCellSize : MonoBehaviour
{
    public int columns = 2;
    public int rows = 4;
    public bool keepSquare = true;

    GridLayoutGroup grid;
    RectTransform rt;

    void OnEnable() { Apply(); }
    void OnRectTransformDimensionsChange() { Apply(); }

    void Apply()
    {
        if (!grid) grid = GetComponent<GridLayoutGroup>();
        if (!rt) rt = GetComponent<RectTransform>();
        if (columns < 1 || rows < 1) return;

        var pad = grid.padding;
        float w = rt.rect.width - pad.left - pad.right - grid.spacing.x * (columns - 1);
        float h = rt.rect.height - pad.top - pad.bottom - grid.spacing.y * (rows - 1);
        if (w <= 0 || h <= 0) return;

        float cellW = w / columns;
        float cellH = h / rows;

        if (keepSquare)
        {
            float s = Mathf.Floor(Mathf.Min(cellW, cellH));
            grid.cellSize = new Vector2(s, s);
        }
        else grid.cellSize = new Vector2(Mathf.Floor(cellW), Mathf.Floor(cellH));
    }
}
