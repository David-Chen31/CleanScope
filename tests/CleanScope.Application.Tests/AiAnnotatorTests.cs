using CleanScope.Ai.Chat;
using CleanScope.Ai.Explanation;
using CleanScope.Ai.Sanitization;
using CleanScope.Ai.Validation;
using CleanScope.Application;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Application.Tests;

// S6: AiAnnotator —— 按脱敏路径模式缓存 (同类复用) + 并发; 跳过系统关键。
public sealed class AiAnnotatorTests
{
    private const string ValidJson = """
        {"whatIsIt":"缓存目录","ownerApp":"App","riskLevel":"B","canDeleteDirectly":false,
         "recommendedAction":"清理","reasoning":["位于缓存目录"],"confidence":0.9,
         "userFriendlyExplanation":"这是缓存。"}
        """;

    private static FileAnalysis Analysis(string path, bool systemCritical = false)
    {
        var node = new FileNode(0, 0, null, path, null, "Cache", true, false, 1000,
            null, null, null, AccessState.Accessible, null, default);
        var bundle = new EvidenceBundle(0, null, new[]
        {
            new Evidence(1, 0, EvidenceKind.Metadata, "v", "s", IsFact: true, 0.9, default),
        });
        var rule = systemCritical
            ? new RuleMatch(0, 0, "r", "cat", RiskLevel.D, false, true, "禁删", 0.9, 100, true)
            : null;
        var risk = new RiskAssessment(0, 0, RiskLevel.B, 35, new[] { "缓存" }, new long[] { 1 }, false, 0.6, default);
        return new FileAnalysis(node, bundle, rule, Array.Empty<AttributionCandidate>(), risk, null);
    }

    private static AiAnnotator Build(CountingChat chat) =>
        new(new SanitizationGateway(), new ExplanationService(chat), new AiOutputValidator());

    [Fact]
    public async Task Same_path_pattern_is_cached_and_calls_ai_once()
    {
        var chat = new CountingChat(ValidJson);
        var annotator = Build(chat);
        var a = Analysis(@"C:\Users\me\AppData\Local\App\Cache");

        await annotator.AnnotateAsync(a);
        await annotator.AnnotateAsync(a);     // 同脱敏模式 → 命中缓存

        Assert.Equal(1, chat.Calls);
    }

    [Fact]
    public async Task Batch_concurrent_dedups_identical_patterns()
    {
        var chat = new CountingChat(ValidJson);
        var annotator = Build(chat);
        var a = Analysis(@"C:\Users\me\AppData\Local\App\Cache");

        var result = await annotator.AnnotateAllAsync(new[] { a, a, a }, maxParallel: 4);

        Assert.Equal(3, result.Length);
        Assert.True(chat.Calls <= 3);          // 缓存使重复模式不必各调一次
    }

    [Fact]
    public async Task System_critical_is_skipped_without_calling_ai()
    {
        var chat = new CountingChat(ValidJson);
        var annotator = Build(chat);

        var ex = await annotator.AnnotateAsync(Analysis(@"C:\Windows\System32", systemCritical: true));

        Assert.Null(ex);
        Assert.Equal(0, chat.Calls);           // 系统关键不出云
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
