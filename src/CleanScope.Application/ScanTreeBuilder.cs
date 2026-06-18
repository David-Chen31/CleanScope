using CleanScope.Domain.Models;

namespace CleanScope.Application;

/// <summary>
/// 从已分类的目录 <see cref="DecisionItem"/> 重建全盘目录树 (P1)。按路径祖先关系挂接,
/// 同级按大小降序。纯函数, 供资源管理器树视图浏览整盘"空间去哪了 + 各是什么"。
/// </summary>
public static class ScanTreeBuilder
{
    public static ScanTreeNode Build(string targetPath, IReadOnlyList<DecisionItem> dirItems, long totalSize)
    {
        ArgumentNullException.ThrowIfNull(dirItems);

        var nodes = new Dictionary<string, ScanTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirItems)
            nodes[d.Path] = ToNode(d);

        var paths = new HashSet<string>(nodes.Keys, StringComparer.OrdinalIgnoreCase);
        var roots = new List<ScanTreeNode>();
        foreach (var d in dirItems)
        {
            var ancestor = NearestAncestorInSet(d.Path, paths);
            if (ancestor is not null && !string.Equals(ancestor, d.Path, StringComparison.OrdinalIgnoreCase))
                nodes[ancestor].Children.Add(nodes[d.Path]);
            else
                roots.Add(nodes[d.Path]);
        }

        // 扫描目标本身若是节点 (如 C:\), 直接作根; 否则合成一个根把顶层节点挂上。
        var root = roots.Count == 1 && string.Equals(roots[0].Path, targetPath, StringComparison.OrdinalIgnoreCase)
            ? roots[0]
            : Synthesize(targetPath, totalSize, roots);

        SortBySizeDescending(root);
        return root;
    }

    private static ScanTreeNode ToNode(DecisionItem d) => new(
        d.Path, LeafName(d.Path), d.Size, d.RiskLevel, d.IsContainer,
        isCleanable: !d.IsContainer && d.RiskLevel is RiskLevel.A or RiskLevel.B,
        origin: d.Origin ?? d.OwnerApp ?? "未知来源",
        purpose: d.Explanation,
        recommendedAction: d.RecommendedAction);

    private static ScanTreeNode Synthesize(string targetPath, long totalSize, List<ScanTreeNode> roots)
    {
        var size = totalSize > 0 ? totalSize : roots.Sum(n => n.Size);
        var root = new ScanTreeNode(targetPath, LeafName(targetPath), size, RiskLevel.C,
            isContainer: true, isCleanable: false, origin: "扫描根", purpose: "本次扫描的根目录", recommendedAction: "展开浏览");
        root.Children.AddRange(roots);
        return root;
    }

    private static void SortBySizeDescending(ScanTreeNode node)
    {
        if (node.Children.Count == 0) return;
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var c in node.Children) SortBySizeDescending(c);
    }

    private static string LeafName(string path)
    {
        var s = path.TrimEnd('\\');
        var i = s.LastIndexOf('\\');
        return i >= 0 && i + 1 < s.Length ? s[(i + 1)..] : s;
    }

    private static string? NearestAncestorInSet(string path, HashSet<string> set)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            if (set.Contains(dir)) return dir;
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (string.Equals(parent, dir, StringComparison.Ordinal)) break;
            dir = parent;
        }
        return null;
    }
}
