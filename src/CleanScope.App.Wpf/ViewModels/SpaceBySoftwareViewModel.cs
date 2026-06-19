using System.Collections.ObjectModel;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;
using CleanScope.Reporting;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 按软件视图 (S-F): 把归属相同的项归并, 回答"空间被哪些软件占了 + 各能清多少"。
/// 每个软件一组 (可展开看名下各项); 点击某项 → 详情。比按风险更贴近用户语言。
/// </summary>
public sealed class SpaceBySoftwareViewModel : ViewModelBase
{
    private readonly INavigationHost _host;

    public SpaceBySoftwareViewModel(INavigationHost host)
    {
        _host = host;
        OpenDetailCommand = new RelayCommand(p => { if (p is FileRowViewModel r) _host.ShowDetail(r); });
    }

    public RelayCommand OpenDetailCommand { get; }

    public ObservableCollection<SoftwareGroupViewModel> Groups { get; } = new();

    private string _summary = "";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    public void Load(ScanSession session)
    {
        Groups.Clear();

        // 用与报告/CLI 同一套聚合 (SoftwareAggregator) 得到顺序与口径一致的汇总, 再挂上各自的明细行。
        // A1: 用排除已删项的有效行, 删后按软件聚合同步。
        var active = session.ActiveRows;
        var byOwner = active
            .Where(r => !r.IsContainer)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.OwnerApp) ? SoftwareAggregator.Unattributed : r.OwnerApp!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var usages = SoftwareAggregator.Aggregate(active.Select(r => r.Item).ToList());
        foreach (var u in usages)
            if (byOwner.TryGetValue(u.Name, out var rows))
                Groups.Add(new SoftwareGroupViewModel(u.Name, u.ItemCount, u.TotalSize, u.CleanableSize, rows));

        var cleanable = usages.Sum(u => u.CleanableSize);
        Summary = $"{Groups.Count} 个软件/来源占用了 {Format.HumanSize(usages.Sum(u => u.TotalSize))}，" +
                  $"其中可清理约 {Format.HumanSize(cleanable)}（A/B，仍建议确认）。";
    }
}

/// <summary>单个软件分组: 汇总 + 名下明细行 (展开时展示)。</summary>
public sealed class SoftwareGroupViewModel
{
    public SoftwareGroupViewModel(string name, int itemCount, long totalSize, long cleanableSize,
        IReadOnlyList<FileRowViewModel> items)
    {
        Name = name;
        ItemCount = itemCount;
        TotalSize = totalSize;
        CleanableSize = cleanableSize;
        // 明细按桶 (可清理优先) + 大小排序, 让"能清的"浮在前面。
        Items = items.OrderBy(r => r.Bucket).ThenByDescending(r => r.Size).ToList();
    }

    public string Name { get; }
    public int ItemCount { get; }
    public long TotalSize { get; }
    public long CleanableSize { get; }
    public IReadOnlyList<FileRowViewModel> Items { get; }

    public string TotalText => Format.HumanSize(TotalSize);
    public string CleanableText => Format.HumanSize(CleanableSize);
    public bool HasCleanable => CleanableSize > 0;
    public string Header => $"{Name}　·　{ItemCount} 项　·　占用 {TotalText}";
}
