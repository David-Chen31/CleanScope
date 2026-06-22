using System.Collections.ObjectModel;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 按软件视图 (S-F): 把归属相同的项归并, 回答"空间被哪些软件占了 + 各能清多少"。
/// 每个软件一组 (可展开看名下各项); 点击某项 → 详情。比按风险更贴近用户语言。
/// 问题3: 每组提供「专清」—— 一键把本软件名下全部可放心清理(A/B)项移入回收站 (粗粒度、仅安全集合、可撤销);
/// 与「目录浏览」分工互补 (后者细粒度多选、可处理高风险), 不重复造文件选择器。
/// </summary>
public sealed class SpaceBySoftwareViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly INavigationHost _host;
    private ScanSession? _session;

    public SpaceBySoftwareViewModel(AppServices services, INavigationHost host)
    {
        _services = services;
        _host = host;
        OpenDetailCommand = new RelayCommand(p => { if (p is FileRowViewModel r) _host.ShowDetail(r); });
        CleanGroupCommand = new AsyncRelayCommand(p => CleanGroupAsync(p as SoftwareGroupViewModel), _ => !_cleaning);
    }

    public RelayCommand OpenDetailCommand { get; }
    public AsyncRelayCommand CleanGroupCommand { get; }   // 问题3: 按软件专清 (本软件可清项一键回收)

    public ObservableCollection<SoftwareGroupViewModel> Groups { get; } = new();

    private string _summary = "";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    public void Load(ScanSession session)
    {
        _session = session;
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
                // 认不出归属的组 → 显示"个人文件"而非"未归类/未知来源"(人话化)。
                Groups.Add(new SoftwareGroupViewModel(Humanize.Origin(u.Name), u.ItemCount, u.TotalSize, u.CleanableSize, rows));

        var cleanable = usages.Sum(u => u.CleanableSize);
        Summary = $"{Groups.Count} 个软件/来源占用了 {Format.HumanSize(usages.Sum(u => u.TotalSize))}，" +
                  $"其中可清理约 {Format.HumanSize(cleanable)}（A/B，仍建议确认）。";
    }

    // —— 问题3: 按软件「专清」(一键把该软件名下全部可放心清理(A/B)项移入回收站) ——
    // 仅取该组内 IsRecyclable (A/B、非容器、未占用、未删) 的行; 逐项仍过安全闸门 (命中红线者跳过、原样保留);
    // 全程移入回收站可还原, 非永久删除。删除后广播扣减 + 重载本页 + Toast 提供"撤销"。
    private bool _cleaning;
    private List<(string path, long size)> _lastCleaned = new();

    private async Task CleanGroupAsync(SoftwareGroupViewModel? group)
    {
        if (group is null || _session is null || _cleaning) return;
        var targets = group.Items.Where(r => r.IsRecyclable).ToList();
        if (targets.Count == 0) { Toast.Show("该软件下没有可放心清理的项。", ToastKind.Info); return; }

        var total = targets.Sum(r => r.Size);
        var model = new Views.ConfirmDialogModel
        {
            Title = $"专清「{group.Name}」",
            Intro = $"把「{group.Name}」名下所有“可放心清理(A/B)”的项一次性移入回收站。只清理已判定安全的项，可随时从回收站撤销/还原。",
            Details = Views.ConfirmDialogModel.Rows(
                ("软件", group.Name), ("可清理", $"{targets.Count} 项"), ("合计", $"约 {Format.HumanSize(total)}")),
            WarningText = "每一项仍逐一经安全闸门复核：命中系统关键/容器/占用的会自动跳过、原样保留。删除只进回收站，不是永久删除。",
            ConfirmText = $"专清（{targets.Count} 项）",
        };
        if (!Views.ConfirmDialog.Show(System.Windows.Application.Current?.MainWindow, model)) return;

        _cleaning = true;
        CleanGroupCommand.RaiseCanExecuteChanged();
        var recycled = new List<(string path, long size)>();
        int ok = 0, skipped = 0;
        long freed = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var r in targets)
                {
                    var request = new ActionRequest(null, r.Path, ActionType.MoveToRecycleBin);
                    var verdict = _services.SafetyGuard.Evaluate(request, r.Analysis.RuleMatch, r.Analysis.Risk);
                    if (verdict.Outcome != GuardOutcome.Allowed) { skipped++; continue; }
                    var log = _services.ActionExecutor.ExecuteAsync(request, verdict).GetAwaiter().GetResult();
                    if (log.Result == ActionResult.Success) { ok++; freed += r.Size; r.MarkDeleted(); recycled.Add((r.Path, r.Size)); }
                    else skipped++;
                }
            });
        }
        finally
        {
            _cleaning = false;
            CleanGroupCommand.RaiseCanExecuteChanged();
        }

        foreach (var (path, size) in recycled) _session.NotifyRemoved(path, size, true);   // A1: 广播扣减
        _lastCleaned = recycled;
        if (ok > 0) UserPrefs.Current.AddCleaned(freed, ok);
        Load(_session);   // 重载分组/汇总 (已删项从 ActiveRows 移出)

        if (ok > 0)
            Toast.Show($"已专清「{group.Name}」{ok} 项（约 {Format.HumanSize(freed)}，可撤销）",
                ToastKind.Success, "撤销", () => _ = UndoLastCleanAsync());
        else if (skipped > 0)
            Toast.Show($"选中的 {skipped} 项都被安全闸门跳过、原样保留。", ToastKind.Info);
    }

    private async Task UndoLastCleanAsync()
    {
        var batch = _lastCleaned.ToList();
        if (batch.Count == 0 || _session is null) return;
        int ok = 0, fail = 0;
        foreach (var (path, size) in batch)
        {
            var restored = await Task.Run(() => _services.RecycleRestore.TryRestore(path));
            if (restored) { _session.NotifyRestored(path, size, true); UserPrefs.Current.SubtractCleaned(size, 1); ok++; }
            else fail++;
        }
        _lastCleaned.Clear();
        Load(_session);
        if (fail == 0) Toast.Show($"已还原 {ok} 项到原位。", ToastKind.Success);
        else Toast.Show($"已还原 {ok} 项；{fail} 项未能自动还原，请在回收站手动还原。", ToastKind.Info);
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
