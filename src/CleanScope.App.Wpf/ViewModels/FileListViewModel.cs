using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 可清理清单 (P1, 由旧"文件清单"改造): 不再是"最大的 N 个文件"(最大≠可清理, 多为容器),
/// 而是一张**可勾选的批量工作清单** —— 默认只看可清理项, 全选/筛选/合计可回收, 一键批量移入回收站。
///
/// 红线: 仍走安全闸门逐项复核 (黑名单/容器/占用/风险), 仅 A/B 可清理项可删, 且只移入回收站 (可恢复)。
/// 取消"只看可清理"可浏览全部项 (谨慎/勿动/容器只读, 不可勾选)。点击行仍可进详情看证据链。
/// </summary>
public sealed class FileListViewModel : ViewModelBase
{
    private readonly INavigationHost _host;
    private readonly AppServices _services;
    private readonly ObservableCollection<FileRowViewModel> _rows = new();

    public FileListViewModel(AppServices services, INavigationHost host)
    {
        _services = services;
        _host = host;
        View = CollectionViewSource.GetDefaultView(_rows);
        View.Filter = FilterRow;
        OpenDetailCommand = new RelayCommand(p => { if (p is FileRowViewModel r) _host.ShowDetail(r); });
        SortBySizeCommand = new RelayCommand(() => ApplySort(SortKind.Size));
        SortByRiskCommand = new RelayCommand(() => ApplySort(SortKind.Risk));
        SortByBucketCommand = new RelayCommand(() => ApplySort(SortKind.Bucket));
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true));
        SelectNoneCommand = new RelayCommand(() => SetAllSelected(false));
        RecycleSelectedCommand = new AsyncRelayCommand(_ => RecycleSelectedAsync(), _ => !_busy);
        ApplySort(SortKind.Size);
    }

    public ICollectionView View { get; }
    public RelayCommand OpenDetailCommand { get; }
    public RelayCommand SortBySizeCommand { get; }
    public RelayCommand SortByRiskCommand { get; }
    public RelayCommand SortByBucketCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand RecycleSelectedCommand { get; }

    private string _summary = "";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    private string _selectionSummary = "未选择任何项。";
    public string SelectionSummary { get => _selectionSummary; private set => SetField(ref _selectionSummary, value); }

    private string _actionStatus = "";
    public string ActionStatus { get => _actionStatus; private set => SetField(ref _actionStatus, value); }

    private string _sortLabel = "按大小";
    public string SortLabel { get => _sortLabel; private set => SetField(ref _sortLabel, value); }

    // 默认只看可清理 (本页的主职责); 取消则浏览全部。
    private bool _showCleanableOnly = true;
    public bool ShowCleanableOnly
    {
        get => _showCleanableOnly;
        set { if (SetField(ref _showCleanableOnly, value)) { View.Refresh(); UpdateSelectionSummary(); } }
    }

    private bool _busy;

    private FileRowViewModel? _selected;
    public FileRowViewModel? Selected
    {
        get => _selected;
        set { if (SetField(ref _selected, value) && value is not null) _host.ShowDetail(value); }
    }

    public void Load(ScanSession session)
    {
        foreach (var r in _rows) r.PropertyChanged -= OnRowPropertyChanged;
        _rows.Clear();
        foreach (var r in session.Rows)
        {
            r.PropertyChanged += OnRowPropertyChanged;
            _rows.Add(r);
        }
        var cleanable = session.Rows.Count(r => r.IsRecyclable);
        Summary = $"{session.TargetPath} — 可清理 {cleanable} 项可勾选批量移入回收站（可还原）；" +
                  $"全部 {session.Rows.Count} 项（✅可清理 {session.CleanableCount} · ⚠谨慎 {session.CautionCount} · 🛑勿动 {session.KeepCount} · 🗂容器 {session.ContainerCount}）。";
        ActionStatus = "";
        ApplySort(SortKind.Size);
        View.Refresh();
        UpdateSelectionSummary();
    }

    private bool FilterRow(object o)
        => o is FileRowViewModel r && (!_showCleanableOnly || r.IsRecyclable);

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileRowViewModel.IsSelected)) UpdateSelectionSummary();
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var r in _rows)
            if (r.IsRecyclable && (View.Filter is null || View.Filter(r)))
                r.IsSelected = selected;
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var chosen = _rows.Where(r => r.IsSelected && r.IsRecyclable).ToList();
        var size = chosen.Sum(r => r.Size);
        SelectionSummary = chosen.Count == 0
            ? "未选择任何项。"
            : $"已选 {chosen.Count} 项，约 {Format.HumanSize(size)}，可一键移入回收站（可还原）。";
        RecycleSelectedCommand.RaiseCanExecuteChanged();
    }

    // 批量移入回收站: 一次确认 → 逐项过安全闸门 (黑名单/容器/占用/风险独立复核) → 先写审计 → 仅移入回收站。
    private async Task RecycleSelectedAsync()
    {
        var targets = _rows.Where(r => r.IsSelected && r.IsRecyclable).ToList();
        if (targets.Count == 0) { ActionStatus = "请先勾选要清理的项。"; return; }

        var total = targets.Sum(r => r.Size);
        var confirm = MessageBox.Show(
            $"确定把选中的 {targets.Count} 项（约 {Format.HumanSize(total)}）移入回收站吗？\n\n" +
            "可从回收站还原，非永久删除。每一项仍会经安全闸门复核，命中系统关键/容器/占用的会被自动跳过。",
            "批量移入回收站 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { ActionStatus = "已取消，未做任何改动。"; return; }

        _busy = true;
        RecycleSelectedCommand.RaiseCanExecuteChanged();
        int ok = 0, skipped = 0;
        long freed = 0;
        try
        {
            foreach (var r in targets)
            {
                var request = new ActionRequest(null, r.Path, ActionType.MoveToRecycleBin);
                var verdict = _services.SafetyGuard.Evaluate(request, r.Analysis.RuleMatch, r.Analysis.Risk);
                if (verdict.Outcome != GuardOutcome.Allowed) { skipped++; continue; }

                var log = await _services.ActionExecutor.ExecuteAsync(request, verdict);
                if (log.Result == ActionResult.Success) { r.MarkDeleted(); ok++; freed += r.Size; }
                else skipped++;
            }
        }
        finally
        {
            _busy = false;
        }

        ActionStatus = $"已移入回收站 {ok} 项（约 {Format.HumanSize(freed)}，可还原）"
            + (skipped > 0 ? $"；{skipped} 项被安全闸门拦下或失败，已保留。" : "。");
        View.Refresh();
        UpdateSelectionSummary();
    }

    private enum SortKind { Size, Risk, Bucket }

    private void ApplySort(SortKind kind)
    {
        View.SortDescriptions.Clear();
        switch (kind)
        {
            case SortKind.Risk:
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.RiskLevel), ListSortDirection.Descending));
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Size), ListSortDirection.Descending));
                SortLabel = "按风险";
                break;
            case SortKind.Bucket:
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Bucket), ListSortDirection.Ascending));
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Size), ListSortDirection.Descending));
                SortLabel = "按分类";
                break;
            default:
                View.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.Size), ListSortDirection.Descending));
                SortLabel = "按大小";
                break;
        }
        View.Refresh();
    }
}
