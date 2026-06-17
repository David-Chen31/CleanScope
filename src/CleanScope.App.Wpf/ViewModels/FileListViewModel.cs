using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CleanScope.App.Wpf.Mvvm;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 大文件/目录清单 (T5.3): 路径/大小/归属/风险/建议 + 解释入口 (→详情)。
/// 可按 风险 或 大小 排序。MVP 不渲染任何删除按钮 —— 列表仅供浏览与跳转详情。
/// </summary>
public sealed class FileListViewModel : ViewModelBase
{
    private readonly INavigationHost _host;
    private readonly ObservableCollection<FileRowViewModel> _rows = new();

    public FileListViewModel(INavigationHost host)
    {
        _host = host;
        View = CollectionViewSource.GetDefaultView(_rows);
        OpenDetailCommand = new RelayCommand(p => { if (p is FileRowViewModel r) _host.ShowDetail(r); });
        SortBySizeCommand = new RelayCommand(() => ApplySort(SortKind.Size));
        SortByRiskCommand = new RelayCommand(() => ApplySort(SortKind.Risk));
        SortByBucketCommand = new RelayCommand(() => ApplySort(SortKind.Bucket));
        ApplySort(SortKind.Bucket);
    }

    public ICollectionView View { get; }
    public RelayCommand OpenDetailCommand { get; }
    public RelayCommand SortBySizeCommand { get; }
    public RelayCommand SortByRiskCommand { get; }
    public RelayCommand SortByBucketCommand { get; }

    private string _summary = "";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    private string _sortLabel = "按大小";
    public string SortLabel { get => _sortLabel; private set => SetField(ref _sortLabel, value); }

    private FileRowViewModel? _selected;
    public FileRowViewModel? Selected
    {
        get => _selected;
        set { if (SetField(ref _selected, value) && value is not null) _host.ShowDetail(value); }
    }

    public void Load(ScanSession session)
    {
        _rows.Clear();
        foreach (var r in session.Rows) _rows.Add(r);
        Summary = $"{session.TargetPath} — 共 {session.Rows.Count} 项（✅可清理 {session.CleanableCount} · ⚠谨慎 {session.CautionCount} · 🛑勿动 {session.KeepCount} · 🗂容器 {session.ContainerCount}）";
        ApplySort(SortKind.Bucket);
    }

    private enum SortKind { Size, Risk, Bucket }

    private void ApplySort(SortKind kind)
    {
        View.SortDescriptions.Clear();
        switch (kind)
        {
            case SortKind.Size:
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Size), ListSortDirection.Descending));
                SortLabel = "按大小";
                break;
            case SortKind.Risk:
                // 风险高→低 (E..A): RiskLevel 枚举值越大越危险, 故降序。
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.RiskLevel), ListSortDirection.Descending));
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Size), ListSortDirection.Descending));
                SortLabel = "按风险";
                break;
            default:
                // 按桶 (可清理→谨慎→勿动→容器), 组内按大小; 把可操作项排在最前。
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Bucket), ListSortDirection.Ascending));
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Size), ListSortDirection.Descending));
                SortLabel = "按分类";
                break;
        }
        View.Refresh();
    }
}
