using System.IO;
using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Application;
using CleanScope.Core.Cleanup;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 首页 (T5.2): 盘符优先的扫描入口 + 扫描后"可回收速览 / 最划算的几步 / 官方清理手段"。
/// 只读扫描, 绝不删除 (文案明示)。官方清理手段经安全闸门 + 审计执行 (启动 Windows 自带工具)。
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly INavigationHost _host;

    public HomeViewModel(AppServices services, INavigationHost host)
    {
        _services = services;
        _host = host;
        AvailableDrives = ReadyDriveRoots();
        _targetPath = OfficialCleanupCatalog.SystemDrive();        // 盘符优先: 默认整个系统盘 (通常 C:\)
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => CanScan);
        ScanAllDrivesCommand = new AsyncRelayCommand(_ => ScanAllDrivesAsync(), _ => !_isScanning && AvailableDrives.Count > 0);
        ScanDriveCommand = new RelayCommand(p => { if (p is string root) TargetPath = root; });
        ViewListCommand = new RelayCommand(_host.ShowList, () => Session is not null);
        AdviseCommand = new AsyncRelayCommand(_ => AdviseAsync(), _ => CanAdvise);
        RunOfficialActionCommand = new AsyncRelayCommand(p => RunOfficialAsync(p as OfficialActionViewModel), _ => !_isScanning && !_officialBusy);
        OfficialActions = _services.OfficialActions.Select(a => new OfficialActionViewModel(a)).ToList();
        RefreshDrive();
    }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ScanAllDrivesCommand { get; }   // #3: 一次扫描整台电脑 (所有固定磁盘)
    public RelayCommand ScanDriveCommand { get; }        // 盘符芯片: 把目标设为某盘根

    // 深度分析的项数上限 (原"TopN"输入框已移除, 改用固定默认值, 普通用户不必关心)。
    private const int DefaultTopN = 200;
    public RelayCommand ViewListCommand { get; }
    public AsyncRelayCommand AdviseCommand { get; }      // 按需生成 AI 清理参谋 (脱敏后请求一次), 非自动
    public AsyncRelayCommand RunOfficialActionCommand { get; }  // P0: 执行某条官方清理手段

    /// <summary>本机已就绪的盘符根 (供"盘符优先"芯片), 如 C:\、D:\。</summary>
    public IReadOnlyList<string> AvailableDrives { get; }

    /// <summary>系统级官方清理手段 (P0): 关闭休眠/清空回收站/DISM/磁盘清理…，确定性目录。</summary>
    public IReadOnlyList<OfficialActionViewModel> OfficialActions { get; }
    public bool HasOfficialActions => OfficialActions.Count > 0;

    private string _officialStatus = "";
    public string OfficialStatus { get => _officialStatus; private set => SetField(ref _officialStatus, value); }

    /// <summary>AI 是否已配置 (决定是否显示按需 AI 入口)。</summary>
    public bool AiEnabled => _services.AiEnabled;

    private string _targetPath;
    public string TargetPath
    {
        get => _targetPath;
        set { if (SetField(ref _targetPath, value)) { RefreshDrive(); ScanCommand.RaiseCanExecuteChanged(); } }
    }

    private bool _adminMode;
    public bool AdminMode { get => _adminMode; set => SetField(ref _adminMode, value); }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetField(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(CanScan));
                ScanCommand.RaiseCanExecuteChanged();
                ScanAllDrivesCommand.RaiseCanExecuteChanged();
                RunOfficialActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanScan => !_isScanning && !string.IsNullOrWhiteSpace(_targetPath);

    private string _status = "选择磁盘后开始扫描。";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    // —— 磁盘容量 ——
    private string _driveInfo = "";
    public string DriveInfo { get => _driveInfo; private set => SetField(ref _driveInfo, value); }

    private double _driveUsedRatio;
    public double DriveUsedRatio { get => _driveUsedRatio; private set => SetField(ref _driveUsedRatio, value); }

    // —— 扫描后概览 ——
    public ScanSession? Session { get; private set; }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; private set => SetField(ref _hasResult, value); }

    private string _overviewTotal = "";
    public string OverviewTotal { get => _overviewTotal; private set => SetField(ref _overviewTotal, value); }

    private string _overviewReclaimable = "";
    public string OverviewReclaimable { get => _overviewReclaimable; private set => SetField(ref _overviewReclaimable, value); }

    private int _highRiskCount;
    public int HighRiskCount { get => _highRiskCount; private set => SetField(ref _highRiskCount, value); }

    // S-H: 整盘 AI 参谋 (跨项建议); 按需生成, 无 AI / 未生成则为空, 卡片隐藏。
    private string? _aiAdvice;
    public string? AiAdvice
    {
        get => _aiAdvice;
        private set { if (SetField(ref _aiAdvice, value)) { OnPropertyChanged(nameof(HasAiAdvice)); OnPropertyChanged(nameof(ShowAdviseButton)); AdviseCommand.RaiseCanExecuteChanged(); } }
    }
    public bool HasAiAdvice => !string.IsNullOrWhiteSpace(_aiAdvice);

    private bool _advising;
    private string _adviseStatus = "";
    public string AdviseStatus { get => _adviseStatus; private set => SetField(ref _adviseStatus, value); }

    // 仅在 AI 已配置、已有扫描结果、且尚未生成时可点 (生成后按钮隐藏, 改显建议卡)。
    public bool CanAdvise => _services.CleanupAdvisor is { Enabled: true } && HasResult && !HasAiAdvice && !_advising;
    public bool ShowAdviseButton => _services.CleanupAdvisor is { Enabled: true } && HasResult && !HasAiAdvice;

    // 按需: 用户点击才发一次脱敏聚合请求 (整盘跨项建议)。失败不阻断, 仅提示。
    // P1: 把"本机适用的官方手段 + 预估收益"一并喂给参谋, 让它给可执行、带优先级的行动计划 (而非泛泛而谈)。
    private async Task AdviseAsync()
    {
        if (Session is null || _services.CleanupAdvisor is not { Enabled: true }) return;
        _advising = true;
        AdviseCommand.RaiseCanExecuteChanged();
        var level = _services.SanitizationLevel;
        AdviseStatus = level switch
        {
            SanitizationLevel.Off => "正在生成 AI 清理建议（脱敏已关闭：附真实大项路径，建议更个性化）…",
            SanitizationLevel.Balanced => "正在生成 AI 清理建议（均衡脱敏：附大项名称，不含完整路径/用户名）…",
            _ => "正在生成 AI 清理建议（严格脱敏：仅占用汇总，不含具体项）…",
        };
        try
        {
            var summary = CleanupSummaryBuilder.From(Session.Report.Items);
            var concrete = BuildConcreteItems(level);
            var advice = await _services.CleanupAdvisor.AdviseAsync(summary, _services.OfficialActions, concrete);
            if (string.IsNullOrWhiteSpace(advice))
            {
                AdviseStatus = "AI 未能生成建议（以确定性的按类别/按软件清理为准）。";
                return;
            }
            Session.ApplyAiAdvice(advice);   // 写回报告, 让导出的报告也含此建议
            AiAdvice = advice;
            AdviseStatus = "";
        }
        catch
        {
            AdviseStatus = "AI 生成建议失败（以确定性的按类别/按软件清理为准）。";
        }
        finally
        {
            _advising = false;
            AdviseCommand.RaiseCanExecuteChanged();
        }
    }

    // 按脱敏档位给参谋附"具体大项", 让"关脱敏 = 更个性化"真正生效, 同时尊重三档隐私语义:
    //  关闭 → 真实完整路径 (最个性化); 均衡 → 仅叶子名 (无完整路径/用户名); 严格 → 不附 (仅聚合)。
    private IReadOnlyList<string>? BuildConcreteItems(SanitizationLevel level)
    {
        if (Session is null || level == SanitizationLevel.Strict) return null;
        var items = Session.Report.Items
            .Where(i => !i.IsContainer && i.ExclusiveSize > 0)
            .OrderByDescending(i => i.ExclusiveSize)
            .Take(15)
            .Select(i =>
            {
                var loc = level == SanitizationLevel.Off ? i.Path : Path.GetFileName(i.Path.TrimEnd('\\'));
                if (string.IsNullOrWhiteSpace(loc)) loc = i.Path;
                return $"{loc} | {Format.HumanSize(i.ExclusiveSize)} | 风险{i.RiskLevel}";
            })
            .ToList();
        return items.Count > 0 ? items : null;
    }

    // —— 概览"最划算的几步": 整棵树里最大的可清理项 (扁平去重), 取代旧"Top10 最大目录"(多为容器) ——
    public IReadOnlyList<OverviewItem> BestCleanable { get; private set; } = Array.Empty<OverviewItem>();

    private Task ScanAsync() => RunScanAsync(TargetPath);

    private async Task RunScanAsync(string target)
    {
        IsScanning = true;
        HasResult = false;
        Status = $"正在扫描 {target} …";
        try
        {
            var mode = AdminMode ? ScanMode.Admin : ScanMode.Normal;
            var options = new ScanOptions(target, DefaultTopN, mode);

            var lastTick = DateTime.MinValue;
            var progress = new Progress<ScanProgress>(p =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastTick).TotalMilliseconds < 120) return;
                lastTick = now;
                Status = $"扫描中… 已观察 {p.FilesScanned} 项　{p.CurrentPath}";
            });

            // 扫描默认不调用 AI (零 token 负担): 确定性的规则/风险/目录名启发已能覆盖绝大多数。
            // AI 仅在用户明确请求时按需触发 (详情页「AI 解释」、资源管理器右键「AI 识别」、概览「生成 AI 建议」)。
            // P1: buildTree 让扫描顺带产出全盘目录树, 供资源管理器浏览。
            var result = await _services.UseCase.ExecuteAsync(options, progress, default, AiMode.OnDemand, buildTree: true);

            // 决议项与完整分析按路径关联, 构造行 VM。
            var byPath = result.Analyses.ToDictionary(a => a.Node.Path, a => a);
            var rows = result.Decisions
                .Select(d => new FileRowViewModel(d, byPath[d.Path]))
                .ToList();

            var session = new ScanSession(target, result.Report, rows, result.Tree);
            _host.LoadSession(session);   // 触发各页加载 (含本页 OnSessionLoaded)
            Status = $"扫描完成：{rows.Count} 项。下方是最划算的几步与官方清理手段；点「去可清理清单」可批量处理。";
        }
        catch (DirectoryNotFoundException)
        {
            Status = $"路径不存在或无法访问：{target}";
        }
        catch (Exception ex)
        {
            Status = $"扫描失败：{ex.GetType().Name} — {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    // #3 整台电脑扫描: 逐个固定磁盘跑同一套裁决链 + 目录树, 再合并成一个会话 (虚拟根「整台电脑」)。
    // 与单盘扫描同口径; 某个盘不可访问则跳过, 不影响其它盘。
    private async Task ScanAllDrivesAsync()
    {
        var drives = AvailableDrives;
        if (drives.Count == 0) { Status = "未发现可扫描的固定磁盘。"; return; }

        IsScanning = true;
        HasResult = false;
        var startedAt = DateTime.UtcNow;
        var mode = AdminMode ? ScanMode.Admin : ScanMode.Normal;
        try
        {
            var allRows = new List<FileRowViewModel>();
            var allDecisions = new List<DecisionItem>();
            var driveTrees = new List<ScanTreeNode>();
            long totalSize = 0, fileCount = 0;
            var index = 0;

            foreach (var root in drives)
            {
                index++;
                var which = index;
                Status = $"正在扫描整台电脑（{which}/{drives.Count}）：{root} …";
                var lastTick = DateTime.MinValue;
                var progress = new Progress<ScanProgress>(p =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastTick).TotalMilliseconds < 120) return;
                    lastTick = now;
                    Status = $"扫描整台电脑（{which}/{drives.Count}）{root}… 已观察 {p.FilesScanned} 项　{p.CurrentPath}";
                });

                ScanAndAnalyzeResult result;
                try
                {
                    var options = new ScanOptions(root, DefaultTopN, mode);
                    result = await _services.UseCase.ExecuteAsync(options, progress, default, AiMode.OnDemand, buildTree: true);
                }
                catch
                {
                    continue;   // 某盘不可访问 (权限/未就绪) → 跳过, 继续其余盘
                }

                var byPath = result.Analyses.ToDictionary(a => a.Node.Path, a => a);
                allRows.AddRange(result.Decisions.Select(d => new FileRowViewModel(d, byPath[d.Path])));
                allDecisions.AddRange(result.Decisions);
                if (result.Tree is not null) driveTrees.Add(result.Tree);
                totalSize += result.Report.Task.TotalSize ?? 0;
                fileCount += result.Report.Task.FileCount ?? 0;
            }

            // 虚拟根「整台电脑」, 各盘目录树作为其子节点 (Origin/Purpose 仅说明性, 不参与裁决)。
            var computer = new ScanTreeNode("", "整台电脑", totalSize, RiskLevel.C,
                isContainer: true, isCleanable: false, origin: "本机",
                purpose: $"本次扫描的 {driveTrees.Count} 个磁盘", recommendedAction: "展开浏览各磁盘");
            computer.Children.AddRange(driveTrees);

            var task = new ScanTask(0, "整台电脑", mode, ScanStatus.Completed,
                startedAt, DateTime.UtcNow, totalSize, fileCount, _services.AppVersion);
            var session = new ScanSession("整台电脑", new ScanReport(task, allDecisions), allRows, computer);
            _host.LoadSession(session);
            Status = $"整台电脑扫描完成：{drives.Count} 个磁盘、{allRows.Count} 项。下方是最划算的几步与官方清理手段；点「去可清理清单」可批量处理。";
        }
        catch (Exception ex)
        {
            Status = $"整台电脑扫描失败：{ex.GetType().Name} — {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void OnSessionLoaded(ScanSession session)
    {
        if (Session is not null) Session.ItemRemoved -= OnItemRemoved;
        Session = session;
        Session.ItemRemoved += OnItemRemoved;   // A3: 别处删除 → 概览数字随之扣减
        HasResult = true;
        OverviewTotal = $"{Format.HumanSize(session.TotalSize)}（{session.FileCount} 项）";
        HighRiskCount = session.HighRiskCount;
        AiAdvice = session.AiCleanupAdvice;
        AdviseStatus = "";
        RecomputeOverview();
        OnPropertyChanged(nameof(ShowAdviseButton));
        ViewListCommand.RaiseCanExecuteChanged();
        AdviseCommand.RaiseCanExecuteChanged();
    }

    private void OnItemRemoved(string path) => RecomputeOverview();

    // A3: 按"已扣减已清理"的口径重算概览可清理估算与"最划算的几步"(排除已删项)。
    private void RecomputeOverview()
    {
        var s = Session;
        if (s is null) return;
        var processed = s.RemovedCount > 0 ? $"，已清理 {s.RemovedCount} 项" : "";
        OverviewReclaimable = $"{Format.HumanSize(s.RemainingReclaimable)}（约 {Math.Max(0, s.TreeCleanableCount - s.RemovedCount)} 处{processed}）";
        BestCleanable = s.BestCleanable(24)
            .Where(n => !s.IsRemoved(n.Path))
            .Take(8)
            .Select(n => new OverviewItem(Format.HumanSize(n.Size), n.Name, n.Path, n.Origin))
            .ToList();
        OnPropertyChanged(nameof(BestCleanable));
        OnPropertyChanged(nameof(HasBestCleanable));
    }

    // A3: 回到首页时刷新磁盘容量条 (官方清理/删除后空间已变, 例如关闭休眠/清空回收站)。
    public void OnActivated()
    {
        RefreshDrive();
        RecomputeOverview();
    }

    public bool HasBestCleanable => BestCleanable.Count > 0;

    // #1: 官方清理执行中的可观察标志 → 卡片显示不确定进度条 + 进度文案 (如"清空回收站"也有动态反馈)。
    private bool _officialBusy;
    public bool IsOfficialRunning
    {
        get => _officialBusy;
        private set { if (SetField(ref _officialBusy, value)) RunOfficialActionCommand.RaiseCanExecuteChanged(); }
    }

    // —— P0: 执行一条官方清理手段 (启动系统自带工具 / 应用内隐藏执行官方命令; 我们不替它删文件) ——
    private async Task RunOfficialAsync(OfficialActionViewModel? vm)
    {
        if (vm is null || _officialBusy) return;
        var a = vm.Action;
        // 执行前预告: 拉起界面 vs 应用内执行 + (需提权时) UAC 会弹窗, 让用户知道接下来会发生什么、不必慌。
        var surfaceLine = a.Surface == CleanupSurface.OpensWindowsUi
            ? "执行方式：会打开 Windows 自带的界面，由你在其中操作（CleanScope 不替你删东西）。"
            : a.NeedsAdmin
                ? "执行方式：CleanScope 在后台执行官方命令（无黑窗）；接下来 Windows 会弹一个授权(UAC)窗口，点「是」即可。"
                : "执行方式：CleanScope 在后台执行官方命令（无黑窗），完成后显示结果。";
        var recover = a.Reversible ? $"能否恢复：可以。{a.Undo}" : $"能否恢复：❌ 不可恢复。{a.Undo}";
        // 问题#3: 弹窗讲清"做什么/后果/能否恢复/如何恢复/怎么执行", 让用户明白这一步到底干什么。
        var confirm = MessageBox.Show(
            $"【{a.Title}】\n\n" +
            $"做什么：{a.Description}\n\n" +
            $"执行后果：{(string.IsNullOrWhiteSpace(a.Consequence) ? a.Description : a.Consequence)}\n\n" +
            $"{recover}\n\n" +
            $"{surfaceLine}\n\n" +
            $"实际执行的命令：{a.Payload}\n\n" +
            $"提示：{a.Note}\n\n确定继续吗？",
            $"确认执行：{a.Title} — CleanScope",
            MessageBoxButton.OKCancel,
            a.Reversible ? MessageBoxImage.Information : MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { OfficialStatus = "已取消，未做任何改动。"; return; }

        IsOfficialRunning = true;
        try
        {
            if (a.Surface == CleanupSurface.OpensWindowsUi)
            {
                // 直接拉起 Windows 自带 GUI / 设置页 (cleanmgr / ms-settings:) —— 无控制台, 即时返回。
                var request = new ActionRequest(null, a.Payload, ActionType.OpenSettings);
                var approval = _services.SafetyGuard.Evaluate(request, null, null);
                var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, approval));
                OfficialStatus = log.Result == ActionResult.Success
                    ? $"已打开「{a.Title}」，请在弹出的 Windows 界面中勾选/确认完成操作。"
                    : $"未能打开「{a.Title}」：{log.RejectReason}";
            }
            else
            {
                // 应用内隐藏执行官方命令 (无 cmd 黑框); 显示进度 + 前后磁盘空间差 = 释放量。
                var beforeFree = FreeBytesOfSystemDrive();
                OfficialStatus = a.NeedsAdmin
                    ? $"正在执行「{a.Title}」… 若弹出 Windows 授权(UAC)窗口，请点「是」。"
                    : $"正在执行「{a.Title}」…（请稍候）";

                var request = new ActionRequest(null, "", ActionType.RunCleanupCommand, a.Payload, Elevate: a.NeedsAdmin);
                var approval = _services.SafetyGuard.Evaluate(request, null, null);
                var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, approval));
                if (log.Result == ActionResult.Success)
                {
                    var freed = Math.Max(0, FreeBytesOfSystemDrive() - beforeFree);
                    OfficialStatus = freed > 0
                        ? $"✓ 已完成「{a.Title}」，释放约 {Format.HumanSize(freed)}。"
                        : $"✓ 已完成「{a.Title}」。";
                    Toast.Show(freed > 0 ? $"已完成「{a.Title}」，释放约 {Format.HumanSize(freed)}" : $"已完成「{a.Title}」", ToastKind.Success);
                }
                else
                {
                    OfficialStatus = $"✗ 未能完成「{a.Title}」：{log.RejectReason}";
                    Toast.Error($"未能完成「{a.Title}」：{log.RejectReason}");
                }
            }
        }
        finally
        {
            IsOfficialRunning = false;
            RefreshDrive();   // A3: 刷新磁盘条 (关休眠/清回收站等即时生效)
        }
    }

    // 系统盘可用空间 (字节), 用于"执行前后差 = 释放量"。失败返回 0 (则不显示释放量)。
    private static long FreeBytesOfSystemDrive()
    {
        try
        {
            var root = Path.GetPathRoot(OfficialCleanupCatalog.SystemDrive());
            if (string.IsNullOrEmpty(root)) return 0;
            var di = new DriveInfo(root);
            return di.IsReady ? di.AvailableFreeSpace : 0;
        }
        catch { return 0; }
    }

    private void RefreshDrive()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(TargetPath));
            if (string.IsNullOrEmpty(root)) { DriveInfo = ""; return; }
            var di = new DriveInfo(root);
            if (!di.IsReady) { DriveInfo = $"{root} 未就绪"; return; }
            var used = di.TotalSize - di.AvailableFreeSpace;
            DriveUsedRatio = di.TotalSize > 0 ? (double)used / di.TotalSize : 0;
            // 减重: 一行说清 (盘 · 占比 · 仅剩 · 状态), 不再罗列已用/共/可用三个数。
            var status = DriveUsedRatio >= 0.9 ? "空间紧张" : DriveUsedRatio >= 0.7 ? "建议清理" : "空间充足";
            DriveInfo = $"{root.TrimEnd('\\')}　{DriveUsedRatio * 100:0}%　仅剩 {Format.HumanSize(di.AvailableFreeSpace)}　{status}";
        }
        catch
        {
            DriveInfo = "";
            DriveUsedRatio = 0;
        }
    }

    private static IReadOnlyList<string> ReadyDriveRoots()
    {
        try
        {
            return System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch { return new[] { @"C:\" }; }
    }
}

/// <summary>概览"最划算的几步"行 (只读预览; 操作在可清理清单页批量进行)。</summary>
public sealed record OverviewItem(string SizeText, string Name, string Path, string Origin);

/// <summary>官方清理手段的展示包装 (P0): 标题/说明/预估收益/标签 + 绑定到首页的执行命令。</summary>
public sealed class OfficialActionViewModel
{
    public OfficialActionViewModel(OfficialCleanupAction action) => Action = action;

    public OfficialCleanupAction Action { get; }
    public string Title => Action.Title;
    public string Description => Action.Description;
    public string Note => Action.Note;
    public bool NeedsAdmin => Action.NeedsAdmin;
    public string AdminBadge => Action.NeedsAdmin ? "管理员" : "";
    public bool Detected => Action.Detected;

    // 问题#3: 卡片上直接标"可恢复 / 不可恢复", 让用户点之前心里有数。
    public string ReversibleBadge => Action.Reversible ? "可恢复" : "不可恢复";
    public bool IsIrreversible => !Action.Reversible;

    // 执行表面提示 (避免黑框疑虑): 拉起 Windows 界面 vs 应用内执行; 按钮文案也随之区分, 且都"会先确认"。
    public bool OpensWindowsUi => Action.Surface == CleanupSurface.OpensWindowsUi;
    public string SurfaceTag => OpensWindowsUi ? "打开 Windows 界面" : "应用内执行";
    public string RunButtonText => OpensWindowsUi ? "打开（先确认）" : "执行（先确认）";
    /// <summary>有预估收益则显示"约 X" (减重: 去掉零信息的"检测到")。</summary>
    public string SavingsText => Action.EstimatedBytes > 0
        ? $"约 {Common.Format.HumanSize(Action.EstimatedBytes)}"
        : "";
    public bool HasSavings => !string.IsNullOrEmpty(SavingsText);
}
