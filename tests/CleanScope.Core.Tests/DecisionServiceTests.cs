using CleanScope.Core.Decisions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T1.9: DecisionService —— 按风险分组排序 / 证据链与建议 / 区分事实与推测呈现。
public sealed class DecisionServiceTests
{
    private static readonly DecisionService Svc = new();

    private static FileNode Node(string path, long size) =>
        new(0, 0, null, path, null, path.Split('\\')[^1], false, false, size,
            null, null, null, AccessState.Accessible, null, default);

    private static RiskAssessment Risk(RiskLevel level, long[] chain, params string[] factors) =>
        new(0, 0, level, 50, factors, chain, false, 0.8, default);

    private static FileAnalysis Analysis(
        string path, long size, RiskLevel level, long[] chain,
        RuleMatch? rule = null, AiExplanation? ai = null,
        IReadOnlyList<AttributionCandidate>? attr = null, params string[] factors) =>
        new(Node(path, size),
            new EvidenceBundle(0, null, Array.Empty<Evidence>()),
            rule,
            attr ?? Array.Empty<AttributionCandidate>(),
            Risk(level, chain, factors),
            ai);

    [Fact]
    public void Groups_by_risk_then_size_descending()
    {
        var items = Svc.Summarize(new[]
        {
            Analysis(@"C:\d", 10, RiskLevel.D, new[] { 1L }),
            Analysis(@"C:\a-small", 100, RiskLevel.A, new[] { 2L }),
            Analysis(@"C:\a-big", 900, RiskLevel.A, new[] { 3L }),
            Analysis(@"C:\c", 50, RiskLevel.C, new[] { 4L }),
        });

        Assert.Equal(new[] { RiskLevel.A, RiskLevel.A, RiskLevel.C, RiskLevel.D },
            items.Select(i => i.RiskLevel).ToArray());
        Assert.Equal(@"C:\a-big", items[0].Path);     // A 组内大头优先
        Assert.Equal(@"C:\a-small", items[1].Path);
    }

    [Fact]
    public void Carries_evidence_chain_and_size()
    {
        var item = Svc.Summarize(new[] { Analysis(@"C:\x", 42, RiskLevel.C, new[] { 7L, 8L }) }).Single();
        Assert.Equal(42, item.Size);
        Assert.Equal(new[] { 7L, 8L }, item.EvidenceChain);
    }

    [Fact]
    public void Rule_recommended_action_takes_precedence()
    {
        var rule = new RuleMatch(0, 0, "r", "cat", RiskLevel.D, false, true, "用 VS Installer 处理", 0.9, 100, true);
        var item = Svc.Summarize(new[] { Analysis(@"C:\x", 1, RiskLevel.D, new[] { 1L }, rule: rule) }).Single();
        Assert.Equal("用 VS Installer 处理", item.RecommendedAction);
    }

    [Fact]
    public void Default_action_used_when_no_rule_or_ai()
    {
        var item = Svc.Summarize(new[] { Analysis(@"C:\x", 1, RiskLevel.E, new[] { 1L }) }).Single();
        Assert.Contains("无法判断", item.RecommendedAction);
    }

    [Fact]
    public void Unvalidated_ai_explanation_is_not_shown()
    {
        var ai = new AiExplanation(0, 0, "缓存", "SomeApp", RiskLevel.B, true, "可删", new[] { "推测" },
            0.4, "这是某App的缓存", Validated: false, "model", IsCloud: false, default);
        var item = Svc.Summarize(new[]
        {
            Analysis(@"C:\x", 1, RiskLevel.C, new[] { 1L }, ai: ai, factors: new[] { "应用数据" })
        }).Single();

        Assert.Equal("应用数据", item.Explanation);   // 未校验 AI 不采用, 回落到事实因素
        Assert.Null(item.OwnerApp);                    // 未校验 AI 的归属也不呈现
    }

    [Fact]
    public void Validated_ai_explanation_is_shown()
    {
        var ai = new AiExplanation(0, 0, "缓存", "Chrome", RiskLevel.B, false, "走浏览器清理",
            new[] { "推测" }, 0.9, "这是 Chrome 的缓存目录", Validated: true, "model", IsCloud: false, default);
        var item = Svc.Summarize(new[] { Analysis(@"C:\x", 1, RiskLevel.B, new[] { 1L }, ai: ai) }).Single();

        Assert.Equal("这是 Chrome 的缓存目录", item.Explanation);
        Assert.Equal("Chrome", item.OwnerApp);
        Assert.Equal("走浏览器清理", item.RecommendedAction);
    }

    [Fact]
    public void Attribution_owner_takes_precedence_over_ai()
    {
        var attr = new[] { new AttributionCandidate(0, 0, "Visual Studio", 0.95, 1, Array.Empty<long>()) };
        var item = Svc.Summarize(new[]
        {
            Analysis(@"C:\x", 1, RiskLevel.C, new[] { 1L }, attr: attr)
        }).Single();
        Assert.Equal("Visual Studio", item.OwnerApp);
    }

    // S1: 独占大小修复父子目录重复计数 —— 嵌套祖先各自扣除其最近子孙, 全集之和=真实占用。
    [Fact]
    public void Exclusive_size_avoids_parent_child_double_counting()
    {
        var items = Svc.Summarize(new[]
        {
            Analysis(@"C:\",                          1000, RiskLevel.E, new[] { 1L }),
            Analysis(@"C:\Users",                      600, RiskLevel.E, new[] { 2L }),
            Analysis(@"C:\Users\a\Temp",               400, RiskLevel.A, new[] { 3L }),  // Users 的子孙
            Analysis(@"C:\Windows",                    300, RiskLevel.D, new[] { 4L }),  // C:\ 的子孙
        });

        long Ex(string p) => items.Single(i => i.Path == p).ExclusiveSize;

        // C:\ 扣除最近子孙 Users(600) 和 Windows(300) → 100; Users 扣除 Temp(400) → 200。
        Assert.Equal(100, Ex(@"C:\"));
        Assert.Equal(200, Ex(@"C:\Users"));
        Assert.Equal(400, Ex(@"C:\Users\a\Temp"));   // 叶子保留自身
        Assert.Equal(300, Ex(@"C:\Windows"));

        // 关键不变量: 独占大小之和 = 根聚合大小 (每个字节只计一次)。
        Assert.Equal(1000, items.Sum(i => i.ExclusiveSize));
        // 聚合 Size 求和会重复计数 (2300 > 1000), 证明修复必要。
        Assert.Equal(2300, items.Sum(i => i.Size));
    }
}
