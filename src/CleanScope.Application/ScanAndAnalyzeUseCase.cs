namespace CleanScope.Application;

/// <summary>编排结果: 扫描报告数据 + 分级建议项 (写文件由宿主经 IReportExporter 完成)。</summary>
public sealed record ScanAndAnalyzeResult(ScanReport Report, IReadOnlyList<DecisionItem> Decisions);

/// <summary>
/// 闭环编排 (架构§3 主干): 扫描 → 规则 → 风险 → 决策 → 报告数据。
/// 全程经 Domain 抽象, 不碰具体实现 (engines 由组合根注入)。MVP 只读、零删除。
///
/// Phase 1 简化: 证据为"扫描观测 + 规则命中"两类事实 (满足 SR-5 证据链非空);
/// 归因为空; AI 为静态桩 (无解释)。Phase 2 接真实证据/归因, Phase 3 接 AI 旁路 (脱敏→解释→校验)。
/// </summary>
public sealed class ScanAndAnalyzeUseCase
{
    private readonly IScanEngine _scan;
    private readonly IRuleEngine _rules;
    private readonly IRiskEngine _risk;
    private readonly IDecisionService _decision;
    private readonly string _appVersion;

    public ScanAndAnalyzeUseCase(
        IScanEngine scan, IRuleEngine rules, IRiskEngine risk, IDecisionService decision,
        string appVersion = "0.1.0")
    {
        _scan = scan;
        _rules = rules;
        _risk = risk;
        _decision = decision;
        _appVersion = appVersion;
    }

    public async Task<ScanAndAnalyzeResult> ExecuteAsync(
        ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var startedAt = DateTime.UtcNow;

        // 流式计数全量节点 (文件/目录数), 同时拿回 TopN 供分析。
        long observed = 0;
        var counter = new SyncProgress<FileNode>(_ => observed++);
        var nodes = await _scan.ScanAsync(options, counter, progress, ct);

        long evId = 0;
        var analyses = new List<FileAnalysis>(nodes.Count);
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();

            // SR-5: 至少一条观测事实证据, 保证证据链非空。
            var evidences = new List<Evidence>
            {
                new(++evId, node.Id, EvidenceKind.Metadata, node.Path, "scan", IsFact: true, 1.0, startedAt),
            };
            var ruleMatch = _rules.Match(node, new EvidenceBundle(node.Id, null, evidences));
            if (ruleMatch is not null)
                evidences.Add(new Evidence(++evId, node.Id, EvidenceKind.PathRule,
                    $"{ruleMatch.RuleId}:{ruleMatch.Category}", "rule", IsFact: true, ruleMatch.Confidence, startedAt));

            var bundle = new EvidenceBundle(node.Id, null, evidences);
            var attributions = Array.Empty<AttributionCandidate>();
            var risk = _risk.Assess(node, bundle, ruleMatch, attributions);

            // AI 静态桩: MVP 无解释 (Explanation=null)。
            analyses.Add(new FileAnalysis(node, bundle, ruleMatch, attributions, risk, Explanation: null));
        }

        var decisions = _decision.Summarize(analyses);
        var totalSize = nodes.Count > 0 ? nodes.Max(n => n.Size) : 0; // 根聚合 = 最大节点
        var task = new ScanTask(0, options.TargetPath, options.Mode, ScanStatus.Completed,
            startedAt, DateTime.UtcNow, totalSize, observed, _appVersion);

        return new ScanAndAnalyzeResult(new ScanReport(task, decisions), decisions);
    }

    // 同步派发的 IProgress (默认 Progress<T> 经 SynchronizationContext 异步派发, 编排里需即时计数)。
    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
