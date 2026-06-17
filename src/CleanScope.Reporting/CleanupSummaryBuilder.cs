using CleanScope.Domain.Models;

namespace CleanScope.Reporting;

/// <summary>
/// 整盘清理参谋输入构造 (S-H)。把决策项聚合成**脱敏**的 <see cref="CleanupSummary"/>
/// (复用 <see cref="SoftwareAggregator"/> / <see cref="CleanupAggregator"/>, 只含软件/类别/容量, 无路径)。
/// 供宿主在 AI 可用时喂给 <c>ICleanupAdvisor</c>。纯函数。
/// </summary>
public static class CleanupSummaryBuilder
{
    public static CleanupSummary From(IReadOnlyList<DecisionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // 容量用去重独占大小 (避免父子重复计入); 容器仅浏览, 不计。
        var total = items.Where(i => !i.IsContainer).Sum(i => i.ExclusiveSize);
        var reclaimable = items
            .Where(i => !i.IsContainer && i.RiskLevel is RiskLevel.A or RiskLevel.B)
            .Sum(i => i.ExclusiveSize);
        return new CleanupSummary(total, reclaimable,
            SoftwareAggregator.Aggregate(items), CleanupAggregator.Aggregate(items));
    }
}
