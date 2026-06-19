using System.Collections.ObjectModel;
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

    public void Load(ScanSession session)
    {
        // A1: 用排除已删项的有效行重建 treemap, 删后空间地图同步。
        var root = TreeBuilder.Build(session.TargetPath, session.ActiveRows, session.TotalSize);
        Breadcrumb.Clear();
        Breadcrumb.Add(root);
        Current = root;
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
