namespace CleanScope.Reporting;

/// <summary>
/// 按软件聚合 (S-F)。把决策项按 <see cref="DecisionItem.OwnerApp"/> 归并, 给每个软件:
/// 项数 / 总占用 (去重独占大小) / 其中可清理 (A/B, 去重)。容器目录 (仅浏览) 不计入;
/// 无归属项归到"未归类 / 未知来源"。纯函数, 供报告/桌面端/CLI 共用。仍不删除任何文件。
/// </summary>
public static class SoftwareAggregator
{
    public const string Unattributed = "未归类 / 未知来源";

    public static IReadOnlyList<SoftwareUsage> Aggregate(IReadOnlyList<DecisionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .Where(i => !i.IsContainer)   // 容器是浏览入口, 不归任何软件
            .GroupBy(i => string.IsNullOrWhiteSpace(i.OwnerApp) ? Unattributed : i.OwnerApp!)
            .Select(g => new SoftwareUsage(
                Name: g.Key,
                ItemCount: g.Count(),
                TotalSize: g.Sum(i => i.ExclusiveSize),
                CleanableSize: g.Where(i => i.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(i => i.ExclusiveSize),
                TopRisk: g.Min(i => i.RiskLevel)))   // 枚举 A<B<…<E, Min = 该软件最"可清理"的等级
            .OrderByDescending(s => s.TotalSize)
            .ToList();
    }
}
