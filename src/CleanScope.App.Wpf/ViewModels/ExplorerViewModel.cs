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
    void CopyToClipboard(string text, string okStatus);
    Task OpenLocationAsync(string path);
    Task RecycleAsync(ExplorerNodeViewModel node);
    Task ManualRecycleAsync(ExplorerNodeViewModel node);   // 问题#4: 强确认手动处置高风险项 (仍仅回收站)
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
        SortBySizeCommand = new RelayCommand(() => SortFlat(bySize: true), () => _showCleanableOnly);
        SortByNameCommand = new RelayCommand(() => SortFlat(bySize: false), () => _showCleanableOnly);
    }

    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = new();

    // C1: 批量操作 (仅"只看可清理"扁平视图)
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand RecycleSelectedCommand { get; }
    public RelayCommand SortBySizeCommand { get; }
    public RelayCommand SortByNameCommand { get; }

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
            SelectAllCommand.RaiseCanExecuteChanged();
            SelectNoneCommand.RaiseCanExecuteChanged();
            RecycleSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>批量操作条仅在"只看可清理"时显示。</summary>
    public bool ShowBatchBar => _showCleanableOnly;

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
        if (_session is not null) _session.ItemRemoved -= OnItemRemoved;
        _session = session;
        _session.ItemRemoved += OnItemRemoved;   // A1: 别处删除时, 本页同步移除/置删除
        ActionStatus = "";
        Rebuild();
    }

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
        var confirm = MessageBox.Show(
            $"确定把选中的 {targets.Count} 项（约 {Format.HumanSize(total)}）移入回收站吗？\n\n" +
            "可从回收站还原，非永久删除。每一项仍会经安全闸门复核，命中系统关键/容器/占用的会被自动跳过。",
            "批量移入回收站 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { ActionStatus = "已取消，未做任何改动。"; return; }

        IsBusy = true;
        int ok = 0, skipped = 0;
        var count = targets.Count;
        var reporter = (IProgress<(int done, ExplorerNodeViewModel? deleted)>)new Progress<(int done, ExplorerNodeViewModel? deleted)>(p =>
        {
            ActionStatus = $"正在处理 {p.done}/{count}…";
            if (p.deleted is not null)
            {
                p.deleted.MarkDeleted();
                _session?.NotifyRemoved(p.deleted.Path, p.deleted.RawSize, p.deleted.IsCleanable);
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

        ActionStatus = $"已移入回收站 {ok} 项（可还原）"
            + (skipped > 0 ? $"；{skipped} 项被安全闸门拦下或失败，已保留。" : "。");
        UpdateSelectionSummary();
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

    // E5+ 按需 AI 识别 (最小用量): 只在用户点了右键"用 AI 识别"时, 对这一个目录发一次脱敏请求。
    // 确定性目录名启发(NameHeuristics)已先免费兜底; AI 仅消化它认不出的残余未知。未配置 AI 的用户菜单项不显示, 零开销。
    public async Task InvestigateAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanInvestigate) return;
        node.BeginInvestigating();   // A4: 行内"✨识别中…"+ 禁用再点, 让用户明确知道在转
        ActionStatus = $"正在用 AI 识别「{node.Name}」（脱敏后请求，仅供参考）…";

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
                ActionStatus = $"AI 未能识别「{node.Name}」（以现有结论为准）。";
                return;
            }

            var owner = ai.OwnerApp?.Trim();
            var why = ai.UserFriendlyExplanation?.Trim();
            var what = ai.WhatIsIt?.Trim();
            var purpose = !string.IsNullOrWhiteSpace(why) ? why : what;
            var origin = !string.IsNullOrWhiteSpace(owner) ? $"{owner}（AI 推测）" : null;

            if (origin is null && string.IsNullOrWhiteSpace(purpose))
            {
                ActionStatus = $"AI 未能识别「{node.Name}」（以现有结论为准）。";
                return;
            }
            node.ApplyAiInvestigation(origin, purpose);
            ActionStatus = $"已用 AI 补充识别「{node.Name}」（推测，仅供参考）。";
        }
        catch
        {
            ActionStatus = $"AI 识别失败「{node.Name}」（以现有结论为准）。";
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

        var confirm = MessageBox.Show(
            $"将把以下目录迁移到其他磁盘，并在原位创建目录联接（对软件透明，照常使用）：\n\n" +
            $"源：{node.Path}\n目标：{targetRoot}\n\n" +
            "过程：复制到目标盘 → 校验 → 原目录改名留作备份 → 建立联接。\n" +
            "我们不会永久删除任何数据；释放原盘空间需你之后确认软件正常、再删除那个 .cleanscope-bak 备份。\n\n确定继续吗？",
            "迁移到其他盘 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { ActionStatus = "已取消，未做任何改动。"; return; }

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

    // E3 删除: 安全闸门当场判定 (黑名单/容器/A·B/占用按路径独立强制) → 两步确认 → 执行器先写审计后仅移入回收站。
    // 绝不永久删除; 被拒则只提示理由, 连确认框都不弹。
    // B: 占用检测(Restart Manager)与回收站执行可能耗时, 一律放后台线程, 先给"检查中"提示, 避免确认框迟迟不弹/界面冻住像出 bug。
    public async Task RecycleAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanRecycle) return;

        var request = new ActionRequest(null, node.Path, ActionType.MoveToRecycleBin);

        ActionStatus = $"正在检查「{node.Name}」是否可安全清理…";
        var verdict = await Task.Run(() => _services.SafetyGuard.Evaluate(request, null, node.ToRiskAssessment()));
        if (verdict.Outcome != GuardOutcome.Allowed)
        {
            ActionStatus = string.IsNullOrWhiteSpace(verdict.RecommendedAlternative)
                ? $"不可删除：{verdict.Reason}"
                : $"不可删除：{verdict.Reason}　建议：{verdict.RecommendedAlternative}";
            return;
        }
        ActionStatus = "";

        var confirm = MessageBox.Show(
            $"确定把以下项移入回收站吗？（可从回收站还原，非永久删除）\n\n{node.Path}\n\n大小：{node.SizeText}　来源：{node.Origin}",
            "移入回收站 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { ActionStatus = "已取消，未做任何改动。"; return; }

        ActionStatus = $"正在移入回收站「{node.Name}」…";
        var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, verdict));
        if (log.Result == ActionResult.Success)
        {
            node.MarkDeleted();
            _session?.NotifyRemoved(node.Path, node.RawSize, node.IsCleanable);   // A1: 广播给其它页 + 概览扣减
            ActionStatus = $"已移入回收站（可还原）：{node.Name}";
        }
        else
        {
            ActionStatus = $"操作未完成：{log.RejectReason}";
        }
    }

    // 问题#4: 手动处置高风险/识别不出的项 (如用户自己下载的文件夹)。强确认 (复选框勾选) → UserOverride 放行,
    // 仅放宽风险等级闸门; 系统关键黑名单/容器/占用红线照旧, 且仍仅移入回收站 (可恢复, 永不永久删除)。
    public async Task ManualRecycleAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanManualRecycle) return;

        var riskHint = $"风险桶：{node.BucketLabel}";
        var confirmed = ManualDeleteDialog.Confirm(
            System.Windows.Application.Current?.MainWindow, node.Name, node.Path, node.SizeText, node.Origin, riskHint);
        if (!confirmed) { ActionStatus = "已取消，未做任何改动。"; return; }

        var request = new ActionRequest(null, node.Path, ActionType.MoveToRecycleBin, UserOverride: true);

        ActionStatus = $"正在检查「{node.Name}」…";
        var verdict = await Task.Run(() => _services.SafetyGuard.Evaluate(request, null, node.ToRiskAssessment()));
        if (verdict.Outcome != GuardOutcome.Allowed)
        {
            // 即便用户强确认, 黑名单/容器/占用仍会拒 —— 如实告知红线。
            ActionStatus = string.IsNullOrWhiteSpace(verdict.RecommendedAlternative)
                ? $"仍不可删除：{verdict.Reason}"
                : $"仍不可删除：{verdict.Reason}　建议：{verdict.RecommendedAlternative}";
            return;
        }

        ActionStatus = $"正在移入回收站「{node.Name}」…";
        var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, verdict));
        if (log.Result == ActionResult.Success)
        {
            node.MarkDeleted();
            _session?.NotifyRemoved(node.Path, node.RawSize, node.IsCleanable);
            ActionStatus = $"已移入回收站（可还原）：{node.Name}";
        }
        else
        {
            ActionStatus = $"操作未完成：{log.RejectReason}";
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
