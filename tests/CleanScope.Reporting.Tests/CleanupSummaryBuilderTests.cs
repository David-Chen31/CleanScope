using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.Reporting.Tests;

// S-H: 整盘参谋输入构造 —— 去重容量/可清理、复用软件与类别聚合、容器排除。
public sealed class CleanupSummaryBuilderTests
{
    private static DecisionItem Item(string path, long excl, string? owner, RiskLevel risk,
        string? category = null, bool container = false) =>
        new(path, excl, owner, risk, "建议", null, new long[] { 1 }, ExclusiveSize: excl,
            Category: category, IsContainer: container);

    [Fact]
    public void Builds_dedup_totals_and_reuses_aggregations()
    {
        var items = new[]
        {
            Item(@"C:\a\.nuget\packages", 1_000, "NuGet", RiskLevel.B, "NuGet 全局包"),
            Item(@"C:\b\data", 4_000, "App", RiskLevel.C),
            Item(@"C:\users", 9_999, null, RiskLevel.C, container: true),   // 容器: 不计
        };

        var s = CleanupSummaryBuilder.From(items);

        Assert.Equal(5_000, s.TotalSize);            // 1000 + 4000, 容器排除
        Assert.Equal(1_000, s.ReclaimableSize);      // 仅 B 计入可清理
        Assert.Equal(2, s.Software.Count);           // NuGet + App, 容器不归任何软件
        Assert.Contains(s.Categories, c => c.Name == "NuGet 全局包");
    }
}
