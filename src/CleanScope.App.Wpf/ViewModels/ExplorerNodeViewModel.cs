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
        OpenLocationCommand = new AsyncRelayCommand(_ => _actions?.OpenLocationAsync(Path) ?? Task.CompletedTask, _ => HasPath && !_isDeleted);
        RecycleCommand = new AsyncRelayCommand(_ => _actions?.RecycleAsync(this) ?? Task.CompletedTask, _ => CanRecycle);
        InvestigateCommand = new AsyncRelayCommand(_ => _actions?.InvestigateAsync(this) ?? Task.CompletedTask, _ => CanInvestigate);
        MigrateCommand = new AsyncRelayCommand(_ => _actions?.MigrateAsync(this) ?? Task.CompletedTask, _ => CanMigrate);
        OpenRecycleBinCommand = new AsyncRelayCommand(_ => _actions?.OpenRecycleBinAsync() ?? Task.CompletedTask, _ => _isDeleted);
    }

    // 有效可清理: 自身可清理, 或处在可清理祖先之下 (缓存目录里的子项也随父一起清, 颜色应一致)。
    private bool EffectiveCleanable => _node.IsCleanable || _withinCleanable;

    // 展开时惰性物化子节点 (含"余量"合成块)。
    public ObservableCollection<ExplorerNodeViewModel> Children { get; }

    public RelayCommand CopyPathCommand { get; }
    public RelayCommand CopyPurposeCommand { get; }
    public AsyncRelayCommand OpenLocationCommand { get; }
    public AsyncRelayCommand RecycleCommand { get; }
    public AsyncRelayCommand InvestigateCommand { get; }   // E5+: 按需用 AI 识别此项 (零默认开销)
    public AsyncRelayCommand MigrateCommand { get; }       // P0: 把此目录迁到其他盘 + 建目录联接
    public AsyncRelayCommand OpenRecycleBinCommand { get; }  // A2: 删除后唯一可用动作 —— 打开回收站查看/还原

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (SetField(ref _isExpanded, value) && value) BuildChildren(); }
    }

    public string Name => _node.Name;
    public string Path => _node.Path;
    public string SizeText => Format.HumanSize(_node.Size);
    // AI 按需识别的结果优先于确定性结论 (并明确标注"AI 推测", 仅展示, 不改判风险/删除)。
    public string Origin => _aiOrigin ?? _node.Origin;
    public string Purpose => _aiPurpose ?? _node.Purpose ?? "";
    public string RecommendedAction => _node.RecommendedAction;
    public bool IsCleanable => EffectiveCleanable;
    public bool IsRemainder { get; private init; }

    // —— E5+ 按需 AI 识别 ——
    private string? _aiOrigin;
    private string? _aiPurpose;
    private bool _aiResolved;
    public bool IsAiResolved { get => _aiResolved; private set { if (SetField(ref _aiResolved, value)) OnPropertyChanged(nameof(CanInvestigate)); } }

    // 仅 AI 已启用 + 真实项 + 尚未识别/识别中/已删 时可点; 未配置 AI 的用户菜单项不显示, 自然零开销。
    public bool ShowAiMenu => _actions?.AiEnabled == true;
    public bool CanInvestigate => ShowAiMenu && HasPath && !IsRemainder && !_aiResolved && !_isInvestigating && !_isDeleted;

    // A4: AI 识别中的瞬时态 —— 行内显示"✨识别中…", 期间禁用该动作, 让用户明确知道在转。
    private bool _isInvestigating;
    public bool IsInvestigating
    {
        get => _isInvestigating;
        private set { if (SetField(ref _isInvestigating, value)) { OnPropertyChanged(nameof(CanInvestigate)); InvestigateCommand.RaiseCanExecuteChanged(); } }
    }
    public void BeginInvestigating() => IsInvestigating = true;
    public void EndInvestigating() => IsInvestigating = false;

    // 写回 AI 识别结果 (推测), 触发界面刷新。
    public void ApplyAiInvestigation(string? origin, string? purpose)
    {
        if (!string.IsNullOrWhiteSpace(origin)) _aiOrigin = origin;
        if (!string.IsNullOrWhiteSpace(purpose)) _aiPurpose = purpose;
        IsAiResolved = true;
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(Purpose));
        InvestigateCommand.RaiseCanExecuteChanged();
    }

    internal long RawSize => _node.Size;
    internal bool RawIsDirectory => _node.IsDirectory;

    private bool HasPath => !string.IsNullOrEmpty(_node.Path);
    // 真实文件/目录 (有路径、非余量合成块) 才允许尝试移入回收站; 是否放行由安全闸门当场判定。
    public bool CanRecycle => HasPath && !IsRemainder && !_isDeleted;

    // 迁移入口: 真实目录 (非余量/非容器/未删) 才显示; 是否真能迁由迁移器保守白名单当场判定。
    public bool CanMigrate => HasPath && IsDirectory && !_node.IsContainer && !_isDeleted;

    // 已移入回收站: UI 置灰 + 删除线, 给即时反馈。A2: 删除后失效的动作一并禁用, 只留"打开回收站"。
    private bool _isDeleted;
    public bool IsDeleted
    {
        get => _isDeleted;
        private set
        {
            if (!SetField(ref _isDeleted, value)) return;
            OnPropertyChanged(nameof(CanRecycle));
            OnPropertyChanged(nameof(CanMigrate));
            OnPropertyChanged(nameof(CanInvestigate));
            OnPropertyChanged(nameof(ShowLiveActions));
            RecycleCommand.RaiseCanExecuteChanged();
            MigrateCommand.RaiseCanExecuteChanged();
            InvestigateCommand.RaiseCanExecuteChanged();
            OpenLocationCommand.RaiseCanExecuteChanged();
            OpenRecycleBinCommand.RaiseCanExecuteChanged();
        }
    }

    public void MarkDeleted() => IsDeleted = true;

    /// <summary>是否仍显示"对实物的操作"(复制/打开/AI/迁移/回收)。删除后这些都失效, 仅留"打开回收站"。</summary>
    public bool ShowLiveActions => !_isDeleted;

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
