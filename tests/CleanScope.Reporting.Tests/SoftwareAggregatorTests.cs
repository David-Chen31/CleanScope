using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.Reporting.Tests;

// S-F: 按软件聚合 —— 归并归属、去重独占大小、A/B 可清理、容器排除、无归属归类。
public sealed class SoftwareAggregatorTests
{
    private static DecisionItem Item(string path, long excl, string? owner, RiskLevel risk,
        bool container = false) =>
        new(path, excl, owner, risk, "建议", null, new long[] { 1 }, ExclusiveSize: excl, IsContainer: container);

    [Fact]
    public void Groups_by_owner_with_dedup_size_and_cleanable()
    {
        var items = new[]
        {
            Item(@"C:\a\.nuget\packages", 1_000, "NuGet", RiskLevel.B),
            Item(@"C:\b\.nuget\http-cache", 500, "NuGet", RiskLevel.A),
            Item(@"C:\c\AppData\Local\Code\data", 2_000, "VS Code", RiskLevel.C),  // 不可清理
            Item(@"C:\users", 9_999, null, RiskLevel.C, container: true),          // 容器, 排除
        };

        var soft = SoftwareAggregator.Aggregate(items);

        Assert.Equal(2, soft.Count);                                   // NuGet + VS Code; 容器排除
        var nuget = soft.Single(s => s.Name == "NuGet");
        Assert.Equal(2, nuget.ItemCount);
        Assert.Equal(1_500, nuget.TotalSize);                          // 去重独占求和
        Assert.Equal(1_500, nuget.CleanableSize);                      // A+B 全可清理
        Assert.Equal(RiskLevel.A, nuget.TopRisk);                      // 最"可清理"等级

        var code = soft.Single(s => s.Name == "VS Code");
        Assert.Equal(2_000, code.TotalSize);
        Assert.Equal(0, code.CleanableSize);                          // C 不计入可清理
    }

    [Fact]
    public void Null_owner_grouped_as_unattributed_and_sorted_by_total()
    {
        var items = new[]
        {
            Item(@"C:\big", 5_000, null, RiskLevel.E),
            Item(@"C:\small", 100, "App", RiskLevel.B),
        };

        var soft = SoftwareAggregator.Aggregate(items);

        Assert.Equal(SoftwareAggregator.Unattributed, soft[0].Name);  // 5000 最大, 排第一
        Assert.Contains(soft, s => s.Name == "App");
    }

    [Fact]
    public void Empty_input_yields_empty()
        => Assert.Empty(SoftwareAggregator.Aggregate(Array.Empty<DecisionItem>()));
}
