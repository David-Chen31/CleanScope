using System.Collections.ObjectModel;
using System.IO;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 报告导出 + 忽略名单管理 (T5.5)。
/// 导出经 <see cref="Domain.Abstractions.IReportExporter"/> 写 Markdown; 忽略名单经仓储增删 (仅本地)。
/// 这些都不触碰被分析的文件本身, 与删除红线无关。
/// </summary>
public sealed class ReportViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly INavigationHost _host;
    private ScanSession? _session;

    public ReportViewModel(AppServices services, INavigationHost host)
    {
        _services = services;
        _host = host;
        _exportPath = DefaultExportPath();
        ExportCommand = new AsyncRelayCommand(_ => ExportAsync(), _ => _session is not null);
        AddIgnoreCommand = new AsyncRelayCommand(_ => AddIgnoreAsync(), _ => !string.IsNullOrWhiteSpace(NewIgnorePath));
        RemoveIgnoreCommand = new AsyncRelayCommand(RemoveIgnoreAsync);
        OpenRecycleBinCommand = new RelayCommand(OpenRecycleBin);
        _ = RefreshIgnoresAsync();
        _ = RefreshHistoryAsync();
    }

    public AsyncRelayCommand ExportCommand { get; }
    public AsyncRelayCommand AddIgnoreCommand { get; }
    public AsyncRelayCommand RemoveIgnoreCommand { get; }
    public RelayCommand OpenRecycleBinCommand { get; }   // F3: 打开系统回收站 (在其中还原)

    public ObservableCollection<IgnoreEntryViewModel> Ignores { get; } = new();

    // F3: 回收历史 (跨会话持久, 来自本地审计日志)。强化"删除可还原"心智。
    public ObservableCollection<RecycleHistoryViewModel> RecycleHistory { get; } = new();
    public bool HasRecycleHistory => RecycleHistory.Count > 0;

    private string _historyStatus = "";
    public string HistoryStatus { get => _historyStatus; private set => SetField(ref _historyStatus, value); }
    public ObservableCollection<CleanupCategoryViewModel> CleanupCategories { get; } = new();

    private string _reclaimableTotal = "";
    public string ReclaimableTotal { get => _reclaimableTotal; private set => SetField(ref _reclaimableTotal, value); }

    public bool HasCategories => CleanupCategories.Count > 0;

    private string _exportPath;
    public string ExportPath { get => _exportPath; set => SetField(ref _exportPath, value); }

    private string _exportStatus = "";
    public string ExportStatus { get => _exportStatus; private set => SetField(ref _exportStatus, value); }

    private string _newIgnorePath = "";
    public string NewIgnorePath
    {
        get => _newIgnorePath;
        set { if (SetField(ref _newIgnorePath, value)) AddIgnoreCommand.RaiseCanExecuteChanged(); }
    }

    private string _newIgnoreReason = "";
    public string NewIgnoreReason { get => _newIgnoreReason; set => SetField(ref _newIgnoreReason, value); }

    private string _ignoreStatus = "";
    public string IgnoreStatus { get => _ignoreStatus; private set => SetField(ref _ignoreStatus, value); }

    public void Load(ScanSession session)
    {
        _session = session;
        ExportStatus = "";
        CleanupCategories.Clear();
        foreach (var c in session.CleanupCategories) CleanupCategories.Add(new CleanupCategoryViewModel(c));
        // A1/A3: 扣减已清理后的剩余可回收 (与概览同口径)。
        ReclaimableTotal = Common.Format.HumanSize(session.RemainingReclaimable);
        OnPropertyChanged(nameof(HasCategories));
        ExportCommand.RaiseCanExecuteChanged();
    }

    private async Task ExportAsync()
    {
        if (_session is null) return;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(ExportPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var exporter = _services.ReportExporterFor(ExportPath);
            await exporter.ExportAsync(_session.Report, ExportPath);
            ExportStatus = $"已导出报告（{exporter.Format}）：{Path.GetFullPath(ExportPath)}";
        }
        catch (Exception ex)
        {
            ExportStatus = $"导出失败：{ex.GetType().Name} — {ex.Message}";
        }
    }

    private async Task AddIgnoreAsync()
    {
        var path = NewIgnorePath.Trim();
        if (path.Length == 0) return;
        try
        {
            var reason = string.IsNullOrWhiteSpace(NewIgnoreReason) ? null : NewIgnoreReason.Trim();
            await _services.IgnoreRepository.AddAsync(
                new IgnoreEntry(0, path, Domain.Enums.MatchType.Exact, reason, DateTime.UtcNow));
            NewIgnorePath = "";
            NewIgnoreReason = "";
            IgnoreStatus = "已加入忽略名单。";
            await RefreshIgnoresAsync();
        }
        catch (Exception ex)
        {
            IgnoreStatus = $"添加失败：{ex.Message}";
        }
    }

    private async Task RemoveIgnoreAsync(object? parameter)
    {
        if (parameter is not IgnoreEntryViewModel vm) return;
        try
        {
            await _services.IgnoreRepository.RemoveAsync(vm.Id);
            IgnoreStatus = "已移除忽略项。";
            await RefreshIgnoresAsync();
        }
        catch (Exception ex)
        {
            IgnoreStatus = $"移除失败：{ex.Message}";
        }
    }

    /// <summary>A5/F3: 切回本页时刷新忽略名单 + 回收历史 (别处可能刚回收了文件)。</summary>
    public void RefreshOnShow() { _ = RefreshIgnoresAsync(); _ = RefreshHistoryAsync(); }

    // F3: 从本地审计日志读取"已成功移入回收站"的记录 (跨会话持久)。
    private async Task RefreshHistoryAsync()
    {
        try
        {
            var logs = await _services.AuditLog.GetRecentAsync(200);
            RecycleHistory.Clear();
            foreach (var l in logs.Where(l => l.Action == ActionType.MoveToRecycleBin && l.Result == ActionResult.Success))
                RecycleHistory.Add(new RecycleHistoryViewModel(l));
            OnPropertyChanged(nameof(HasRecycleHistory));
            HistoryStatus = RecycleHistory.Count == 0
                ? "暂无回收记录。你在本应用移入回收站的项会出现在这里，随时可还原。"
                : $"共 {RecycleHistory.Count} 条回收记录（均可在回收站还原）。";
        }
        catch (Exception ex)
        {
            HistoryStatus = $"读取回收历史失败：{ex.Message}";
        }
    }

    private void OpenRecycleBin()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
            HistoryStatus = "已打开回收站：选中要恢复的项 → 右键「还原」即可复位到原处。";
        }
        catch (Exception ex) { HistoryStatus = $"打开回收站失败：{ex.Message}"; }
    }

    private async Task RefreshIgnoresAsync()
    {
        try
        {
            var all = await _services.IgnoreRepository.GetAllAsync();
            Ignores.Clear();
            foreach (var e in all) Ignores.Add(new IgnoreEntryViewModel(e));
        }
        catch (Exception ex)
        {
            IgnoreStatus = $"读取忽略名单失败：{ex.Message}";
        }
    }

    private static string DefaultExportPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Directory.Exists(docs) ? docs : Path.GetTempPath();
        return Path.Combine(dir, $"CleanScope-报告-{DateTime.Now:yyyyMMdd-HHmmss}.md");
    }
}

