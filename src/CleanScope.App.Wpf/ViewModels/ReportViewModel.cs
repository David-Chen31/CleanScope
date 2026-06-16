using System.Collections.ObjectModel;
using System.IO;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;

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
        _ = RefreshIgnoresAsync();
    }

    public AsyncRelayCommand ExportCommand { get; }
    public AsyncRelayCommand AddIgnoreCommand { get; }
    public AsyncRelayCommand RemoveIgnoreCommand { get; }

    public ObservableCollection<IgnoreEntryViewModel> Ignores { get; } = new();

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
        ExportCommand.RaiseCanExecuteChanged();
    }

    private async Task ExportAsync()
    {
        if (_session is null) return;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(ExportPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await _services.ReportExporter.ExportAsync(_session.Report, ExportPath);
            ExportStatus = $"已导出报告：{Path.GetFullPath(ExportPath)}";
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
