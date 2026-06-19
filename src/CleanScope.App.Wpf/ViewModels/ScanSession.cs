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
    public ScanReport Report { get; private set; }
    public IReadOnlyList<FileRowViewModel> Rows { get; }

    // —— A1 变更总线: 任一页删除/迁移一个路径后通知全会话, 其它页据此移除/重算 (跨页一致) ——
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>某路径被移除(回收/迁移)时触发, 参数为被移除路径。各页订阅后移除对应项或重算。</summary>
    public event Action<string>? ItemRemoved;

    /// <summary>修订号: 每次移除递增, 供导航时判断某页是否需要按最新状态重载。</summary>
    public int Revision { get; private set; }

    /// <summary>已被移除且原本计入"可清理"的字节合计 (用于概览扣减)。</summary>
    public long RemovedReclaimableBytes { get; private set; }

    /// <summary>已移除项数。</summary>
    public int RemovedCount { get; private set; }

    /// <summary>登记一次移除: 记入集合、累计、递增修订号并广播。重复路径忽略。</summary>
    public void NotifyRemoved(string path, long size, bool wasReclaimable)
    {
        var p = Norm(path);
        if (p.Length == 0 || !_removed.Add(p)) return;
        RemovedCount++;
        if (wasReclaimable) RemovedReclaimableBytes += size;
        Revision++;
        ItemRemoved?.Invoke(path);
    }

    /// <summary>该路径或其某个祖先是否已被移除 (移除父目录则其子孙都视为已移除)。</summary>
    public bool IsRemoved(string path)
    {
        var p = Norm(path);
        if (p.Length == 0) return false;
        foreach (var r in _removed)
            if (p.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>排除已移除后的有效行 (各聚合页按此重算, 保证删后总量/分组同步)。</summary>
    public IReadOnlyList<FileRowViewModel> ActiveRows =>
        Rows.Where(r => !r.IsDeleted && !IsRemoved(r.Path)).ToList();

    /// <summary>扣减已清理后的剩余整盘可清理估算。</summary>
    public long RemainingReclaimable => Math.Max(0, TreeReclaimable - RemovedReclaimableBytes);

    private static string Norm(string p) => string.IsNullOrEmpty(p) ? "" : p.Replace('/', '\\').TrimEnd('\\');

    /// <summary>按需生成的整盘 AI 参谋写回报告 (S-H), 使后续导出的报告也包含该建议。</summary>
    public void ApplyAiAdvice(string advice) => Report = Report with { AiCleanupAdvice = advice };

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

    /// <summary>整棵树里最大的若干"可清理"项 (扁平、去重), 供概览"最划算的几步"预览。无树则空。</summary>
    public IReadOnlyList<ScanTreeNode> BestCleanable(int take) =>
        Tree is not null ? ScanTreeStats.EnumerateCleanable(Tree).Take(take).ToList() : Array.Empty<ScanTreeNode>();
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
