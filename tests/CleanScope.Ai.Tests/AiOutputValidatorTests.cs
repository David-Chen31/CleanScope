using CleanScope.Ai.Validation;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;

namespace CleanScope.Ai.Tests;

// T3.3: AiOutputValidator —— AS-1~8 (T-09~T-13)。
public sealed class AiOutputValidatorTests
{
    private static readonly AiOutputValidator V = new();

    private static AiExplanation Ai(RiskLevel? risk, bool? canDelete = null,
        string[]? reasoning = null, double? conf = 0.9, string? what = null, string? action = null) =>
        new(0, 0, what ?? "某文件", "App", risk, canDelete, action ?? "处理建议",
            reasoning ?? new[] { "理由" }, conf, "解释", Validated: false, "cloud", IsCloud: true, default);

    private static RiskAssessment Risk(RiskLevel level, bool canDelete = false) =>
        new(0, 0, level, 50, new[] { "f" }, new[] { 1L }, canDelete, 0.8, default);

    private static RuleMatch Rule(bool systemCritical) =>
        new(0, 0, "r", "cat", systemCritical ? RiskLevel.D : RiskLevel.B, false, systemCritical, "a", 0.9, 100, true);

    [Fact] // T-09: AI 对黑名单返回可删 → 拦截改判不建议删除
    public void Blacklist_deletable_is_overridden_to_not_recommended()
    {
        var r = V.Validate(Ai(RiskLevel.A, canDelete: true), Rule(systemCritical: true), Risk(RiskLevel.D));
        Assert.Equal(RiskLevel.D, r.RiskLevel);
        Assert.False(r.CanDeleteDirectly);
        Assert.Contains("不建议删除", r.RecommendedAction);
        Assert.True(r.Validated);
    }

    [Fact] // T-10: AI 风险低于引擎 → 以引擎为准
    public void Ai_risk_below_engine_is_raised()
    {
        var r = V.Validate(Ai(RiskLevel.A), Rule(false), Risk(RiskLevel.C));
        Assert.Equal(RiskLevel.C, r.RiskLevel);
        Assert.True(r.Validated);
    }

    [Fact] // T-11: 缺证据/置信度 → 判非法不展示
    public void Missing_reasoning_or_confidence_is_invalid()
    {
        var noReason = V.Validate(Ai(RiskLevel.B, reasoning: Array.Empty<string>()), null, Risk(RiskLevel.B));
        Assert.False(noReason.Validated);

        var noConf = V.Validate(Ai(RiskLevel.B, conf: null), null, Risk(RiskLevel.B));
        Assert.False(noConf.Validated);
    }

    [Fact] // T-12: 证据不足(引擎E)但 AI 给确定结论 → 降级 E
    public void Insufficient_evidence_downgrades_to_E()
    {
        var r = V.Validate(Ai(RiskLevel.A, conf: 0.95), null, Risk(RiskLevel.E));
        Assert.Equal(RiskLevel.E, r.RiskLevel);
        Assert.Contains("无法判断", r.RecommendedAction);
        Assert.False(r.CanDeleteDirectly);
    }

    [Fact] // T-13: 含疑似命令文本 → 当作文本, 不执行 (无副作用)
    public void Command_like_text_is_treated_as_inert_text()
    {
        var ai = Ai(RiskLevel.C, what: "运行 del /f /q C:\\Windows 即可", action: "rm -rf /");
        var r = V.Validate(ai, null, Risk(RiskLevel.C));
        Assert.True(r.Validated);                          // 仅作文本处理
        Assert.Equal(RiskLevel.C, r.RiskLevel);            // 未因命令文本而异常
    }

    [Fact] // AS-1: 绝对化表述被改写
    public void Absolute_phrasing_is_softened()
    {
        var r = V.Validate(Ai(RiskLevel.A, what: "这个文件一定可删"), null, Risk(RiskLevel.A));
        Assert.DoesNotContain("一定可删", r.WhatIsIt);
        Assert.Contains("建议确认", r.WhatIsIt);
    }

    [Fact] // 删除能力永不超过引擎: 仅 A 级 + 引擎允许才放行
    public void Direct_delete_allowed_only_when_engine_permits_and_A()
    {
        var allowed = V.Validate(Ai(RiskLevel.A, canDelete: true), null, Risk(RiskLevel.A, canDelete: true));
        Assert.True(allowed.CanDeleteDirectly);

        var blocked = V.Validate(Ai(RiskLevel.A, canDelete: true), null, Risk(RiskLevel.A, canDelete: false));
        Assert.False(blocked.CanDeleteDirectly);           // 引擎不允许 → AI 不能放行
    }
}
