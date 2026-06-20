using System.Collections.ObjectModel;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 空间地图 (S2): treemap 矩形树图 + 目录下钻。回答“空间去哪了”。
/// 矩形面积=聚合大小, 颜色=风险; 点击有子节点的块 → 下钻; 点击叶子 → 跳详情; 面包屑可回上层。
/// </summary>
public sealed class SpaceMapViewModel : ViewModelBase
{
    private readonly INavigationHost _host;

    public SpaceMapViewModel(INavigationHost host)
    {
        _host = host;
        UpCommand = new RelayCommand(GoUp, () => Breadcrumb.Count > 1);
        NavigateToCommand = new RelayCommand(p => { if (p is TreeNodeViewModel n) NavigateTo(n); });
    }

    public RelayCommand UpCommand { get; }
    public RelayCommand NavigateToCommand { get; }

    /// <summary>当前层变化时通知视图重绘 treemap。</summary>
    public event Action? Changed;

    public ObservableCollection<TreeNodeViewModel> Breadcrumb { get; } = new();

    private TreeNodeViewModel? _current;
    public TreeNodeViewModel? Current
    {
        get => _current;
        private set
        {
            if (SetField(ref _current, value))
            {
                OnPropertyChanged(nameof(Hint));
                UpCommand.RaiseCanExecuteChanged();
                Changed?.Invoke();
            }
        }
    }

    public string Hint => Current is null
        ? ""
        : $"{Current.Name} — {Current.SizeText}　(点击方块下钻；点击最末层查看详情)";

    // 问题#2: 以真实磁盘占用为准的一行说明 (避免"逻辑合计"被误当成磁盘占用)。
    private string _diskHint = "";
    public string DiskHint { get => _diskHint; private set => SetField(ref _diskHint, value); }
    public bool HasDiskHint => !string.IsNullOrEmpty(_diskHint);

    public void Load(ScanSession session)
    {
        // A1: 用排除已删项的有效行重建 treemap, 删后空间地图同步。
        var root = TreeBuilder.Build(session.TargetPath, session.ActiveRows, session.TotalSize);
        Breadcrumb.Clear();
        Breadcrumb.Add(root);
        Current = root;
        DiskHint = BuildDiskHint(session);
        OnPropertyChanged(nameof(HasDiskHint));
    }

    // 真实磁盘占用 + 逻辑合计差异说明: 扫描按"逻辑大小"求和, 硬链接/稀疏文件会被重复计, 故逻辑合计可能 > 实际占用。
    private static string BuildDiskHint(ScanSession session)
    {
        if (session.Disk is not { } d) return "";
        var logical = session.TotalSize;
        var baseLine = $"磁盘 {d.Root} 实际占用 {Format.HumanSize(d.Used)} / 容量 {Format.HumanSize(d.Total)}（可用 {Format.HumanSize(d.Free)}）。";
        // 仅当逻辑合计明显大于实际占用 (>3%) 时, 解释差异来源, 避免用户疑惑"为什么比磁盘还大"。
        if (logical > d.Used * 1.03)
            return baseLine + $" 下图按扫描到的逻辑大小展示，逻辑合计约 {Format.HumanSize(logical)}；" +
                   "它高于实际占用是因为硬链接/稀疏文件（如 WSL、Docker、虚拟磁盘）被重复计入，属正常现象。";
        return baseLine + " 下图按扫描到的逻辑大小展示各目录占比。";
    }

    /// <summary>激活一个方块: 有子节点则下钻, 否则 (叶子/有对应分析行) 跳详情。</summary>
    public void Activate(TreeNodeViewModel node)
    {
        if (node.IsRemainder) return;
        if (node.HasChildren)
        {
            Breadcrumb.Add(node);
            Current = node;
        }
        else if (node.Row is not null)
        {
            _host.ShowDetail(node.Row);
        }
    }

    private void GoUp()
    {
        if (Breadcrumb.Count <= 1) return;
        Breadcrumb.RemoveAt(Breadcrumb.Count - 1);
        Current = Breadcrumb[^1];
    }

    private void NavigateTo(TreeNodeViewModel crumb)
    {
        var idx = Breadcrumb.IndexOf(crumb);
        if (idx < 0) return;
        while (Breadcrumb.Count > idx + 1) Breadcrumb.RemoveAt(Breadcrumb.Count - 1);
        Current = Breadcrumb[^1];
    }
}
