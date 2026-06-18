using System.Collections.ObjectModel;
using System.Windows.Media;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 资源管理器树的一个节点 (P1)。包裹 <see cref="ScanTreeNode"/>, 提供大小条、四桶配色、
/// 来源/用途, 及惰性展开 (展开时才把子节点物化为 VM, 大树也轻)。
/// </summary>
public sealed class ExplorerNodeViewModel : ViewModelBase
{
    private readonly ScanTreeNode _node;
    private readonly long _parentSize;
    private bool _childrenBuilt;

    public ExplorerNodeViewModel(ScanTreeNode node, long parentSize)
    {
        _node = node;
        _parentSize = parentSize > 0 ? parentSize : node.Size;
        Children = new ObservableCollection<ExplorerNodeViewModel>();
        if (node.HasChildren) Children.Add(Placeholder);   // 占位, 展开时替换为真实子节点
    }

    // 展开时惰性物化子节点 (含"余量"合成块)。
    public ObservableCollection<ExplorerNodeViewModel> Children { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (SetField(ref _isExpanded, value) && value) BuildChildren(); }
    }

    public string Name => _node.Name;
    public string Path => _node.Path;
    public string SizeText => Format.HumanSize(_node.Size);
    public string Origin => _node.Origin;
    public string Purpose => _node.Purpose ?? "";
    public string RecommendedAction => _node.RecommendedAction;
    public bool IsCleanable => _node.IsCleanable;
    public bool IsRemainder { get; private init; }

    // 四桶配色 (与列表一致): 容器/可清理/谨慎/勿动。
    public CleanupBucket Bucket => Buckets.Of(_node.IsContainer, _node.RiskLevel);
    public string BucketLabel => Buckets.Label(Bucket);
    public Brush BucketBrush => Buckets.Brush(Bucket);

    /// <summary>占父目录百分比 (大小条宽度); 0–1。</summary>
    public double FractionOfParent => _parentSize > 0 ? Math.Min(1.0, (double)_node.Size / _parentSize) : 0;
    public string PercentText => $"{FractionOfParent * 100:0.#}%";

    private void BuildChildren()
    {
        if (_childrenBuilt) return;
        _childrenBuilt = true;
        Children.Clear();
        foreach (var c in _node.Children)
            Children.Add(new ExplorerNodeViewModel(c, _node.Size));

        // 余量 (直接文件 + 被剪枝的小目录): 仅当确有展开出的真实子项、且余量显著时才显示, 避免噪点。
        // 没有真实子项时不显示余量 —— 否则会冒出一条与本目录等大、可被反复展开的"（其它文件/小目录）"。
        var r = _node.Remainder;
        if (_node.HasChildren && r > 1_000_000 && r > _node.Size * 0.02)
            Children.Add(Remainder(r, _node.Size, _node.Origin));
    }

    private static readonly ScanTreeNode PlaceholderNode =
        new("", "…", 0, Domain.Enums.RiskLevel.C, false, false, "", null, "");
    private static ExplorerNodeViewModel Placeholder => new(PlaceholderNode, 1);

    // 余量节点恒为叶子 (无子项 → 不可展开), 来源继承父目录 (这些文件属于同一目录, 归属一致)。
    private static ExplorerNodeViewModel Remainder(long size, long parentSize, string parentOrigin)
    {
        var node = new ScanTreeNode("", "（本目录其它文件）", size, Domain.Enums.RiskLevel.C,
            isContainer: false, isCleanable: false,
            origin: string.IsNullOrWhiteSpace(parentOrigin) ? "未知来源" : parentOrigin,
            purpose: "本目录下未单独列出的直接文件与小于阈值的子目录",
            recommendedAction: "在系统资源管理器中打开本目录查看");
        return new ExplorerNodeViewModel(node, parentSize) { IsRemainder = true };
    }
}
