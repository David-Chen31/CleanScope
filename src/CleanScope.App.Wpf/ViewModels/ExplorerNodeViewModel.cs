using System.Collections.ObjectModel;
using System.Windows.Media;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 资源管理器树的一个节点 (P1)。包裹 <see cref="ScanTreeNode"/>, 提供大小条、四桶配色、
/// 来源/用途, 惰性展开 (展开时才把子节点物化为 VM, 大树也轻), 及右键操作 (E3: 复制/打开/移入回收站)。
/// </summary>
public sealed class ExplorerNodeViewModel : ViewModelBase
{
    private readonly ScanTreeNode _node;
    private readonly long _parentSize;
    private readonly bool _withinCleanable;   // 处在某个可清理祖先(如缓存目录)之下 → 随父清理
    private readonly IExplorerActions? _actions;
    private bool _childrenBuilt;

    public ExplorerNodeViewModel(ScanTreeNode node, long parentSize, bool withinCleanable = false,
        IExplorerActions? actions = null)
    {
        _node = node;
        _parentSize = parentSize > 0 ? parentSize : node.Size;
        _withinCleanable = withinCleanable;
        _actions = actions;
        Children = new ObservableCollection<ExplorerNodeViewModel>();
        if (node.HasChildren) Children.Add(Placeholder);   // 占位, 展开时替换为真实子节点

        CopyPathCommand = new RelayCommand(_ => _actions?.CopyToClipboard(Path, "已复制路径"), _ => HasPath);
        CopyPurposeCommand = new RelayCommand(
            _ => _actions?.CopyToClipboard(string.IsNullOrWhiteSpace(Purpose) ? RecommendedAction : Purpose, "已复制用途说明"),
            _ => !string.IsNullOrWhiteSpace(Purpose) || !string.IsNullOrWhiteSpace(RecommendedAction));
        OpenLocationCommand = new AsyncRelayCommand(_ => _actions?.OpenLocationAsync(Path) ?? Task.CompletedTask, _ => HasPath);
        RecycleCommand = new AsyncRelayCommand(_ => _actions?.RecycleAsync(this) ?? Task.CompletedTask, _ => CanRecycle);
    }

    // 有效可清理: 自身可清理, 或处在可清理祖先之下 (缓存目录里的子项也随父一起清, 颜色应一致)。
    private bool EffectiveCleanable => _node.IsCleanable || _withinCleanable;

    // 展开时惰性物化子节点 (含"余量"合成块)。
    public ObservableCollection<ExplorerNodeViewModel> Children { get; }

    public RelayCommand CopyPathCommand { get; }
    public RelayCommand CopyPurposeCommand { get; }
    public AsyncRelayCommand OpenLocationCommand { get; }
    public AsyncRelayCommand RecycleCommand { get; }

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
    public bool IsCleanable => EffectiveCleanable;
    public bool IsRemainder { get; private init; }

    private bool HasPath => !string.IsNullOrEmpty(_node.Path);
    // 真实文件/目录 (有路径、非余量合成块) 才允许尝试移入回收站; 是否放行由安全闸门当场判定。
    public bool CanRecycle => HasPath && !IsRemainder && !_isDeleted;

    // 已移入回收站: UI 置灰 + 删除线, 给即时反馈。
    private bool _isDeleted;
    public bool IsDeleted
    {
        get => _isDeleted;
        private set { if (SetField(ref _isDeleted, value)) { OnPropertyChanged(nameof(CanRecycle)); RecycleCommand.RaiseCanExecuteChanged(); } }
    }

    public void MarkDeleted() => IsDeleted = true;

    // 送交安全闸门的风险快照: 处在可清理祖先下的子项按"可清理(B)"评估, 与绿色显示一致;
    // 其余用本节点真实等级。黑名单/容器/占用仍由闸门独立按路径强制 (不受此影响)。
    internal RiskAssessment ToRiskAssessment()
    {
        var level = EffectiveCleanable && !_node.IsContainer ? RiskLevel.B : _node.RiskLevel;
        return new RiskAssessment(0, 0, level, 0, Array.Empty<string>(), new long[] { 0 },
            CanDeleteDirectly: false, Confidence: null, CreatedAt: DateTime.UtcNow, IsContainer: _node.IsContainer);
    }

    // 文件 vs 目录 (图标区分): 目录📁 / 文件📄。余量块视作文件聚合。
    public bool IsDirectory => _node.IsDirectory && !IsRemainder;
    public string Glyph => IsDirectory ? "📁" : "📄";

    // 四桶配色 (与列表一致): 容器/可清理/谨慎/勿动。处在可清理祖先下的子项统一显示为可清理(绿)。
    public CleanupBucket Bucket =>
        EffectiveCleanable && !_node.IsContainer ? CleanupBucket.Cleanable
                                                 : Buckets.Of(_node.IsContainer, _node.RiskLevel);
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
        // 处在可清理祖先下 → 子项随父清理 (颜色一致); 容器自身不下传可清理。
        var childWithinCleanable = EffectiveCleanable && !_node.IsContainer;
        foreach (var c in _node.Children)
            Children.Add(new ExplorerNodeViewModel(c, _node.Size, childWithinCleanable, _actions));

        // 余量 (直接文件 + 被剪枝的小目录): 仅当确有展开出的真实子项、且余量显著时才显示, 避免噪点。
        // 没有真实子项时不显示余量 —— 否则会冒出一条与本目录等大、可被反复展开的"（其它文件/小目录）"。
        var r = _node.Remainder;
        if (_node.HasChildren && r > 1_000_000 && r > _node.Size * 0.02)
            Children.Add(Remainder(r, _node.Size, _node.Origin, childWithinCleanable, _actions));
    }

    private static readonly ScanTreeNode PlaceholderNode =
        new("", "…", 0, RiskLevel.C, false, false, "", null, "");
    private static ExplorerNodeViewModel Placeholder => new(PlaceholderNode, 1);

    // 余量节点恒为叶子 (无子项 → 不可展开), 来源继承父目录 (这些文件属于同一目录, 归属一致)。
    private static ExplorerNodeViewModel Remainder(long size, long parentSize, string parentOrigin,
        bool withinCleanable, IExplorerActions? actions)
    {
        var node = new ScanTreeNode("", "（本目录其它文件）", size, RiskLevel.C,
            isContainer: false, isCleanable: false,
            origin: string.IsNullOrWhiteSpace(parentOrigin) ? "未知来源" : parentOrigin,
            purpose: "本目录下未单独列出的直接文件与小于阈值的子目录",
            recommendedAction: "在系统资源管理器中打开本目录查看", isDirectory: false);
        return new ExplorerNodeViewModel(node, parentSize, withinCleanable, actions) { IsRemainder = true };
    }
}
