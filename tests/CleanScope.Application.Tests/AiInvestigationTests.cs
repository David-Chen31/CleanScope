using CleanScope.Ai.Chat;
using CleanScope.Ai.Explanation;
using CleanScope.Ai.Sanitization;
using CleanScope.Ai.Validation;
using CleanScope.Application;
using CleanScope.Core.Attribution;
using CleanScope.Core.Decisions;
using CleanScope.Core.Risk;
using CleanScope.Core.Rules;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Application.Tests;

// S-C: 给 AI 真实定位 —— InvestigateUnknowns 模式只对"真正三无"未知项 (E, 非容器) 跑 AI 调查,
// 把推测写回 DecisionItem.AiInvestigation; AI 永不进入裁决 (风险仍由本地引擎权威判定)。
public sealed class AiInvestigationTests
{
    private const string ValidJson = """
        {"whatIsIt":"某软件遗留数据","ownerApp":"SomeApp","riskLevel":"C","canDeleteDirectly":false,
         "recommendedAction":"建议确认","reasoning":["路径位于用户目录"],"confidence":0.6,
         "userFriendlyExplanation":"这看起来像某个软件留下的数据。"}
        """;

    // 两节点: cache 命中规则→B; weird 三无 (无规则/无归因/无缓存名/非容器/非 AppData) → E。
    private static readonly FileNode CacheNode = Node(@"D:\App\Cache", "Cache");
    private static readonly FileNode UnknownNode = Node(@"D:\Weird\blob", "blob");

    private static FileNode Node(string path, string name) =>
        new(0, 0, null, path, null, name, true, false, 4000,
            null, null, null, AccessState.Accessible, null, default);

    private static ScanAndAnalyzeUseCase Build(CountingChat chat) => new(
        new FakeScan(new[] { CacheNode, UnknownNode }),
        new FakeEvidence(),
        new RuleEngine(new[]
        {
            new RuleDefinition("cache-rule", @"D:\App\Cache", RuleMatchKind.PathPrefix,
                "缓存", RiskLevel.B, false, false, "缓存", "用官方方式清理", "path_rule", 0.9, 60),
        }),
        new AttributionEngine(), new RiskEngine(), new DecisionService(),
        "test", new SanitizationGateway(), new ExplanationService(chat), new AiOutputValidator());

    [Fact]
    public async Task InvestigateUnknowns_runs_ai_only_on_E_items_and_writes_investigation()
    {
        var chat = new CountingChat(ValidJson);
        var result = await Build(chat).ExecuteAsync(
            new ScanOptions(@"D:\", 50, ScanMode.Normal), aiMode: AiMode.InvestigateUnknowns);

        Assert.Equal(1, chat.Calls);   // 仅 E 项调一次, B 项不调

        var unknown = result.Decisions.Single(d => d.Path == UnknownNode.Path);
        Assert.Equal(RiskLevel.E, unknown.RiskLevel);                         // 风险未被 AI 改判
        Assert.False(string.IsNullOrWhiteSpace(unknown.AiInvestigation));     // AI 推测已写入

        var cache = result.Decisions.Single(d => d.Path == CacheNode.Path);
        Assert.Equal(RiskLevel.B, cache.RiskLevel);
        Assert.Null(cache.AiInvestigation);                                   // 非 E 不调查
    }

    [Fact]
    public async Task OnDemand_does_not_call_ai_during_scan()
    {
        var chat = new CountingChat(ValidJson);
        var result = await Build(chat).ExecuteAsync(new ScanOptions(@"D:\", 50, ScanMode.Normal));

        Assert.Equal(0, chat.Calls);   // OnDemand: 扫描不解释, 详情页按需
        Assert.All(result.Decisions, d => Assert.Null(d.AiInvestigation));
    }

    [Fact]
    public async Task Cap_limits_number_of_investigated_unknowns()
    {
        var chat = new CountingChat(ValidJson);
        await Build(chat).ExecuteAsync(
            new ScanOptions(@"D:\", 50, ScanMode.Normal),
            aiMode: AiMode.InvestigateUnknowns, maxInvestigations: 0);

        Assert.Equal(0, chat.Calls);   // 上限 0 → 不调查任何未知项
    }

    // —— fakes ——

    private sealed class FakeScan : IScanEngine
    {
        private readonly IReadOnlyList<FileNode> _nodes;
        public FakeScan(IReadOnlyList<FileNode> nodes) => _nodes = nodes;

        public Task<IReadOnlyList<FileNode>> ScanAsync(
            ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
            => Task.FromResult(_nodes);

        public Task<IReadOnlyList<FileNode>> ScanAsync(
            ScanOptions options, IProgress<FileNode>? onNode,
            IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        {
            if (onNode is not null) foreach (var n in _nodes) onNode.Report(n);
            return Task.FromResult(_nodes);
        }
    }

    private sealed class FakeEvidence : IEvidenceCollector
    {
        public Task<EvidenceBundle> CollectAsync(FileNode node, CancellationToken ct = default)
            => Task.FromResult(new EvidenceBundle(node.Id, null, new[]
            {
                new Evidence(1, node.Id, EvidenceKind.Metadata, "v", "s", IsFact: true, 0.9, default),
            }));
    }

    private sealed class CountingChat : IAiChat
    {
        private readonly string _reply;
        private int _calls;
        public CountingChat(string reply) => _reply = reply;
        public bool Enabled => true;
        public int Calls => _calls;
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(_reply);
        }
    }
}
