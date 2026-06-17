namespace CleanScope.App.Wpf.Common;

/// <summary>一个 treemap 矩形 (索引回指原始子项 + 像素坐标)。</summary>
public readonly record struct TreemapTile(int Index, double X, double Y, double W, double H);

/// <summary>
/// Squarified Treemap 布局 (Bruls 等, 2000)。把一组权重铺满给定矩形, 力求每块接近正方形,
/// 便于肉眼比较大小。纯函数, 无 UI 依赖。
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapTile> Squarify(IReadOnlyList<long> weights, double width, double height)
    {
        var result = new List<TreemapTile>();
        if (width <= 0 || height <= 0) return result;

        var items = weights
            .Select((w, i) => (Index: i, Weight: (double)w))
            .Where(t => t.Weight > 0)
            .OrderByDescending(t => t.Weight)
            .ToList();
        if (items.Count == 0) return result;

        var total = items.Sum(t => t.Weight);
        var scale = (width * height) / total;            // 权重 → 像素面积
        var areas = items.Select(t => (t.Index, Area: t.Weight * scale)).ToList();

        double x = 0, y = 0, w = width, h = height;
        var row = new List<(int Index, double Area)>();
        var pos = 0;

        while (pos < areas.Count)
        {
            var side = Math.Min(w, h);
            var candidate = areas[pos];
            if (row.Count == 0 || Worst(row, candidate.Area, side) <= Worst(row, null, side))
            {
                row.Add(candidate);
                pos++;
            }
            else
            {
                LayoutRow(row, ref x, ref y, ref w, ref h, result);
                row.Clear();
            }
        }
        if (row.Count > 0) LayoutRow(row, ref x, ref y, ref w, ref h, result);
        return result;
    }

    // 行内最差长宽比 (越小越接近正方形)。side = 该行铺设方向的边长。
    private static double Worst(List<(int Index, double Area)> row, double? extra, double side)
    {
        double sum = row.Sum(r => r.Area) + (extra ?? 0);
        if (sum <= 0 || side <= 0) return double.PositiveInfinity;
        double max = row.Count == 0 ? 0 : row.Max(r => r.Area);
        double min = row.Count == 0 ? double.MaxValue : row.Min(r => r.Area);
        if (extra is { } e) { max = Math.Max(max, e); min = Math.Min(min, e); }
        var s2 = sum * sum;
        var side2 = side * side;
        return Math.Max(side2 * max / s2, s2 / (side2 * min));
    }

    private static void LayoutRow(
        List<(int Index, double Area)> row,
        ref double x, ref double y, ref double w, ref double h,
        List<TreemapTile> result)
    {
        var rowSum = row.Sum(r => r.Area);
        if (w >= h)
        {
            var colW = rowSum / h;                       // 在左侧竖排一列
            var cy = y;
            foreach (var item in row)
            {
                var ih = colW > 0 ? item.Area / colW : 0;
                result.Add(new TreemapTile(item.Index, x, cy, colW, ih));
                cy += ih;
            }
            x += colW; w -= colW;
        }
        else
        {
            var rowH = rowSum / w;                        // 在顶部横排一行
            var cx = x;
            foreach (var item in row)
            {
                var iw = rowH > 0 ? item.Area / rowH : 0;
                result.Add(new TreemapTile(item.Index, cx, y, iw, rowH));
                cx += iw;
            }
            y += rowH; h -= rowH;
        }
    }
}
