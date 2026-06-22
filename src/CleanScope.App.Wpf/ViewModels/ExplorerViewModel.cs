using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.App.Wpf.Views;
using CleanScope.Application;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>节点右键操作契约 (E3): 复制 / 打开位置 / 移入回收站 / 按需 AI 识别。由 <see cref="ExplorerViewModel"/> 实现。</summary>
public interface IExplorerActions
{
    bool AiEnabled { get; }
    int AiConfigGeneration { get; }   // 问题#5: 改模型/脱敏档位后 +1, 已识别项据此可重新识别
    void CopyToClipboard(string text, string okStatus);
    Task OpenLocationAsync(string path);
    Task RecycleAsync(ExplorerNodeViewModel node);   // 统一入口: 任意风险等级, 经确认弹窗 (高风险强确认) → 仅回收站
    Task InvestigateAsync(ExplorerNodeViewModel node);
    Task MigrateAsync(ExplorerNodeViewModel node);
    Task OpenRecycleBinAsync();
    Task AddIgnoreAsync(ExplorerNodeViewModel node);
}

/// <summary>
/// 资源管理器树视图 (P1): 把整盘当目录树浏览——可展开/折叠、显大小与占比、按大小排序、标来源/用途/可清理。
/// E3: 每行支持右键复制路径/用途、在系统资源管理器打开、移入回收站 (走安全闸门 + 两步确认, 仅可恢复)。
/// </summary>
public sealed class ExplorerViewModel : ViewModelBase, IExplorerActions
{
    private readonly AppServices _services;
    private ScanSession? _session;
    private readonly List<ExplorerNodeViewModel> _flatNodes = new();   // 当前扁平视图中已订阅勾选变化的节点

