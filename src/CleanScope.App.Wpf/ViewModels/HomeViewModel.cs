using System.IO;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Application;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 首页 (T5.2): C 盘容量概览 + 扫描入口; 扫描完成后展示 TopN 大目录 / 高风险数 / 可清理估算。
/// 只读扫描, 绝不删除 (文案明示)。扫描经编排用例 (裁决链), 进度回调更新 UI。
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly INavigationHost _host;

    public HomeViewModel(AppServices services, INavigationHost host)
    {
        _services = services;
        _host = host;
        _targetPath = SuggestDefaultTarget();
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => CanScan);
        ViewListCommand = new RelayCommand(_host.ShowList, () => Session is not null);
        RefreshDrive();
    }

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand ViewListCommand { get; }

    private string _targetPath;
    public string TargetPath
    {
        get => _targetPath;
        set { if (SetField(ref _targetPath, value)) { RefreshDrive(); ScanCommand.RaiseCanExecuteChanged(); } }
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
            }
        }
    }

    public bool CanScan => !_isScanning && !string.IsNullOrWhiteSpace(_targetPath);

    private string _status = "选择目标路径后开始扫描。CleanScope 只读分析，不会删除任何文件。";
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

    // S-H: 整盘 AI 参谋 (跨项建议); 无 AI / 未生成则为空, 卡片隐藏。
    private string? _aiAdvice;
    public string? AiAdvice { get => _aiAdvice; private set { if (SetField(ref _aiAdvice, value)) OnPropertyChanged(nameof(HasAiAdvice)); } }
    public bool HasAiAdvice => !string.IsNullOrWhiteSpace(_aiAdvice);

    public IReadOnlyList<FileRowViewModel> TopDirectories { get; private set; } = Array.Empty<FileRowViewModel>();

    private async Task ScanAsync()
    {
        IsScanning = true;
        HasResult = false;
        Status = $"正在扫描 {TargetPath} …";
        try
        {
            var mode = AdminMode ? ScanMode.Admin : ScanMode.Normal;
            var options = new ScanOptions(TargetPath, Math.Max(1, TopN), mode);

            var lastTick = DateTime.MinValue;
            var progress = new Progress<ScanProgress>(p =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastTick).TotalMilliseconds < 120) return;
                lastTick = now;
                Status = $"扫描中… 已观察 {p.FilesScanned} 项　{p.CurrentPath}";
            });

            // AI 配置后, 扫描末尾对无主未知项做调查/归因兜底 (S-C/S-G, 上限受控); 否则秒级返回。
            var aiMode = _services.AiEnabled ? AiMode.InvestigateUnknowns : AiMode.OnDemand;
            var result = await _services.UseCase.ExecuteAsync(options, progress, default, aiMode);

            // 决议项与完整分析按路径关联, 构造行 VM。
            var byPath = result.Analyses.ToDictionary(a => a.Node.Path, a => a);
            var rows = result.Decisions
                .Select(d => new FileRowViewModel(d, byPath[d.Path]))
                .ToList();

            // S-H: 整盘 AI 参谋 (脱敏聚合 → 跨项建议)。失败/未启用 → 跳过, 不阻断结果。
            var report = result.Report;
            if (_services.CleanupAdvisor is { Enabled: true })
            {
                Status = "正在生成 AI 清理参谋…";
                var advice = await _services.CleanupAdvisor.AdviseAsync(CleanupSummaryBuilder.From(result.Decisions));
                if (!string.IsNullOrWhiteSpace(advice)) report = report with { AiCleanupAdvice = advice };
            }

            var session = new ScanSession(TargetPath, report, rows);
            _host.LoadSession(session);   // 触发各页加载 (含本页 OnSessionLoaded)
            Status = $"扫描完成：{rows.Count} 项，用时已计入报告。点击「查看清单」浏览明细。";
        }
        catch (DirectoryNotFoundException)
        {
            Status = $"路径不存在或无法访问：{TargetPath}";
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
        Session = session;
        HasResult = true;
        OverviewTotal = $"{Format.HumanSize(session.TotalSize)}（{session.FileCount} 项）";
        OverviewReclaimable = Format.HumanSize(session.ReclaimableEstimate);
        HighRiskCount = session.HighRiskCount;
        AiAdvice = session.AiCleanupAdvice;
        TopDirectories = session.Rows.OrderByDescending(r => r.Size).Take(10).ToList();
        OnPropertyChanged(nameof(TopDirectories));
        ViewListCommand.RaiseCanExecuteChanged();
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

    private static string SuggestDefaultTarget()
    {
        // 默认指向用户本地缓存目录 (高价值、低风险扫描起点), 回退到用户主目录。
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Directory.Exists(local) ? local
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
