namespace CleanScope.Reporting;

/// <summary>
/// 可清理类别聚合 (S3)。把 A/B 可清理项按 <see cref="DecisionItem.Category"/> 归并,
/// 每类给去重后的可回收大小 (ExclusiveSize 之和, 避免父子目录重复计数) 与官方清理方式。
/// 纯函数, 供报告/桌面端/CLI 共用。仍不删除任何文件, 只汇总"能省多少 + 怎么省"。
/// </summary>
public static class CleanupAggregator
{
    public static IReadOnlyList<CleanupCategory> Aggregate(IReadOnlyList<DecisionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .Where(i => i.RiskLevel is RiskLevel.A or RiskLevel.B)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? "其他可清理" : i.Category!)
            .Select(g => new CleanupCategory(
                Name: g.Key,
                ItemCount: g.Count(),
                ReclaimableSize: g.Sum(i => i.ExclusiveSize),
                TopRisk: g.Min(i => i.RiskLevel),    // A 优先于 B (枚举 A<B)
                // 取该类里最具体的一条建议 (优先 A 级、占用大的)。
                RecommendedAction: g
                    .OrderBy(i => i.RiskLevel)
                    .ThenByDescending(i => i.ExclusiveSize)
                    .Select(i => i.RecommendedAction)
                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? "见各项建议"))
            .OrderByDescending(c => c.ReclaimableSize)
            .ToList();
    }
}
