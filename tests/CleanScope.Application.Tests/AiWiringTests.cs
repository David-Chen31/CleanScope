using CleanScope.Ai.Chat;
using CleanScope.Ai.Explanation;
using CleanScope.Ai.Sanitization;
using CleanScope.Ai.Validation;
using CleanScope.Application;
using CleanScope.Core.Attribution;
using CleanScope.Core.Decisions;
using CleanScope.Core.Evidences;
using CleanScope.Core.Risk;
using CleanScope.Core.Rules;
using CleanScope.Core.Scanning;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Infrastructure.Windows;

namespace CleanScope.Application.Tests;

// T3.4: 编排接入 AI —— validated 解释进结果; 未校验不展示。
public sealed class AiWiringTests : IDisposable
{
    private readonly string _root;

    public AiWiringTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanscope_ai_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Cache"));
        File.WriteAllBytes(Path.Combine(_root, "Cache", "big.bin"), new byte[6000]);
    }

    private ScanAndAnalyzeUseCase BuildWithAi(IAiChat chat)
    {
        var rules = new[]
        {
            new RuleDefinition("temp-cache", Path.Combine(_root, "Cache"), RuleMatchKind.PathPrefix,
                "缓存", RiskLevel.B, false, false, "测试缓存", "用官方命令清理", "path_rule", 0.9, 60),
        };
        var windows = new WindowsAccess();
        return new ScanAndAnalyzeUseCase(
            new ScanEngine(), new EvidenceCollector(windows), new RuleEngine(rules),
            new AttributionEngine(), new RiskEngine(), new DecisionService(), "test",
            new SanitizationGateway(), new ExplanationService(chat), new AiOutputValidator());
    }

    [Fact]
    public async Task Validated_ai_explanation_flows_into_decisions()
    {
        var json = """
            {"whatIsIt":"测试缓存目录","ownerApp":"TestApp","riskLevel":"B","canDeleteDirectly":false,
             "recommendedAction":"用官方命令清理","reasoning":["位于缓存目录"],"confidence":0.9,
             "userFriendlyExplanation":"这是测试缓存, 可安全清理。"}
            """;
        var result = await BuildWithAi(new StubChat(enabled: true, reply: json))
            .ExecuteAsync(new ScanOptions(_root, 50, ScanMode.Normal));

        var cache = result.Decisions.Single(d => d.Path == Path.Combine(_root, "Cache"));
        Assert.Equal("这是测试缓存, 可安全清理。", cache.Explanation);   // 已校验 AI 解释进入结果
        Assert.Equal("TestApp", cache.OwnerApp);
    }

    [Fact]
    public async Task Invalid_ai_explanation_is_not_shown()
    {
        // AI 返回缺 reasoning → 校验器判非法(Validated=false) → 决策层不展示, 回落事实因素。
        var json = """{"whatIsIt":"x","riskLevel":"B","confidence":0.9,"reasoning":[]}""";
        var result = await BuildWithAi(new StubChat(enabled: true, reply: json))
            .ExecuteAsync(new ScanOptions(_root, 50, ScanMode.Normal));

        var cache = result.Decisions.Single(d => d.Path == Path.Combine(_root, "Cache"));
        Assert.NotEqual("x", cache.Explanation);   // 未校验 AI 文案不出现
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class StubChat : IAiChat
    {
        private readonly string _reply;
        public StubChat(bool enabled, string reply) { Enabled = enabled; _reply = reply; }
        public bool Enabled { get; }
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default) => Task.FromResult(_reply);
    }
}
