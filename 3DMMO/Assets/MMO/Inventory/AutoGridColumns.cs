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

    float _lastW = -1f;
    int _lastCols = -1;
    Vector2 _lastCell;

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
        var newCell = keepSquare ? new Vector2(cell, cell) : new Vector2(cell, grid.cellSize.y);

        // short-circuit if nothing effectively changed
        if (Mathf.Approximately(_lastW, w) && _lastCols == columns && (Vector2)grid.cellSize == newCell)
            return;

        grid.cellSize = newCell;
        _lastW = w; _lastCols = columns; _lastCell = newCell;
    }
}
