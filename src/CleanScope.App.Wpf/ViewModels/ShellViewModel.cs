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
    private readonly SpaceMapViewModel _map;
    private readonly ExplorerViewModel _explorer;
    private readonly SpaceBySoftwareViewModel _software;
    private readonly ReportViewModel _report;
    private readonly FileDetailViewModel _detail;

    public ShellViewModel(AppServices services)
    {
        Services = services;
        _home = new HomeViewModel(services, this);
        _list = new FileListViewModel(services, this);
        _map = new SpaceMapViewModel(this);
        _explorer = new ExplorerViewModel(services);
        _software = new SpaceBySoftwareViewModel(this);
        _report = new ReportViewModel(services, this);
        _detail = new FileDetailViewModel(services, this);

        GoHomeCommand = new RelayCommand(ShowHome);
        GoListCommand = new RelayCommand(ShowList, () => Session is not null);
        GoMapCommand = new RelayCommand(ShowMap, () => Session is not null);
        GoExplorerCommand = new RelayCommand(ShowExplorer, () => Session is not null);
        GoBySoftwareCommand = new RelayCommand(ShowBySoftware, () => Session is not null);
        GoReportCommand = new RelayCommand(ShowReport, () => Session is not null);

        _current = _home;
        AiBadge = services.AiEnabled ? "AI 解释: 已启用 (脱敏后出云)" : "AI 解释: 未配置 (纯本地)";
    }

    public AppServices Services { get; }
    public string AiBadge { get; }
    public string Title => $"CleanScope {Services.AppVersion} — 分析为主；删除仅对可清理项、且只移入回收站（可还原）";

    private object _current;
    public object CurrentView
    {
        get => _current;
        private set => SetField(ref _current, value);
    }

    public ScanSession? Session { get; private set; }

    public RelayCommand GoHomeCommand { get; }
    public RelayCommand GoListCommand { get; }
    public RelayCommand GoMapCommand { get; }
    public RelayCommand GoExplorerCommand { get; }
    public RelayCommand GoBySoftwareCommand { get; }
    public RelayCommand GoReportCommand { get; }

    // A1: 聚合页(空间地图/按软件/报告)按修订号懒重载 —— 删除发生在别处时, 切回来才按最新状态重算一次, 避免每次删除都全量重建。
    private readonly Dictionary<object, int> _shownRevision = new();

    public void LoadSession(ScanSession session)
    {
        Session = session;
        _list.Load(session);
        _map.Load(session);
        _explorer.Load(session);
        _software.Load(session);
        _report.Load(session);
        _home.OnSessionLoaded(session);
        _shownRevision[_map] = _shownRevision[_software] = _shownRevision[_report] = session.Revision;
        GoListCommand.RaiseCanExecuteChanged();
        GoMapCommand.RaiseCanExecuteChanged();
        GoExplorerCommand.RaiseCanExecuteChanged();
        GoBySoftwareCommand.RaiseCanExecuteChanged();
        GoReportCommand.RaiseCanExecuteChanged();
    }

    // 切到某聚合页时, 若会话已变更(有删除), 先按最新状态重载一次。
    private void EnsureFresh(object page, Action reload)
    {
        if (Session is null) return;
        if (_shownRevision.TryGetValue(page, out var rev) && rev == Session.Revision) return;
        reload();
        _shownRevision[page] = Session.Revision;
    }

    public void ShowHome() { _home.OnActivated(); CurrentView = _home; }
    public void ShowList() => CurrentView = _list;
    public void ShowMap() { EnsureFresh(_map, () => _map.Load(Session!)); CurrentView = _map; }
    public void ShowExplorer() => CurrentView = _explorer;
    public void ShowBySoftware() { EnsureFresh(_software, () => _software.Load(Session!)); CurrentView = _software; }
    public void ShowReport() { EnsureFresh(_report, () => _report.Load(Session!)); CurrentView = _report; }

    public void ShowDetail(FileRowViewModel row)
    {
        _detail.Show(row);
        CurrentView = _detail;
    }
}
