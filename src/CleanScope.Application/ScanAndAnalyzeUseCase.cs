namespace CleanScope.Application;

/// <summary>编排结果: 扫描报告数据 + 分级建议项 (写文件由宿主经 IReportExporter 完成)。</summary>
public sealed record ScanAndAnalyzeResult(ScanReport Report, IReadOnlyList<DecisionItem> Decisions);

/// <summary>
/// 闭环编排 (架构§3 主干): 扫描 → 证据 → 规则 → 归因 → 风险 → 决策 → 报告数据。
/// 全程经 Domain 抽象, 不碰具体实现 (engines 由组合根注入)。MVP 只读、零删除。
///
/// Phase 2 起接真实证据 (EvidenceCollector, 只产事实) 与候选归因 (AttributionEngine);
/// AI 仍为静态桩 (无解释), Phase 3 接 AI 旁路 (脱敏→解释→校验)。
/// </summary>
public sealed class ScanAndAnalyzeUseCase
{
    private readonly IScanEngine _scan;
    private readonly IEvidenceCollector _evidence;
    private readonly IRuleEngine _rules;
    private readonly IAttributionEngine _attribution;
    private readonly IRiskEngine _risk;
    private readonly IDecisionService _decision;
    private readonly string _appVersion;

    public ScanAndAnalyzeUseCase(
        IScanEngine scan, IEvidenceCollector evidence, IRuleEngine rules, IAttributionEngine attribution,
        IRiskEngine risk, IDecisionService decision, string appVersion = "0.1.0")
    {
        _scan = scan;
        _evidence = evidence;
        _rules = rules;
        _attribution = attribution;
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

        var analyses = new List<FileAnalysis>(nodes.Count);
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();

            // 证据采集 (只产事实, 含基础观测 → SR-5 链非空) → 规则 → 归因 → 风险。
            var bundle = await _evidence.CollectAsync(node, ct);
            var ruleMatch = _rules.Match(node, bundle);
            var attributions = _attribution.Attribute(node, bundle, ruleMatch);
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
