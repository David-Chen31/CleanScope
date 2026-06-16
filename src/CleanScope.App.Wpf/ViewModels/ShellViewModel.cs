using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 应用外壳 (T5.1): 持有导航状态与各页 VM, 充当 <see cref="INavigationHost"/>。
/// 侧栏切换 概览/列表/报告; 列表选中行 → 详情。会话结果在此集中保存供各页读取。
/// </summary>
public sealed class ShellViewModel : ViewModelBase, INavigationHost
{
    private readonly HomeViewModel _home;
    private readonly FileListViewModel _list;
    private readonly ReportViewModel _report;
    private readonly FileDetailViewModel _detail;

    public ShellViewModel(AppServices services)
    {
        Services = services;
        _home = new HomeViewModel(services, this);
        _list = new FileListViewModel(this);
        _report = new ReportViewModel(services, this);
        _detail = new FileDetailViewModel(services, this);

        GoHomeCommand = new RelayCommand(ShowHome);
        GoListCommand = new RelayCommand(ShowList, () => Session is not null);
        GoReportCommand = new RelayCommand(ShowReport, () => Session is not null);

        _current = _home;
        AiBadge = services.AiEnabled ? "AI 解释: 已启用 (脱敏后出云)" : "AI 解释: 未配置 (纯本地)";
    }

    public AppServices Services { get; }
    public string AiBadge { get; }
    public string Title => $"CleanScope {Services.AppVersion} — 只读分析，绝不删除你的文件";

    private object _current;
    public object CurrentView
    {
        get => _current;
        private set => SetField(ref _current, value);
    }

    public ScanSession? Session { get; private set; }

    public RelayCommand GoHomeCommand { get; }
    public RelayCommand GoListCommand { get; }
    public RelayCommand GoReportCommand { get; }

    public void LoadSession(ScanSession session)
    {
        Session = session;
        _list.Load(session);
        _report.Load(session);
        _home.OnSessionLoaded(session);
        GoListCommand.RaiseCanExecuteChanged();
        GoReportCommand.RaiseCanExecuteChanged();
    }

    public void ShowHome() => CurrentView = _home;
    public void ShowList() => CurrentView = _list;
    public void ShowReport() => CurrentView = _report;

    public void ShowDetail(FileRowViewModel row)
    {
        _detail.Show(row);
        CurrentView = _detail;
    }
}
