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
        IExplorerActions? actions = null, bool selectable = false)
    {
        _node = node;
        _parentSize = parentSize > 0 ? parentSize : node.Size;
        _withinCleanable = withinCleanable;
        _actions = actions;
        IsSelectable = selectable;
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
        AddIgnoreCommand = new AsyncRelayCommand(_ => _actions?.AddIgnoreAsync(this) ?? Task.CompletedTask, _ => HasPath && !_isDeleted);
    }

    // 有效可清理: 自身可清理, 或处在可清理祖先之下 (缓存目录里的子项也随父一起清, 颜色应一致)。
    private bool EffectiveCleanable => _node.IsCleanable || _withinCleanable;

    // 展开时惰性物化子节点 (含"余量"合成块)。
    public ObservableCollection<ExplorerNodeViewModel> Children { get; }

    public RelayCommand CopyPathCommand { get; }
    public RelayCommand CopyPurposeCommand { get; }
    public AsyncRelayCommand OpenLocationCommand { get; }
    public AsyncRelayCommand RecycleCommand { get; }   // 统一入口: 任意风险等级 (高风险走强确认弹窗)
    public AsyncRelayCommand InvestigateCommand { get; }   // E5+: 按需用 AI 识别此项 (零默认开销)
    public AsyncRelayCommand MigrateCommand { get; }       // P0: 把此目录迁到其他盘 + 建目录联接
    public AsyncRelayCommand OpenRecycleBinCommand { get; }  // A2: 删除后唯一可用动作 —— 打开回收站查看/还原
    public AsyncRelayCommand AddIgnoreCommand { get; }       // A5: 加入忽略名单 (报告页同步)

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
    // 认不出归属的项 → 展示为"个人文件"而非"未知来源/无法判断"(人话化, 不动裁决)。
    public string Origin => _aiOrigin ?? Humanize.Origin(_node.Origin);
    public string Purpose => _aiPurpose ?? Humanize.Purpose(_node.Purpose, Origin);
    public string RecommendedAction => _node.RecommendedAction;
    public bool IsCleanable => EffectiveCleanable;
    public bool IsRemainder { get; private init; }

    // —— E5+ 按需 AI 识别 ——
    private string? _aiOrigin;
    private string? _aiPurpose;
    private bool _aiResolved;
    private int _resolvedGeneration = -1;   // 问题#5: 上次识别时的 AI 配置代次
    public bool IsAiResolved { get => _aiResolved; private set { if (SetField(ref _aiResolved, value)) OnPropertyChanged(nameof(CanInvestigate)); } }

    private int CurrentAiGeneration => _actions?.AiConfigGeneration ?? 0;
    // 问题#5: 已识别过, 但用户之后改了模型/脱敏档位 (代次变了) → 允许在新配置下重新识别。
    private bool ConfigChangedSinceResolve => _aiResolved && _resolvedGeneration != CurrentAiGeneration;

    // 仅 AI 已启用 + 真实项 + 识别中/已删 之外可点; 未配置 AI 的用户菜单项不显示, 自然零开销。
    // 已识别后默认禁用, 除非配置已变 (换模型/换档位) → 可重新识别。
    public bool ShowAiMenu => _actions?.AiEnabled == true;
    public bool CanInvestigate => ShowAiMenu && HasPath && !IsRemainder && !_isInvestigating && !_isDeleted
        && (!_aiResolved || ConfigChangedSinceResolve);

    /// <summary>问题#5: AI 配置变更后, 让"重新识别"按钮/菜单即时恢复可点。</summary>
    public void RefreshAiState()
    {
        OnPropertyChanged(nameof(CanInvestigate));
        InvestigateCommand.RaiseCanExecuteChanged();
    }

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
        _resolvedGeneration = CurrentAiGeneration;   // 问题#5: 记下本次识别所用的配置代次
        IsAiResolved = true;
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(Purpose));
        OnPropertyChanged(nameof(Grade));        // AI 识别可能把"未知"判为个人文件 → 徽章随之变
        OnPropertyChanged(nameof(BucketLabel));
        OnPropertyChanged(nameof(BucketBrush));
        InvestigateCommand.RaiseCanExecuteChanged();
    }

    internal long RawSize => _node.Size;
    internal bool RawIsDirectory => _node.IsDirectory;

    private bool HasPath => !string.IsNullOrEmpty(_node.Path);
    // 真实文件/目录 (有路径、非余量合成块) 才允许尝试移入回收站; 是否放行 (及是否需强确认) 由安全闸门当场判定。
    // 问题#2: 单一"移入回收站"入口覆盖全部风险等级 —— A/B 普通确认, C-E 经 override 走高风险强确认弹窗。
    public bool CanRecycle => HasPath && !IsRemainder && !_isDeleted;

    // C1: "只看可清理"扁平视图里的批量勾选 (仅可回收项可勾)。
    public bool IsSelectable { get; }
    public bool CanSelect => IsSelectable && CanRecycle;
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        // 不可勾的项强制为否; 变化时通知宿主重算"已选合计/批量条可见性"(树模式也支持多选)。
        set { if (SetField(ref _isSelected, value && CanSelect)) _actions?.OnSelectionChanged(); }
    }

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
            if (_isSelected) { _isSelected = false; OnPropertyChanged(nameof(IsSelected)); _actions?.OnSelectionChanged(); }
            OnPropertyChanged(nameof(CanRecycle));
            OnPropertyChanged(nameof(CanSelect));
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
    // P2 双语义: "个人文件"是珍贵≠危险 —— 用中性蓝灰, 不再用谨慎/勿动的红/橙, 避免"你的资料像有问题"。
    private bool IsPersonal => Origin == Common.Humanize.Personal
        && Bucket is not CleanupBucket.Cleanable and not CleanupBucket.Container;
    public string BucketLabel => IsPersonal ? "个人文件" : Buckets.Label(Bucket);
    public Brush BucketBrush => IsPersonal ? PersonalBrush : Buckets.Brush(Bucket);
    private static readonly Brush PersonalBrush = new SolidColorBrush(Color.FromRgb(0x9D, 0xB4, 0xCE));

    // 品牌签名: A–E 等级徽章 (清单/详情/地图统一)。个人/容器/余量为无字母中性徽章。
    // 字母等级直取风险引擎的 RiskLevel —— 把过去藏起来的"为什么这么判"提到台面, 成为解释主角。
    public Common.GradeBadge Grade =>
        IsRemainder ? Common.GradeBadge.Other
        : IsPersonal ? Common.GradeBadge.Personal
        : _node.IsContainer ? Common.GradeBadge.Container
        : Common.GradeBadge.Of(_node.RiskLevel);

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
            Children.Add(new ExplorerNodeViewModel(c, _node.Size, childWithinCleanable, _actions, IsSelectable));

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
