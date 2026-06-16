using CleanScope.Ai.Chat;
using CleanScope.Ai.Explanation;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Ai.Tests;

// T3.2: ExplanationService —— 云端解释 + 降级回退规则解释 (T-14, 决议5)。
public sealed class ExplanationServiceTests
{
    private static AiInput Input() => new(
        @"C:\Users\%USER%\AppData\Local\Google\Chrome\User Data\Default\%FILE%", null, 1000,
        NodeType.Cache, "浏览器缓存", RiskLevel.B, false,
        new[] { "Signature: signed by Google LLC" },
        new[] { new AttributionCandidate(0, 0, "Chrome", 0.9, 1, Array.Empty<long>()) }, 0.85);

    [Fact]
    public async Task Disabled_cloud_falls_back_to_local_rule_explanation()
    {
        var svc = new ExplanationService(new FakeChat { Enabled = false });
        Assert.False(svc.IsCloudEnabled);

        var e = await svc.ExplainAsync(Input());
        Assert.False(e.IsCloud);
        Assert.Equal("rule-based", e.ModelUsed);
        Assert.Equal(RiskLevel.B, e.RiskLevel);
        Assert.False(e.CanDeleteDirectly);            // 本地保守
        Assert.NotEmpty(e.Reasoning);
        Assert.False(e.Validated);                    // 须经校验器
    }

    [Fact]
    public async Task Null_chat_uses_local()
    {
        var e = await new ExplanationService(null).ExplainAsync(Input());
        Assert.False(e.IsCloud);
        Assert.Equal("rule-based", e.ModelUsed);
    }

    [Fact]
    public async Task Cloud_success_returns_parsed_explanation()
    {
        var json = """
            {"whatIsIt":"Chrome 浏览器缓存目录","ownerApp":"Chrome","riskLevel":"B",
             "canDeleteDirectly":false,"recommendedAction":"用 Chrome 设置清除浏览数据",
             "reasoning":["路径位于 Chrome 用户数据","属可再生成缓存"],"confidence":0.9,
             "userFriendlyExplanation":"这是 Chrome 的缓存, 可经浏览器清理。"}
            """;
        var svc = new ExplanationService(new FakeChat { Enabled = true, Reply = _ => json });

        var e = await svc.ExplainAsync(Input());
        Assert.True(e.IsCloud);
        Assert.Equal("cloud", e.ModelUsed);
        Assert.Equal("Chrome 浏览器缓存目录", e.WhatIsIt);
        Assert.Equal(RiskLevel.B, e.RiskLevel);
        Assert.Equal(2, e.Reasoning.Count);
        Assert.False(e.Validated);
    }

    [Fact] // T-14: 云端抛异常(超时/不可用) → 降级本地
    public async Task Cloud_failure_degrades_to_local()
    {
        var svc = new ExplanationService(new FakeChat
        {
            Enabled = true,
            Reply = _ => throw new HttpRequestException("timeout"),
        });

        var e = await svc.ExplainAsync(Input());
        Assert.False(e.IsCloud);
        Assert.Equal("rule-based", e.ModelUsed);     // 核心功能不依赖 AI 在线
    }

    [Fact]
    public async Task Cloud_garbage_output_degrades_to_local()
    {
        var svc = new ExplanationService(new FakeChat { Enabled = true, Reply = _ => "这不是JSON" });
        var e = await svc.ExplainAsync(Input());
        Assert.Equal("rule-based", e.ModelUsed);
    }

    private sealed class FakeChat : IAiChat
    {
        public bool Enabled { get; set; }
        public Func<string, string>? Reply { get; set; }

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
            => Task.FromResult(Reply is null ? "{}" : Reply(userPrompt));
    }
}
