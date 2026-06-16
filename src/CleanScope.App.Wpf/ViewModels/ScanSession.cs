using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>一次扫描的会话结果 (跨页共享: 概览 / 列表 / 详情 / 报告)。只读快照。</summary>
public sealed class ScanSession
{
    public ScanSession(string targetPath, ScanReport report, IReadOnlyList<FileRowViewModel> rows)
    {
        TargetPath = targetPath;
        Report = report;
        Rows = rows;
    }

    public string TargetPath { get; }
    public ScanReport Report { get; }
    public IReadOnlyList<FileRowViewModel> Rows { get; }

    public long TotalSize => Report.Task.TotalSize ?? 0;
    public long FileCount => Report.Task.FileCount ?? 0;

    public int Count(RiskLevel level) => Rows.Count(r => r.RiskLevel == level);
    public long SizeOf(RiskLevel level) => Rows.Where(r => r.RiskLevel == level).Sum(r => r.Size);

    public int HighRiskCount => Rows.Count(r => r.IsHighRisk);

    /// <summary>可清理估算 (A+B; 仍建议用户确认/官方方式)。</summary>
    public long ReclaimableEstimate => Rows.Where(r => r.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(r => r.Size);
}

/// <summary>导航宿主契约 (Shell 实现)。子页通过它跳转, 不互相直接耦合。</summary>
public interface INavigationHost
{
    ScanSession? Session { get; }
    void LoadSession(ScanSession session);
    void ShowHome();
    void ShowList();
    void ShowDetail(FileRowViewModel row);
    void ShowReport();
}
