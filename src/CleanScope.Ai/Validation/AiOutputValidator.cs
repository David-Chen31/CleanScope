namespace CleanScope.Ai.Validation;

/// <summary>
/// AI 输出校验器 (实现 <see cref="IAiOutputValidator"/>, 安全§5 AS-1~8)。
/// 让 AI 幻觉"无害": 既不能提高危险性以下 (被引擎压制), 也不能开删除绿灯。
///
///  - AS-4: 缺 reasoning/confidence → 判非法, <c>Validated=false</c> (不展示)。
///  - AS-2/AS-5: 风险不得低于引擎; 取 (AI, 引擎) 中更高者 (E 最高)。引擎判 E → 强制 E。
///  - AS-3: 命中系统关键黑名单 → 强制不可删 + "不建议删除"。
///  - AS-1: 绝对化表述 ("一定可删"等) 改写为"建议确认"。
///  - AS-6: 输出仅作文本, 校验器绝不解析/执行其中任何指令 (IR-6)。
///  - 删除能力永不超过引擎: 仅当引擎允许且最终 A 级且非系统关键, 才保留 canDeleteDirectly。
/// </summary>
public sealed class AiOutputValidator : IAiOutputValidator
{
    private static readonly (string From, string To)[] AbsolutePhrases =
    {
        ("一定可以删除", "可能可清理(建议确认)"),
        ("绝对可以删除", "可能可清理(建议确认)"),
        ("肯定可以删除", "可能可清理(建议确认)"),
        ("一定可删", "可能可清理(建议确认)"),
        ("绝对可删", "可能可清理(建议确认)"),
        ("肯定可删", "可能可清理(建议确认)"),
        ("百分百可删", "可能可清理(建议确认)"),
        ("可以放心删除", "建议确认后再处理"),
        ("放心删除", "建议确认后再处理"),
    };

    public AiExplanation Validate(AiExplanation raw, RuleMatch? ruleMatch, RiskAssessment risk)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(risk);

        // AS-4: 必带证据与置信度, 否则非法 → 不展示。
        if (raw.Reasoning is null || raw.Reasoning.Count == 0 || raw.Confidence is null)
            return raw with { Validated = false };

        var engineRisk = risk.Level;
        var systemCritical = ruleMatch?.IsSystemCritical == true;

        // AS-2/AS-5: 取更高风险 (A<B<C<D<E); AI 永不能放低。引擎 E → 仍 E。
        var aiRisk = raw.RiskLevel ?? engineRisk;
        var finalRisk = (RiskLevel)Math.Max((int)aiRisk, (int)engineRisk);

        // 删除能力永不超过引擎: 仅引擎允许 + 最终 A 级 + 非系统关键 + AI 也建议, 才放行。
        var canDelete = !systemCritical
            && finalRisk == RiskLevel.A
            && risk.CanDeleteDirectly
            && raw.CanDeleteDirectly == true;

        // AS-3 / AS-5: 系统关键或 E 级 → 强制安全文案。
        var action =
            systemCritical ? "不建议删除: 系统关键项, 请勿删除, 见官方替代方案"
            : finalRisk == RiskLevel.E ? "无法判断, 不建议删除: 请进一步确认"
            : Soften(raw.RecommendedAction) ?? "建议确认后再处理";

        return raw with
        {
            RiskLevel = finalRisk,
            CanDeleteDirectly = canDelete,
            RecommendedAction = action,
            WhatIsIt = Soften(raw.WhatIsIt),
            UserFriendlyExplanation = Soften(raw.UserFriendlyExplanation),
            Validated = true,
        };
    }

    // AS-1: 软化绝对化表述。纯文本替换, 绝不解析/执行 (AS-6/IR-6)。
    private static string? Soften(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (from, to) in AbsolutePhrases)
            text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }
}