/// <summary>可清理类别展示 (S3): 类别 + 项数 + 可回收大小 + 官方清理方式。</summary>
public sealed class CleanupCategoryViewModel
{
    public CleanupCategoryViewModel(CleanupCategory c)
    {
        Name = c.Name;
        ItemCount = c.ItemCount;
        ReclaimableText = Common.Format.HumanSize(c.ReclaimableSize);
        RiskText = c.TopRisk.ToString();
        RecommendedAction = c.RecommendedAction;
    }

    public string Name { get; }
    public int ItemCount { get; }
    public string ReclaimableText { get; }
    public string RiskText { get; }
    public string RecommendedAction { get; }
}

/// <summary>F3: 回收历史条目展示 (来自审计日志的一条"移入回收站"记录)。</summary>
public sealed class RecycleHistoryViewModel
{
    public RecycleHistoryViewModel(ActionLog log)
    {
        Path = log.TargetPath ?? "";
        Name = LeafName(Path);
        When = log.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        Recoverable = log.Recoverable;
    }

    public string Path { get; }
    public string Name { get; }
    public string When { get; }
    public bool Recoverable { get; }

    private static string LeafName(string p)
    {
        var s = p.TrimEnd('\\', '/');
        var i = s.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 && i + 1 < s.Length ? s[(i + 1)..] : s;
    }
}

/// <summary>忽略名单条目展示。</summary>
public sealed class IgnoreEntryViewModel
{
    public IgnoreEntryViewModel(IgnoreEntry e)
    {
        Id = e.Id;
        PathOrPattern = e.PathOrPattern;
        MatchType = e.MatchType.ToString();
        Reason = e.Reason;
        CreatedAt = e.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public long Id { get; }
    public string PathOrPattern { get; }
    public string MatchType { get; }
    public string? Reason { get; }
    public string CreatedAt { get; }
}
