namespace CleanScope.Core.Decisions;

/// <summary>
/// 决策汇总 (架构§3 末环)。把逐文件 <see cref="FileAnalysis"/> 装配成面向用户的 <see cref="DecisionItem"/>:
/// 按风险分组、每项带证据链与建议。不自动执行任何操作 (MVP 零删除)。
///
/// 呈现安全:
///  - 推荐处理以规则权威结论优先, 其次 AI 解释 (仅 Validated 时), 否则按风险默认。
///  - 区分事实/推测: Explanation 仅在 AI 解释已校验时采用其措辞; 否则用事实因素 (risk.Factors)。
///  - 证据链原样透传 (risk.EvidenceChain), 供 UI/报告回溯。
/// </summary>
public sealed class DecisionService : IDecisionService
{
    public IReadOnlyList<DecisionItem> Summarize(IReadOnlyList<FileAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);

        return analyses
            .Select(ToItem)
            // 按风险分组 (A→E), 组内按占用大小降序 (大头优先)。
            .OrderBy(i => i.RiskLevel)
            .ThenByDescending(i => i.Size)
            .ToList();
    }

    private static DecisionItem ToItem(FileAnalysis a)
    {
        var validatedAi = a.Explanation is { Validated: true } ? a.Explanation : null;

        return new DecisionItem(
            Path: a.Node.Path,
            Size: a.Node.Size,
            OwnerApp: OwnerOf(a, validatedAi),
            RiskLevel: a.Risk.Level,
            RecommendedAction: RecommendedActionOf(a, validatedAi),
            Explanation: ExplanationOf(a, validatedAi),
            EvidenceChain: a.Risk.EvidenceChain);
    }

    private static string? OwnerOf(FileAnalysis a, AiExplanation? ai)
    {
        var top = a.Attributions.OrderByDescending(c => c.Confidence).FirstOrDefault();
        return top?.AppName ?? ai?.OwnerApp;
    }

    // 规则权威优先 → 已校验 AI → 按风险默认。
    private static string RecommendedActionOf(FileAnalysis a, AiExplanation? ai)
    {
        if (!string.IsNullOrWhiteSpace(a.RuleMatch?.RecommendedAction))
            return a.RuleMatch!.RecommendedAction!;
        if (!string.IsNullOrWhiteSpace(ai?.RecommendedAction))
            return ai!.RecommendedAction!;
        return DefaultActionFor(a.Risk.Level);
    }

    // 已校验 AI 措辞 (推测) 优先; 否则呈现事实因素。
    private static string? ExplanationOf(FileAnalysis a, AiExplanation? ai)
    {
        if (!string.IsNullOrWhiteSpace(ai?.UserFriendlyExplanation))
            return ai!.UserFriendlyExplanation;
        return a.Risk.Factors.Count > 0 ? string.Join("; ", a.Risk.Factors) : null;
    }

    private static string DefaultActionFor(RiskLevel level) => level switch
    {
        RiskLevel.A => "通常可清理 (仍建议确认)",
        RiskLevel.B => "建议用官方方式清理 (命令/设置)",
        RiskLevel.C => "谨慎处理: 建议先备份或确认用途",
        RiskLevel.D => "不建议删除: 高风险, 见官方替代方案",
        RiskLevel.E => "无法判断, 不建议删除: 请进一步确认",
        _ => "不建议删除",
    };
}
