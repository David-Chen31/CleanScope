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

    // 容器分析 (IsContainer=true) + 指定来源短标签所需因素。
    private static FileAnalysis Container(string path, long size, params string[] factors) =>
        new(Node(path, size), new EvidenceBundle(0, null, Array.Empty<Evidence>()), null,
            Array.Empty<AttributionCandidate>(),
            new RiskAssessment(0, 0, RiskLevel.C, 40, factors, new[] { 1L }, false, 0.5, default, IsContainer: true),
            null);

    [Fact] // 容器: 有"存在解释"——来源短标签 + 贴切建议 + 非空说明; 无空白。
    public void Container_gets_origin_purpose_and_browse_action()
    {
        var item = Svc.Summarize(new[]
        {
            Container(@"C:\Users\28170\AppData\Roaming", 1000, "用户应用程序的配置与个性化数据 (随账户在域内漫游)"),
        }).Single();

        Assert.Equal("应用配置·漫游", item.Origin);                       // 来源/归属列不再空白
        Assert.Equal("展开按子目录查看，勿整体处理", item.RecommendedAction); // 贴切动作
        Assert.False(string.IsNullOrWhiteSpace(item.Explanation));         // 说明非空
        Assert.Contains("漫游", item.Explanation!);
    }

    [Fact] // 不变式: 每个项的 Origin 与 Explanation 都非空 (没有"无解释"的文件)。
    public void Every_item_has_nonempty_origin_and_explanation()
    {
        var items = Svc.Summarize(new[]
        {
            Container(@"C:\", 9, "磁盘根目录"),
            Analysis(@"C:\Users\me\AppData\Local\App\Cache", 100, RiskLevel.B, new[] { 1L }, factors: "缓存"),
            Analysis(@"C:\weird\unknown", 50, RiskLevel.E, new[] { 2L }, factors: "无规则/无归因"),
        });

        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Origin)));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Explanation)));
    }

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

    // S-D: 推荐动作类型 —— 规则带命令→RunCommand; 安装目录(C)→Uninstall; A/B 文件夹→OpenFolder; D/E→None。
    [Fact]
    public void Action_kind_run_command_when_rule_has_command()
    {
        var rule = new RuleMatch(0, 0, "conda", "conda 包缓存", RiskLevel.B, false, false,
            "用 conda clean --all", 0.9, 60, true, Command: "conda clean --all");
        var item = Svc.Summarize(new[] { Analysis(@"C:\x\pkgs", 1, RiskLevel.B, new[] { 1L }, rule: rule) }).Single();
        Assert.Equal(CleanupActionKind.RunCommand, item.ActionKind);
        Assert.Equal("conda clean --all", item.Command);
    }

    [Fact]
    public void Action_kind_uninstall_for_install_dir_C()
    {
        var attr = new[] { new AttributionCandidate(0, 0, "Docker", 0.85, 1, Array.Empty<long>()) };
        var item = Svc.Summarize(new[]
        {
            Analysis(@"C:\Program Files\Docker", 1, RiskLevel.C, new[] { 1L }, attr: attr)
        }).Single();
        Assert.Equal(CleanupActionKind.Uninstall, item.ActionKind);
    }

    [Fact]
    public void Action_kind_none_for_high_risk()
    {
        var item = Svc.Summarize(new[] { Analysis(@"C:\Windows\System32", 1, RiskLevel.D, new[] { 1L }) }).Single();
        Assert.Equal(CleanupActionKind.None, item.ActionKind);
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
