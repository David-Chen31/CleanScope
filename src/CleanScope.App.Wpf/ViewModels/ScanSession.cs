using CleanScope.Application;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>一次扫描的会话结果 (跨页共享: 概览 / 列表 / 详情 / 报告)。只读快照。</summary>
public sealed class ScanSession
{
    public ScanSession(string targetPath, ScanReport report, IReadOnlyList<FileRowViewModel> rows,
        ScanTreeNode? tree = null)
    {
        TargetPath = targetPath;
        Report = report;
        Rows = rows;
        Tree = tree;
        CleanupCategories = CleanupAggregator.Aggregate(report.Items);
    }

    public string TargetPath { get; }
    public ScanReport Report { get; }
    public IReadOnlyList<FileRowViewModel> Rows { get; }

    /// <summary>全盘目录树 (P1): 资源管理器式浏览; 无树时 null。</summary>
    public ScanTreeNode? Tree { get; }

    /// <summary>整盘 AI 参谋文本 (S-H); 透传自报告。无 AI / 未生成则为 null。</summary>
    public string? AiCleanupAdvice => Report.AiCleanupAdvice;

    /// <summary>可清理类别聚合 (S3): 每类去重可回收大小 + 官方清理方式。</summary>
    public IReadOnlyList<CleanupCategory> CleanupCategories { get; }

    public long TotalSize => Report.Task.TotalSize ?? 0;
    public long FileCount => Report.Task.FileCount ?? 0;

    public int Count(RiskLevel level) => Rows.Count(r => r.RiskLevel == level);
    // 占用统计用去重独占大小 (S1): 父子目录同时入选时, 同一批字节不被重复计入。
    public long SizeOf(RiskLevel level) => Rows.Where(r => r.RiskLevel == level).Sum(r => r.ExclusiveSize);

    public int HighRiskCount => Rows.Count(r => r.IsHighRisk);

    // 四桶计数 (D6)。
    public int CleanableCount => Rows.Count(r => r.Bucket == Common.CleanupBucket.Cleanable);
    public int CautionCount => Rows.Count(r => r.Bucket == Common.CleanupBucket.Caution);
    public int KeepCount => Rows.Count(r => r.Bucket == Common.CleanupBucket.Keep);
    public int ContainerCount => Rows.Count(r => r.Bucket == Common.CleanupBucket.Container);

    /// <summary>可清理估算 (A+B, 去重; 仅 Top-N 行)。</summary>
    public long ReclaimableEstimate => Rows.Where(r => r.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(r => r.ExclusiveSize);

    /// <summary>整盘可清理估算 (P2): 从**整棵目录树**去重累加, 含深埋各 app 的缓存; 无树则回退 Top-N。</summary>
    public long TreeReclaimable => Tree is not null ? ScanTreeStats.CleanableTotal(Tree) : ReclaimableEstimate;

    /// <summary>整盘可清理的"处数" (顶层可清理目录数)。</summary>
    public int TreeCleanableCount => Tree is not null ? ScanTreeStats.CleanableCount(Tree) : CleanableCount;
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
    void ShowBySoftware();
    void ShowExplorer();
}
