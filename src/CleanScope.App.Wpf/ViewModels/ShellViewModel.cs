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
    private readonly SpaceMapViewModel _map;
    private readonly ExplorerViewModel _explorer;
    private readonly SpaceBySoftwareViewModel _software;
    private readonly ReportViewModel _report;
    private readonly FileDetailViewModel _detail;
    private readonly AiSettingsViewModel _aiSettings;

    public ShellViewModel(AppServices services)
    {
        Services = services;
        _home = new HomeViewModel(services, this);
        _map = new SpaceMapViewModel(this);
        _explorer = new ExplorerViewModel(services);
        _software = new SpaceBySoftwareViewModel(this);
        _report = new ReportViewModel(services, this);
        _detail = new FileDetailViewModel(services, this);
        _aiSettings = new AiSettingsViewModel(services);

        GoHomeCommand = new RelayCommand(ShowHome);
        GoMapCommand = new RelayCommand(ShowMap, () => Session is not null);
        GoExplorerCommand = new RelayCommand(ShowExplorer, () => Session is not null);
        GoBySoftwareCommand = new RelayCommand(ShowBySoftware, () => Session is not null);
        GoReportCommand = new RelayCommand(ShowReport, () => Session is not null);
        GoAiSettingsCommand = new RelayCommand(ShowAiSettings);

        _current = _home;
        _aiBadge = ComputeAiBadge();
        services.AiChanged += () => AiBadge = ComputeAiBadge();   // D: 保存配置后徽章实时刷新
    }

    public AppServices Services { get; }

    private string _aiBadge;
    public string AiBadge { get => _aiBadge; private set => SetField(ref _aiBadge, value); }
    // 减重: 徽章压成一行 (引导文案移进 AI 设置页内, 不常驻)。
    private string ComputeAiBadge() => Services.AiEnabled
        ? $"AI 已启用 · 脱敏：{SanitizationLabel}"
        : "AI 未配置 · 全程本地";

    private string SanitizationLabel => Services.SanitizationLevel switch
    {
        Domain.Enums.SanitizationLevel.Strict => "严格",
        Domain.Enums.SanitizationLevel.Balanced => "均衡",
        _ => "关闭",
    };

    // 顶栏只留产品名; 安全承诺由顶栏 chip 单独承担 (不再重复长句)。
    public string Title => "CleanScope";

    private object _current;
    public object CurrentView
    {
        get => _current;
        private set { if (SetField(ref _current, value)) OnPropertyChanged(nameof(ShowResultsBar)); }
    }

    public ScanSession? Session { get; private set; }

    // 两态外壳: 未扫描 = 纯 Hero (无视图切换条); 扫描后 = 结果工作区 (顶部分段切换镜头)。
    public bool HasSession => Session is not null;
    public bool ShowResultsBar => HasSession && _activePage != "ai" && !ReferenceEquals(_current, _detail);

    // 当前页 key (分段控件高亮: 与各按钮 Tag 比对)。详情页不改高亮, 保留来处页。
    private string _activePage = "home";
    public string ActivePage
    {
        get => _activePage;
        private set { if (SetField(ref _activePage, value)) OnPropertyChanged(nameof(ShowResultsBar)); }
    }

    public RelayCommand GoHomeCommand { get; }
    public RelayCommand GoMapCommand { get; }
    public RelayCommand GoExplorerCommand { get; }
    public RelayCommand GoBySoftwareCommand { get; }
    public RelayCommand GoReportCommand { get; }
    public RelayCommand GoAiSettingsCommand { get; }

    // A1: 聚合页(空间地图/按软件/报告)按修订号懒重载 —— 删除发生在别处时, 切回来才按最新状态重算一次, 避免每次删除都全量重建。
    private readonly Dictionary<object, int> _shownRevision = new();

    public void LoadSession(ScanSession session)
    {
        Session = session;
        _map.Load(session);
        _explorer.Load(session);
        _software.Load(session);
        _report.Load(session);
        _home.OnSessionLoaded(session);
        _shownRevision[_map] = _shownRevision[_software] = _shownRevision[_report] = session.Revision;
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(ShowResultsBar));
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

    public void ShowHome() { _home.OnActivated(); ActivePage = "home"; CurrentView = _home; }
    // C1: 文件清单已并入资源管理器 —— "可清理清单"即资源管理器的"只看可清理"模式。
    public void ShowList() { _explorer.ShowCleanableOnly = true; ActivePage = "explorer"; CurrentView = _explorer; }
    public void ShowMap() { EnsureFresh(_map, () => _map.Load(Session!)); ActivePage = "map"; CurrentView = _map; }
    public void ShowExplorer() { ActivePage = "explorer"; CurrentView = _explorer; }
    public void ShowBySoftware() { EnsureFresh(_software, () => _software.Load(Session!)); ActivePage = "software"; CurrentView = _software; }
    public void ShowReport() { EnsureFresh(_report, () => _report.Load(Session!)); _report.RefreshOnShow(); ActivePage = "report"; CurrentView = _report; }
    public void ShowAiSettings() { ActivePage = "ai"; CurrentView = _aiSettings; }

    // 进入详情前所在的页, 供详情"返回"原路返回 (修复: 从"按软件"点进详情后返回却跳到资源管理器)。
    private object? _detailReturn;

    public void ShowDetail(FileRowViewModel row)
    {
        if (!ReferenceEquals(_current, _detail)) _detailReturn = _current;   // 记住来处 (避免详情→详情时丢失)
        _detail.Show(row);
        CurrentView = _detail;
    }

    public void BackFromDetail() => CurrentView = _detailReturn ?? _explorer;
}
