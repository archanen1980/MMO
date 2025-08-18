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

    Vector2 _lastCell = new Vector2(-1, -1);
    int _lastCols = -1, _lastRows = -1;
    Vector2 _lastRectSize;

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

        float cellW = Mathf.Floor(w / columns);
        float cellH = Mathf.Floor(h / rows);
        var newCell = keepSquare ? Vector2.one * Mathf.Floor(Mathf.Min(cellW, cellH))
                                 : new Vector2(cellW, cellH);

        // short-circuit if no effective change
        var rectSize = new Vector2(rt.rect.width, rt.rect.height);
        if (_lastCols == columns && _lastRows == rows && _lastCell == newCell && _lastRectSize == rectSize)
            return;

        grid.cellSize = newCell;
        _lastCell = newCell; _lastCols = columns; _lastRows = rows; _lastRectSize = rectSize;
    }
}
