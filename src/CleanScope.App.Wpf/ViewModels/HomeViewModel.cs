using System.IO;
using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Application;
using CleanScope.Core.Cleanup;
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
        QuickScanCommand = new AsyncRelayCommand(_ => QuickScanAsync(), _ => CanScan);
        ScanDriveCommand = new RelayCommand(p => { if (p is string root) TargetPath = root; });
        ViewListCommand = new RelayCommand(_host.ShowList, () => Session is not null);
        AdviseCommand = new AsyncRelayCommand(_ => AdviseAsync(), _ => CanAdvise);
        RunOfficialActionCommand = new AsyncRelayCommand(p => RunOfficialAsync(p as OfficialActionViewModel), _ => !_isScanning);
        OfficialActions = _services.OfficialActions.Select(a => new OfficialActionViewModel(a)).ToList();
        RefreshDrive();
    }

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand QuickScanCommand { get; }   // 仅高价值区 (AppData) 快速扫描
    public RelayCommand ScanDriveCommand { get; }        // 盘符芯片: 把目标设为某盘根
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
        set { if (SetField(ref _targetPath, value)) { RefreshDrive(); ScanCommand.RaiseCanExecuteChanged(); QuickScanCommand.RaiseCanExecuteChanged(); } }
    }

    private int _topN = 200;
    public int TopN { get => _topN; set => SetField(ref _topN, value); }

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
                QuickScanCommand.RaiseCanExecuteChanged();
                RunOfficialActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanScan => !_isScanning && !string.IsNullOrWhiteSpace(_targetPath);

    private string _status = "选择要清理的磁盘后开始扫描。CleanScope 只读分析，不会删除任何文件。";
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
        AdviseStatus = "正在生成 AI 清理建议（脱敏后请求一次，仅供参考）…";
        try
        {
            var summary = CleanupSummaryBuilder.From(Session.Report.Items);
            var advice = await _services.CleanupAdvisor.AdviseAsync(summary, _services.OfficialActions);
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

    // —— 概览"最划算的几步": 整棵树里最大的可清理项 (扁平去重), 取代旧"Top10 最大目录"(多为容器) ——
    public IReadOnlyList<OverviewItem> BestCleanable { get; private set; } = Array.Empty<OverviewItem>();

    private Task ScanAsync() => RunScanAsync(TargetPath);

    // 快速扫描: 仅扫高价值区 (用户 AppData\Local —— 缓存/临时的主场), 更快、零权限问题。
    private Task QuickScanAsync()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return RunScanAsync(Directory.Exists(local) ? local : TargetPath);
    }

    private async Task RunScanAsync(string target)
    {
        IsScanning = true;
        HasResult = false;
        Status = $"正在扫描 {target} …";
        try
        {
            var mode = AdminMode ? ScanMode.Admin : ScanMode.Normal;
            var options = new ScanOptions(target, Math.Max(1, TopN), mode);

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

    // —— P0: 执行一条官方清理手段 (启动 Windows 自带工具; 我们不替它删文件) ——
    private async Task RunOfficialAsync(OfficialActionViewModel? vm)
    {
        if (vm is null) return;
        var a = vm.Action;
        var admin = a.NeedsAdmin ? "\n\n⚠ 需要管理员权限：若本程序非管理员启动，命令可能失败，请以管理员身份重开后再试。" : "";
        var confirm = MessageBox.Show(
            $"将执行官方清理手段：\n\n【{a.Title}】\n{a.Description}\n\n执行内容：{a.Payload}\n\n说明：{a.Note}{admin}\n\nCleanScope 不替你删除文件，只启动 Windows 自带工具，过程对你可见。确定继续吗？",
            "执行官方清理 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { OfficialStatus = "已取消。"; return; }

        // OpenSettings 经 TargetPath 携带 ms-settings: URI; RunCleanupCommand 经 Payload 携带命令。
        var isSettings = a.ExecAction == ActionType.OpenSettings;
        var request = new ActionRequest(null, isSettings ? a.Payload : "", a.ExecAction, isSettings ? null : a.Payload);
        var approval = _services.SafetyGuard.Evaluate(request, null, null);   // 非破坏性辅助操作 → 放行
        var log = await _services.ActionExecutor.ExecuteAsync(request, approval);
        OfficialStatus = log.Result == ActionResult.Success
            ? $"已启动「{a.Title}」（请在弹出的工具/终端中完成操作）。完成后磁盘容量会在回到本页时刷新。"
            : $"未能执行「{a.Title}」：{log.RejectReason}";
        RefreshDrive();   // A3: 尽力刷新磁盘条 (清空回收站等即时生效的能立刻反映)
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
            DriveInfo = $"{root}  已用 {Format.HumanSize(used)} / 共 {Format.HumanSize(di.TotalSize)}" +
                        $"（可用 {Format.HumanSize(di.AvailableFreeSpace)}）";
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
    /// <summary>有预估收益则显示"约 X"，否则显示是否检测到机会。</summary>
    public string SavingsText => Action.EstimatedBytes > 0
        ? $"约 {Common.Format.HumanSize(Action.EstimatedBytes)}"
        : (Action.Detected ? "检测到" : "");
    public bool HasSavings => !string.IsNullOrEmpty(SavingsText);
}
