using CleanScope.App.Wpf.Common;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 空间地图的树节点 (S2)。由扫描得到的逐项 <see cref="FileRowViewModel"/> 按路径祖先关系重建,
/// 用于 treemap 矩形树图与目录下钻。聚合 Size 决定矩形面积, 风险决定颜色。
/// </summary>
public sealed class TreeNodeViewModel
{
    public TreeNodeViewModel(string name, string path, long size, RiskLevel? risk, FileRowViewModel? row)
    {
        Name = name;
        Path = path;
        Size = size;
        Risk = risk;
        Row = row;
    }

    public string Name { get; }
    public string Path { get; }
    public long Size { get; }
    public RiskLevel? Risk { get; }

    /// <summary>对应的分析行 (合成根/“未细分”余量节点为 null)。</summary>
    public FileRowViewModel? Row { get; }

    public List<TreeNodeViewModel> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsRemainder { get; init; }

    public string SizeText => Format.HumanSize(Size);
    public string Label => $"{Name}  ·  {SizeText}";
}

/// <summary>从扁平分析行重建目录树 (按路径前缀祖先关系)。纯函数。</summary>
public static class TreeBuilder
{
    public static TreeNodeViewModel Build(string targetPath, IReadOnlyList<FileRowViewModel> rows, long totalSize)
    {
        var nodes = new Dictionary<string, TreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            nodes[r.Path] = new TreeNodeViewModel(LeafName(r.Path), r.Path, r.Size, r.RiskLevel, r);

        var paths = new HashSet<string>(nodes.Keys, StringComparer.OrdinalIgnoreCase);
        var roots = new List<TreeNodeViewModel>();
        foreach (var r in rows)
        {
            var ancestor = NearestAncestorInSet(r.Path, paths);
            if (ancestor is not null && !string.Equals(ancestor, r.Path, StringComparison.OrdinalIgnoreCase))
                nodes[ancestor].Children.Add(nodes[r.Path]);
            else
                roots.Add(nodes[r.Path]);
        }

        // 合成根 (扫描目标), 把顶层节点挂上; 大小取根聚合 (totalSize) 以反映真实占用。
        var rootSize = totalSize > 0 ? totalSize : roots.Sum(n => n.Size);
        var root = new TreeNodeViewModel(targetPath, targetPath, rootSize, null, null);
        root.Children.AddRange(roots);

        SortAndAddRemainders(root);
        return root;
    }

    // 每个有子节点的节点: 子按大小降序; 若聚合 > 已知子之和, 补一个“未细分”余量块。
    private static void SortAndAddRemainders(TreeNodeViewModel node)
    {
        if (node.Children.Count == 0) return;
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        foreach (var c in node.Children.ToList()) SortAndAddRemainders(c);

        var childrenSum = node.Children.Sum(c => c.Size);
        var remainder = node.Size - childrenSum;
        // 仅当余量显著 (>5% 且 >1MB) 才显示, 避免噪点。
        if (remainder > 1_000_000 && remainder > node.Size * 0.05)
            node.Children.Add(new TreeNodeViewModel("(未细分)", node.Path, remainder, null, null) { IsRemainder = true });
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