    public ExplorerViewModel(AppServices services)
    {
        _services = services;
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => _showCleanableOnly && !_busy);
        SelectNoneCommand = new RelayCommand(() => SetAllSelected(false), () => _showCleanableOnly && !_busy);
        RecycleSelectedCommand = new AsyncRelayCommand(_ => RecycleSelectedAsync(), _ => _showCleanableOnly && !_busy);
        InvestigateSelectedCommand = new AsyncRelayCommand(_ => InvestigateSelectedAsync(), _ => _services.AiEnabled && _showCleanableOnly && !_busy);
        SortBySizeCommand = new RelayCommand(() => SortFlat(bySize: true), () => _showCleanableOnly && !IsSearching);
        SortByNameCommand = new RelayCommand(() => SortFlat(bySize: false), () => _showCleanableOnly && !IsSearching);
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        services.AiChanged += OnAiConfigChanged;   // 问题#5: 改模型/脱敏后, 已识别项恢复"可重新识别"
    }

    // 问题#5: AI 配置变更 → 遍历已物化节点, 让"重新识别"按钮/菜单即时恢复可点。
    private void OnAiConfigChanged()
    {
        foreach (var n in _flatNodes) n.RefreshAiState();
        RefreshAiStateRecursive(Roots);
    }

    private static void RefreshAiStateRecursive(IEnumerable<ExplorerNodeViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            n.RefreshAiState();
            if (n.Children.Count > 0) RefreshAiStateRecursive(n.Children);
        }
    }

    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = new();

    // C1: 批量操作 (仅"只看可清理"扁平视图)
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand RecycleSelectedCommand { get; }
    public AsyncRelayCommand InvestigateSelectedCommand { get; }   // 问题#1: 对勾选项批量 AI 识别
    public RelayCommand SortBySizeCommand { get; }
    public RelayCommand SortByNameCommand { get; }
    public RelayCommand ClearSearchCommand { get; }

    // F1/问题2: 按名称或路径在当前扫描结果里搜索/定位 (输入路径片段即可定位)。仅检索 ≥ 建树阈值 的目录/文件。
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? "")) return;
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(ShowBatchBar));
            OnPropertyChanged(nameof(ShowBatchAi));
            InvestigateSelectedCommand.RaiseCanExecuteChanged();
            SortBySizeCommand.RaiseCanExecuteChanged();
            SortByNameCommand.RaiseCanExecuteChanged();
            Rebuild();
        }
    }
    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    private string _summary = "扫描后在此像目录树一样浏览整个磁盘。";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    private string _actionStatus = "";
    public string ActionStatus { get => _actionStatus; private set => SetField(ref _actionStatus, value); }

    private string _selectionSummary = "";
    public string SelectionSummary { get => _selectionSummary; private set => SetField(ref _selectionSummary, value); }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!SetField(ref _busy, value)) return;
            SelectAllCommand.RaiseCanExecuteChanged();
            SelectNoneCommand.RaiseCanExecuteChanged();
            RecycleSelectedCommand.RaiseCanExecuteChanged();
            InvestigateSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    // "只看可清理": 把深埋在谨慎容器(如 AppData\Local)里的可清理项一次性铺平, 不必逐层展开才找到 (如 %TEMP%)。
    // C1: 此模式下整合"可清理清单"——可勾选 + 全选/全不选 + 批量移入回收站 (替代独立的文件清单页)。
    private bool _showCleanableOnly;
    public bool ShowCleanableOnly
    {
        get => _showCleanableOnly;
        set
        {
            if (!SetField(ref _showCleanableOnly, value)) return;
            Rebuild();
            OnPropertyChanged(nameof(ShowBatchBar));
            OnPropertyChanged(nameof(ShowBatchAi));
            SelectAllCommand.RaiseCanExecuteChanged();
            SelectNoneCommand.RaiseCanExecuteChanged();
            RecycleSelectedCommand.RaiseCanExecuteChanged();
            InvestigateSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>批量操作条仅在"只看可清理"且非搜索态时显示 (搜索结果不参与批量)。</summary>
    public bool ShowBatchBar => _showCleanableOnly && !IsSearching;

    /// <summary>问题#1: 批量 AI 识别按钮仅在配置了 AI 时出现 (未配置零开销, 不显示)。</summary>
    public bool ShowBatchAi => ShowBatchBar && _services.AiEnabled;

    // C2: 当前选中节点 → 底部详情面板完整展示来源/用途/AI 解释/建议 (长文本不再被列截断、不必复制)。
    private ExplorerNodeViewModel? _selectedNode;
    public ExplorerNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set { if (SetField(ref _selectedNode, value)) OnPropertyChanged(nameof(HasSelectedNode)); }
    }
    public bool HasSelectedNode => _selectedNode is not null;

    public void Load(ScanSession session)
    {
        if (_session is not null) { _session.ItemRemoved -= OnItemRemoved; _session.ItemRestored -= OnItemRestored; }
        _session = session;
        _session.ItemRemoved += OnItemRemoved;   // A1: 别处删除时, 本页同步移除/置删除
        _session.ItemRestored += OnItemRestored; // H: 撤销还原后, 本页重建以重新纳入已还原项
        ActionStatus = "";
        _ = PreloadInsightsAsync();              // F: 拉取历史 AI 识别缓存, 供"用 AI 识别"命中复用
        Rebuild();
    }

    // F: AI 识别结果缓存 (path → 推测), 跨会话。命中即免再花 token。仅推测/展示。
    private readonly Dictionary<string, AiInsight> _insightCache = new(StringComparer.OrdinalIgnoreCase);
    private async Task PreloadInsightsAsync()
    {
        try
        {
            var all = await _services.AiInsights.GetAllAsync();
            _insightCache.Clear();
            foreach (var i in all) _insightCache[i.Path] = i;
        }
        catch (Exception ex) { CleanScope.Domain.Diagnostics.AppTrace.Log("加载 AI 识别缓存失败", ex); }
    }

    // H: 撤销还原后, 整页重建 —— 还原项已从 session._removed 移出, 重建即重新纳入显示 (与磁盘一致)。
    private void OnItemRestored(string path) => Rebuild();

    // A1: 任一页移除某路径 → 本页同步。只看可清理模式直接移除对应根(便宜, 避免每次全量重建);
    // 树模式标记已物化的对应节点为已删。
    private void OnItemRemoved(string path)
    {
        var p = path.Replace('/', '\\').TrimEnd('\\');
        if (_showCleanableOnly)
        {
            var match = Roots.FirstOrDefault(r =>
                !string.IsNullOrEmpty(r.Path) && string.Equals(r.Path.TrimEnd('\\'), p, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.PropertyChanged -= OnFlatNodePropertyChanged;
                _flatNodes.Remove(match);
                Roots.Remove(match);
                UpdateSelectionSummary();
            }
            return;
        }
        MarkMaterializedDeleted(Roots, p);
    }

    // —— C1 批量勾选 / 全选 / 批量回收 (只看可清理模式) ——
    private void OnFlatNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerNodeViewModel.IsSelected)) UpdateSelectionSummary();
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var n in _flatNodes) n.IsSelected = selected;
        UpdateSelectionSummary();
    }

    // 扁平视图重排序 (按大小降序 / 按名称升序), 重排同一批节点实例 (勾选订阅不变)。
    private void SortFlat(bool bySize)
    {
        if (!_showCleanableOnly) return;
        var sorted = bySize
            ? _flatNodes.OrderByDescending(n => n.RawSize).ToList()
            : _flatNodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _flatNodes.Clear();
        _flatNodes.AddRange(sorted);
        Roots.Clear();
        foreach (var n in sorted) Roots.Add(n);
    }

    private void UpdateSelectionSummary()
    {
        var chosen = _flatNodes.Where(n => n.IsSelected).ToList();
        var size = chosen.Sum(n => n.RawSize);
        SelectionSummary = chosen.Count == 0
            ? "勾选可清理项后可批量移入回收站（可还原）。"
            : $"已选 {chosen.Count} 项，约 {Format.HumanSize(size)}，可一键移入回收站（可还原）。";
        RecycleSelectedCommand.RaiseCanExecuteChanged();
    }

    // 批量移入回收站: 先确认(瞬时) → 后台线程逐项过闸门+执行 → 进度回 UI 线程 + 标删 + 广播变更总线。
    private async Task RecycleSelectedAsync()
    {
        var targets = _flatNodes.Where(n => n.IsSelected && n.CanRecycle).ToList();
        if (targets.Count == 0) { ActionStatus = "请先勾选要清理的项。"; return; }

        var total = targets.Sum(n => n.RawSize);
        var model = new ConfirmDialogModel
        {
            Title = "批量移入回收站",
            Intro = "确定把选中的这些项移入回收站吗？可从回收站随时还原，非永久删除。",
            Details = ConfirmDialogModel.Rows(
                ("项数", $"{targets.Count} 项"), ("合计", $"约 {Format.HumanSize(total)}")),
            WarningText = "每一项仍会逐一经安全闸门复核，命中系统关键/容器/占用的会被自动跳过、不动。",
            ConfirmText = $"移入回收站（{targets.Count} 项）",
        };
        if (!ConfirmDialog.Show(System.Windows.Application.Current?.MainWindow, model))
        {
            ActionStatus = "已取消，未做任何改动。"; return;
        }

        IsBusy = true;
        int ok = 0, skipped = 0;
        var count = targets.Count;
        var recycled = new List<(string path, long size, bool cleanable)>();   // H: 供"撤销"
        var reporter = (IProgress<(int done, ExplorerNodeViewModel? deleted)>)new Progress<(int done, ExplorerNodeViewModel? deleted)>(p =>
        {
            ActionStatus = $"正在处理 {p.done}/{count}…";
            if (p.deleted is not null)
            {
                p.deleted.MarkDeleted();
                _session?.NotifyRemoved(p.deleted.Path, p.deleted.RawSize, p.deleted.IsCleanable);
                recycled.Add((p.deleted.Path, p.deleted.RawSize, p.deleted.IsCleanable));
            }
        });
        try
        {
            await Task.Run(async () =>
            {
                var i = 0;
                foreach (var n in targets)
                {
                    i++;
                    var request = new ActionRequest(null, n.Path, ActionType.MoveToRecycleBin);
                    var verdict = _services.SafetyGuard.Evaluate(request, null, n.ToRiskAssessment());
                    if (verdict.Outcome != GuardOutcome.Allowed) { skipped++; reporter.Report((i, null)); continue; }
                    var log = await _services.ActionExecutor.ExecuteAsync(request, verdict);
                    if (log.Result == ActionResult.Success) { ok++; reporter.Report((i, n)); }
                    else { skipped++; reporter.Report((i, null)); }
                }
            });
        }
        finally
        {
            IsBusy = false;
        }

        _lastRecycled = recycled;
        ActionStatus = $"已移入回收站 {ok} 项（可还原）"
            + (skipped > 0 ? $"；{skipped} 项被安全闸门拦下或失败，已保留。" : "。");
        if (ok > 0) Toast.Show($"已移入回收站 {ok} 项", ToastKind.Success, "撤销", () => _ = UndoLastRecycleAsync());
        UpdateSelectionSummary();
    }

    // 问题#1: 批量 AI 识别勾选项 —— 逐个走按需识别 (命中缓存即跳过、不花 token), 给整体进度。
    private async Task InvestigateSelectedAsync()
    {
        if (!_services.AiEnabled) { ActionStatus = "未配置 AI，无法识别。可在「AI 设置」里配置后再试。"; return; }

        // 只挑勾选、可识别、且本会话尚未识别过的 (已识别的不重复花 token)。
        var targets = _flatNodes.Where(n => n.IsSelected && n.CanInvestigate && !n.IsAiResolved).ToList();
        if (targets.Count == 0)
        {
            ActionStatus = _flatNodes.Any(n => n.IsSelected)
                ? "勾选的项都已识别过了（已识别的不重复请求）。"
                : "请先勾选要用 AI 识别的项。";
            return;
        }

        var model = new ConfirmDialogModel
        {
            Title = "批量用 AI 识别",
            Intro = "将对勾选的项逐个用 AI 识别来源 / 用途（脱敏后请求，仅供参考）。命中缓存或已识别过的会自动跳过、不再花 token。",
            Details = ConfirmDialogModel.Rows(
                ("待识别", $"{targets.Count} 项"), ("出云脱敏", SanitizationLabel)),
            WarningText = "AI 只补充“是什么/归谁”的推测，绝不改动风险等级、更不会触发删除。",
            ConfirmText = $"开始识别（{targets.Count} 项）",
        };
        if (!ConfirmDialog.Show(System.Windows.Application.Current?.MainWindow, model))
        {
            ActionStatus = "已取消批量识别。"; return;
        }

        IsBusy = true;
        var done = 0;
        try
        {
            foreach (var n in targets)
            {
                done++;
                ActionStatus = $"正在用 AI 识别 {done}/{targets.Count}：「{n.Name}」…";
                await InvestigateAsync(n);   // 复用单项逻辑: 缓存/脱敏请求/落库/行内转圈
            }
        }
        finally { IsBusy = false; }

        ActionStatus = $"批量 AI 识别完成：处理 {targets.Count} 项（结果见各行「AI推测」标记与右侧解释面板）。";
    }

    // H: 撤销刚才的回收 —— 从回收站还原回原位 (纯还原); 自动还原失败的回退到"打开回收站"。
    private List<(string path, long size, bool cleanable)> _lastRecycled = new();

    private async Task UndoLastRecycleAsync()
    {
        var batch = _lastRecycled.ToList();
        if (batch.Count == 0) { await OpenRecycleBinAsync(); return; }
        ActionStatus = $"正在还原 {batch.Count} 项…";
        int ok = 0, fail = 0;
        foreach (var (path, size, cleanable) in batch)
        {
            var restored = await Task.Run(() => _services.RecycleRestore.TryRestore(path));
            if (restored) { _session?.NotifyRestored(path, size, cleanable); ok++; }
            else fail++;
        }
        _lastRecycled.Clear();
        if (ok > 0 && fail == 0)
        {
            ActionStatus = $"已还原 {ok} 项到原位。";
            Toast.Show($"已还原 {ok} 项", ToastKind.Success);
        }
        else if (ok > 0)
        {
            ActionStatus = $"已还原 {ok} 项；{fail} 项未能自动还原，已为你打开回收站手动还原。";
            await OpenRecycleBinAsync();
        }
        else
        {
            ActionStatus = "未能自动还原，已为你打开回收站，可在其中右键「还原」。";
            await OpenRecycleBinAsync();
        }
    }

    private static void MarkMaterializedDeleted(IEnumerable<ExplorerNodeViewModel> nodes, string path)
    {
        foreach (var n in nodes)
        {
            if (!n.IsDeleted && !string.IsNullOrEmpty(n.Path) &&
                string.Equals(n.Path.TrimEnd('\\'), path, StringComparison.OrdinalIgnoreCase))
                n.MarkDeleted();
            if (n.Children.Count > 0) MarkMaterializedDeleted(n.Children, path);
        }
    }

    // 整棵扫描树的 DFS 枚举 (搜索用)。树已按建树阈值剪枝, 规模有界。
    private static IEnumerable<ScanTreeNode> EnumerateAll(ScanTreeNode root)
    {
        var stack = new Stack<ScanTreeNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            foreach (var c in n.Children) stack.Push(c);
        }
    }

    private void Rebuild()
    {
        // 释放上一批扁平节点的勾选订阅, 避免泄漏。
        foreach (var n in _flatNodes) n.PropertyChanged -= OnFlatNodePropertyChanged;
        _flatNodes.Clear();
        Roots.Clear();
        var session = _session;
        if (session?.Tree is null)
        {
            Summary = "本次扫描未生成目录树。";
            SelectionSummary = "";
            return;
        }

        // F1/问题2: 搜索态优先 —— 不论树/扁平, 都把整棵扫描树里名称或路径匹配的项铺平展示, 供快速定位。
        if (IsSearching)
        {
            var q = _searchText.Trim();
            var matches = EnumerateAll(session.Tree)
                .Where(n => !string.IsNullOrEmpty(n.Path) && !ReferenceEquals(n, session.Tree)
                            && (n.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || n.Path.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => n.Size)
                .Take(500)
                .ToList();
            var top = matches.Count > 0 ? matches[0].Size : 1;
            foreach (var n in matches)
                Roots.Add(new ExplorerNodeViewModel(n, top, withinCleanable: false, actions: this));
            Summary = matches.Count == 0
                ? $"没有匹配「{q}」的项（仅检索 ≥ 阈值的目录/文件）。"
                : $"搜索「{q}」：{matches.Count} 个匹配，按大小排序（最多 500 个；仅含 ≥ 阈值的项）。点项看右侧解释。";
            SelectionSummary = "";
            return;
        }

        if (_showCleanableOnly)
        {
            // 扁平列出整盘所有"最顶层可清理"节点 (与概览的处数/总量同口径), 按大小降序; 比例条相对最大者。
            // A1: 排除已被(本页或别页)移除的项, 删后即从清单消失。C1: 这些项可勾选批量回收。
            var items = ScanTreeStats.EnumerateCleanable(session.Tree).Where(n => !session.IsRemoved(n.Path)).ToList();
            var max = items.Count > 0 ? items[0].Size : 1;
            foreach (var n in items)
            {
                var vm = new ExplorerNodeViewModel(n, max, withinCleanable: false, actions: this, selectable: true);
                vm.PropertyChanged += OnFlatNodePropertyChanged;
                _flatNodes.Add(vm);
                Roots.Add(vm);
            }
            Summary = $"只看可清理：{items.Count} 处，共约 {Format.HumanSize(session.RemainingReclaimable)}" +
                      "（含深埋各软件内部缓存）。可勾选后批量移入回收站（可还原），或右键单项操作；取消勾选「只看可清理」可看完整目录树。";
            UpdateSelectionSummary();
            return;
        }

        var root = new ExplorerNodeViewModel(session.Tree, session.Tree.Size, withinCleanable: false, actions: this)
        { IsExpanded = true };
        Roots.Add(root);
        Summary = $"{session.TargetPath} — 共 {Format.HumanSize(session.Tree.Size)}；" +
                  $"其中 ✓可清理约 {Format.HumanSize(session.TreeReclaimable)}（{session.TreeCleanableCount} 处，含各软件内部缓存）。" +
                  "点击 ▸ 展开目录；右键可复制路径/打开位置/移入回收站。";
    }

    // —— IExplorerActions ——

    public bool AiEnabled => _services.AiEnabled;
    public int AiConfigGeneration => _services.AiConfigGeneration;

    // 问题#4: 当前脱敏档位的简短中文标签 (展示给用户, 让"三档位"可见)。
    private string SanitizationLabel => _services.SanitizationLevel switch
    {
        SanitizationLevel.Strict => "严格",
        SanitizationLevel.Balanced => "均衡",
        _ => "关闭",
    };

    // 严格档下 AI 常认不出具体软件; 提示用户"可在 AI 设置降档重试", 避免误以为 AI 能力弱。
    // 问题#1: 若失败有诊断原因 (网络/鉴权/模型名/截断), 一并带出, 不再笼统"未能识别"。
    private string IdentifyFailedHint(string name)
    {
        var baseHint = _services.SanitizationLevel == SanitizationLevel.Strict
            ? $"AI 未能识别「{name}」。当前出云脱敏为「严格」(不发送文件夹名)，可在「AI 设置」改为「均衡」后重新识别以提升识别力。"
            : $"AI 未能识别「{name}」（以现有结论为准）。";
        var reason = CleanScope.Domain.Diagnostics.AppTrace.LastError;
        return reason is null ? baseHint : $"{baseHint}　原因：{reason}（详见 AI 设置 → 查看日志）。";
    }

    // E5+ 按需 AI 识别 (最小用量): 只在用户点了右键"用 AI 识别"时, 对这一个目录发一次脱敏请求。
    // 确定性目录名启发(NameHeuristics)已先免费兜底; AI 仅消化它认不出的残余未知。未配置 AI 的用户菜单项不显示, 零开销。
    public async Task InvestigateAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanInvestigate) return;

        // F: 缓存命中 (上次/上回会话识别过的同一路径) → 直接复用, 不再花 token。
        // 仅在本会话尚未识别该项时用缓存; 改了模型/脱敏档位后再点 (IsAiResolved=true) 则跳过缓存、走 AI 取新结果。
        if (!node.IsAiResolved && !string.IsNullOrEmpty(node.Path)
            && _insightCache.TryGetValue(node.Path, out var cached)
            && (cached.Origin is not null || cached.Purpose is not null))
        {
            node.ApplyAiInvestigation(cached.Origin, cached.Purpose);
            ActionStatus = $"已复用上次的 AI 识别「{node.Name}」（缓存，未再花 token）。";
            return;
        }

        node.BeginInvestigating();   // A4: 行内"✨识别中…"+ 禁用再点, 让用户明确知道在转
        // 问题#4: 把当前脱敏档位摆到用户眼前, 让"可调三档位"被看见 (Strict 时尤其要让人知道可降档提升识别力)。
        ActionStatus = $"正在用 AI 识别「{node.Name}」（当前出云脱敏：{SanitizationLabel}；脱敏后请求，仅供参考）…";

        // 构造轻量分析 (空证据、无逐项 I/O) 交注解器: 脱敏 → 解释 → 校验。AI 永不进入风险/删除判定。
        var fileNode = new FileNode(0, 0, null, node.Path, null, node.Name, node.RawIsDirectory, false,
            node.RawSize, null, null, null, AccessState.Accessible, null, DateTime.UtcNow);
        var analysis = new FileAnalysis(fileNode, new EvidenceBundle(0, null, Array.Empty<Evidence>()),
            RuleMatch: null, Array.Empty<AttributionCandidate>(), node.ToRiskAssessment(), Explanation: null);

        try
        {
            var ai = await _services.Annotator.AnnotateAsync(analysis);
            if (ai is not { Validated: true })
            {
                ActionStatus = IdentifyFailedHint(node.Name);
                return;
            }

            var owner = ai.OwnerApp?.Trim();
            var why = ai.UserFriendlyExplanation?.Trim();
            var what = ai.WhatIsIt?.Trim();
            var purpose = !string.IsNullOrWhiteSpace(why) ? why : what;
            var origin = !string.IsNullOrWhiteSpace(owner) ? $"{owner}（AI 推测）" : null;

            if (origin is null && string.IsNullOrWhiteSpace(purpose))
            {
                ActionStatus = IdentifyFailedHint(node.Name);
                return;
            }
            node.ApplyAiInvestigation(origin, purpose);
            ActionStatus = $"已用 AI 补充识别「{node.Name}」（推测，仅供参考）。";

            // F: 落库缓存 (跨会话复用, 下次同一路径免再花 token)。失败不影响本次结果。
            var insight = new AiInsight(node.Path, origin, purpose, DateTime.UtcNow);
            _insightCache[node.Path] = insight;
            try { await _services.AiInsights.UpsertAsync(insight); }
            catch (Exception ex) { CleanScope.Domain.Diagnostics.AppTrace.Log("写入 AI 识别缓存失败", ex); }
        }
        catch (Exception ex)
        {
            CleanScope.Domain.Diagnostics.AppTrace.Log($"AI 识别「{node.Name}」失败", ex);
            ActionStatus = $"AI 识别失败「{node.Name}」：{ex.Message}（可在「AI 设置 → 查看日志」看详情；以现有结论为准）。";
        }
        finally
        {
            node.EndInvestigating();
        }
    }

    // P0 跨盘迁移: 选目标盘 → 两步确认 → 迁移器 (复制/校验/审计/改名留备份/建联接, 绝不永久删除)。
    // 原目录改名留作 .cleanscope-bak 备份; 确认软件正常后用户可自行移入回收站释放原盘空间。
    public async Task MigrateAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanMigrate) return;

        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"选择把「{node.Name}」迁移到的目标位置（请选其他磁盘的一个文件夹）",
        };
        if (picker.ShowDialog() != true) { ActionStatus = "已取消迁移。"; return; }
        var targetRoot = picker.FolderName;

        var model = new ConfirmDialogModel
        {
            Title = "迁移到其他盘",
            Intro = "将把以下目录迁移到其他磁盘，并在原位创建目录联接（对软件透明，照常使用）。",
            Details = ConfirmDialogModel.Rows(("源", node.Path), ("目标", targetRoot),
                ("过程", "复制到目标盘 → 校验 → 原目录改名留作备份 → 建立联接")),
            WarningText = "不会永久删除任何数据；释放原盘空间需你之后确认软件正常、再自行删除那个 .cleanscope-bak 备份。",
            ConfirmText = "开始迁移",
        };
        if (!ConfirmDialog.Show(System.Windows.Application.Current?.MainWindow, model))
        {
            ActionStatus = "已取消，未做任何改动。"; return;
        }

        ActionStatus = $"正在迁移「{node.Name}」…（大目录可能需要一些时间）";
        try
        {
            var result = await Task.Run(() => _services.Migrator.MigrateAsync(new MigrationRequest(node.Path, targetRoot)));
            ActionStatus = result.Outcome switch
            {
                MigrationOutcome.Success => $"✅ 已迁移「{node.Name}」。{result.Message}",
                MigrationOutcome.Rejected => $"未迁移：{result.Message}",
                _ => $"迁移未完成：{result.Message}",
            };
        }
        catch (Exception ex)
        {
            ActionStatus = $"迁移失败「{node.Name}」：{ex.Message}";
        }
    }

    public void CopyToClipboard(string text, string okStatus)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); ActionStatus = $"{okStatus}：{text}"; }
        catch { ActionStatus = "复制失败（剪贴板被占用），请稍后重试。"; }
    }

    public Task OpenLocationAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return Task.CompletedTask;
        var request = new ActionRequest(null, path, ActionType.OpenDir);
        var approval = _services.SafetyGuard.Evaluate(request, null, null);   // 只读操作, 闸门直接放行
        return RunAsync(request, approval, "已在系统资源管理器中打开位置。");
    }

    // E3 删除 (问题#2 统一入口): 任意风险等级走同一个"移入回收站"。两段式判定 ——
    //   ① 先按常规 (A/B) 过闸门; 放行 → 普通确认弹窗。
    //   ② 常规被拒 (风险等级不够) → 再以 UserOverride 复判; 这次放行 → 说明只是风险高 (非红线),
    //      弹"高风险强确认"弹窗 (红 + 必须勾选), 用户确认后以 override 仅移入回收站。
    //   ③ 连 override 都拒 → 命中系统关键/容器/占用红线, 不可删除, 只如实告知理由 (不弹确认框)。
    // 绝不永久删除; 占用检测与回收站执行可能耗时, 一律放后台线程, 先给"检查中"提示, 避免界面像冻住。
    public async Task RecycleAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanRecycle) return;

        var risk = node.ToRiskAssessment();
        ActionStatus = $"正在检查「{node.Name}」是否可安全清理…";

        var normal = new ActionRequest(null, node.Path, ActionType.MoveToRecycleBin);
        var verdict = await Task.Run(() => _services.SafetyGuard.Evaluate(normal, null, risk));
        var request = normal;
        var highRisk = false;

        if (verdict.Outcome != GuardOutcome.Allowed)
        {
            // 风险等级不够 → 试 override; 若仍被拒, 即为红线 (黑名单/容器/占用), 不可越过。
            var manual = new ActionRequest(null, node.Path, ActionType.MoveToRecycleBin, UserOverride: true);
            var manualVerdict = await Task.Run(() => _services.SafetyGuard.Evaluate(manual, null, risk));
            if (manualVerdict.Outcome != GuardOutcome.Allowed)
            {
                ActionStatus = string.IsNullOrWhiteSpace(manualVerdict.RecommendedAlternative)
                    ? $"不可删除：{manualVerdict.Reason}"
                    : $"不可删除：{manualVerdict.Reason}　建议：{manualVerdict.RecommendedAlternative}";
                return;
            }
            verdict = manualVerdict;
            request = manual;
            highRisk = true;
        }
        ActionStatus = "";

        var model = highRisk
            ? new ConfirmDialogModel
            {
                Title = "移入回收站（高风险项）",
                IsHighRisk = true,
                Intro = "此项未被识别为可安全清理，等级偏高。仅在你确认这是你自己的、了解其用途的数据时才继续。",
                Details = ConfirmDialogModel.Rows(
                    ("名称", node.Name), ("路径", node.Path), ("大小", node.SizeText),
                    ("来源", node.Origin), ("风险", node.BucketLabel)),
                WarningText = "移入回收站后仍可还原，但请自行确认它不是某个程序/系统正在使用的数据。",
                CheckText = "我确认这是我自己的数据，了解风险并自行承担（仍可从回收站还原）。",
                ConfirmText = "确认移入回收站",
            }
            : new ConfirmDialogModel
            {
                Title = "移入回收站",
                Intro = "确定把以下项移入回收站吗？可从回收站随时还原，非永久删除。",
                Details = ConfirmDialogModel.Rows(
                    ("名称", node.Name), ("路径", node.Path), ("大小", node.SizeText), ("来源", node.Origin)),
                ConfirmText = "移入回收站",
            };

        if (!ConfirmDialog.Show(System.Windows.Application.Current?.MainWindow, model))
        {
            ActionStatus = "已取消，未做任何改动。";
            return;
        }

        ActionStatus = $"正在移入回收站「{node.Name}」…";
        var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, verdict));
        if (log.Result == ActionResult.Success)
        {
            node.MarkDeleted();
            _session?.NotifyRemoved(node.Path, node.RawSize, node.IsCleanable);   // A1: 广播给其它页 + 概览扣减
            _lastRecycled = new() { (node.Path, node.RawSize, node.IsCleanable) };  // H: 供"撤销"
            ActionStatus = $"已移入回收站（可还原）：{node.Name}";
            Toast.Show($"已移入回收站：{node.Name}", ToastKind.Success, "撤销", () => _ = UndoLastRecycleAsync());
        }
        else
        {
            ActionStatus = $"操作未完成：{log.RejectReason}";
            Toast.Error($"未能移入回收站：{log.RejectReason}");
        }
    }

    // A5: 加入忽略名单 (持久化到本地库)。报告/忽略名单页在切回时刷新显示, 跨页一致。
    public async Task AddIgnoreAsync(ExplorerNodeViewModel node)
    {
        if (node is null || string.IsNullOrEmpty(node.Path)) return;
        try
        {
            await _services.IgnoreRepository.AddAsync(
                new IgnoreEntry(0, node.Path, MatchType.Exact, "在资源管理器忽略", DateTime.UtcNow));
            ActionStatus = $"已加入忽略名单：{node.Name}（可在「报告 / 忽略名单」页管理）。";
            Toast.Show($"已加入忽略名单：{node.Name}", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ActionStatus = $"加入忽略失败：{ex.Message}";
        }
    }

    // A2: 删除后唯一可用动作 —— 打开系统回收站, 用户可在其中查看/还原。
    public Task OpenRecycleBinAsync()
    {
        var request = new ActionRequest(null, "shell:RecycleBinFolder", ActionType.OpenDir);
        var approval = _services.SafetyGuard.Evaluate(request, null, null);   // 只读操作, 闸门直接放行
        return RunAsync(request, approval, "已打开回收站，可在其中查看或还原。");
    }

    private async Task RunAsync(ActionRequest request, GuardDecision approval, string okStatus)
    {
        var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, approval));
        ActionStatus = log.Result == ActionResult.Success ? okStatus : $"操作未完成：{log.RejectReason}";
    }
}
