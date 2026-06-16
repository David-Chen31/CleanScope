namespace CleanScope.Application;

/// <summary>
/// 编排结果: 扫描报告数据 + 分级建议项 (写文件由宿主经 IReportExporter 完成)。
/// <paramref name="Analyses"/> 保留逐文件完整分析 (证据链/归因/AI 解释), 供详情页展示
/// 事实 vs 推测 (安全§9); 报告/列表用 <paramref name="Decisions"/> 精简视图即可。
/// </summary>
public sealed record ScanAndAnalyzeResult(
    ScanReport Report,
    IReadOnlyList<DecisionItem> Decisions,
    IReadOnlyList<FileAnalysis> Analyses);

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

    // AI 旁路 (可选): 三者齐备才启用; 任一缺失 → 无 AI 解释 (MVP/离线)。AI 永不进入裁决。
    private readonly ISanitizationGateway? _sanitizer;
    private readonly IExplanationService? _explanation;
    private readonly IAiOutputValidator? _validator;

    public ScanAndAnalyzeUseCase(
        IScanEngine scan, IEvidenceCollector evidence, IRuleEngine rules, IAttributionEngine attribution,
        IRiskEngine risk, IDecisionService decision, string appVersion = "0.1.0",
        ISanitizationGateway? sanitizer = null, IExplanationService? explanation = null,
        IAiOutputValidator? validator = null)
    {
        _scan = scan;
        _evidence = evidence;
        _rules = rules;
        _attribution = attribution;
        _risk = risk;
        _decision = decision;
        _appVersion = appVersion;
        _sanitizer = sanitizer;
        _explanation = explanation;
        _validator = validator;
    }

    private bool AiEnabled => _sanitizer is not null && _explanation is not null && _validator is not null;

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

            var analysis = new FileAnalysis(node, bundle, ruleMatch, attributions, risk, Explanation: null);

            // AI 旁路: 脱敏(唯一出云通道) → 解释(可降级) → 校验(规则优先)。系统关键项跳过(规则文案已足且省调用)。
            if (AiEnabled && ruleMatch?.IsSystemCritical != true)
            {
                var aiInput = _sanitizer!.Sanitize(analysis);
                var rawExplanation = await _explanation!.ExplainAsync(aiInput, ct);
                var validated = _validator!.Validate(rawExplanation, ruleMatch, risk);
                analysis = analysis with { Explanation = validated };   // 未通过校验者 Validated=false, 决策层不展示
            }

            analyses.Add(analysis);
        }

        var decisions = _decision.Summarize(analyses);
        var totalSize = nodes.Count > 0 ? nodes.Max(n => n.Size) : 0; // 根聚合 = 最大节点
        var task = new ScanTask(0, options.TargetPath, options.Mode, ScanStatus.Completed,
            startedAt, DateTime.UtcNow, totalSize, observed, _appVersion);

        return new ScanAndAnalyzeResult(new ScanReport(task, decisions), decisions, analyses);
    }

    // 同步派发的 IProgress (默认 Progress<T> 经 SynchronizationContext 异步派发, 编排里需即时计数)。
    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
