namespace CleanScope.Application;

/// <summary>
/// 编排结果: 扫描报告数据 + 分级建议项 (写文件由宿主经 IReportExporter 完成)。
/// <paramref name="Analyses"/> 保留逐文件完整分析 (证据链/归因/AI 解释), 供详情页展示
/// 事实 vs 推测 (安全§9); 报告/列表用 <paramref name="Decisions"/> 精简视图即可。
/// </summary>
public sealed record ScanAndAnalyzeResult(
    ScanReport Report,
    IReadOnlyList<DecisionItem> Decisions,
    IReadOnlyList<FileAnalysis> Analyses,
    ScanTreeNode? Tree = null);   // P1: 全盘目录树 (仅 buildTree 时构建; 供资源管理器浏览)

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

    // AI 旁路 (可选, S6): 经注解器 (脱敏→解释→校验 + 缓存 + 并发)。AI 永不进入裁决。
    private readonly AiAnnotator _annotator;

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
        _annotator = new AiAnnotator(sanitizer, explanation, validator);
    }

    private bool AiEnabled => _annotator.Enabled;

    public async Task<ScanAndAnalyzeResult> ExecuteAsync(
        ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken ct = default,
        AiMode aiMode = AiMode.OnDemand, int maxInvestigations = 30,
        bool buildTree = false, long treeMinSize = 1_000_000)
    {
        ArgumentNullException.ThrowIfNull(options);
        var startedAt = DateTime.UtcNow;

        // 流式计数全量节点 (文件/目录数), 同时拿回 TopN 供分析。
        // P1: buildTree 时顺带收集"够大的目录节点" (≥ treeMinSize) 以构建全盘目录树, 小目录滚入父级余量。
        long observed = 0;
        var dirNodes = buildTree ? new List<FileNode>() : null;
        var counter = new SyncProgress<FileNode>(n =>
        {
            observed++;
            if (dirNodes is not null && n.IsDirectory && n.Size >= treeMinSize) dirNodes.Add(n);
        });
        var nodes = await _scan.ScanAsync(options, counter, progress, ct);

        // 裁决链 (纯本地, 快): 证据→规则→归因→风险。AI 不在此串行 (S6: 否则百项十几分钟)。
        var analyses = new List<FileAnalysis>(nodes.Count);
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var bundle = await _evidence.CollectAsync(node, ct);
            var ruleMatch = _rules.Match(node, bundle);
            var attributions = _attribution.Attribute(node, bundle, ruleMatch);
            var risk = _risk.Assess(node, bundle, ruleMatch, attributions);
            analyses.Add(new FileAnalysis(node, bundle, ruleMatch, attributions, risk, Explanation: null));
        }

        // AI 旁路: 按模式注解。AI 永不进入裁决, 仅旁路解释/调查/归因兜底 (脱敏→解释→校验)。
        //  - Batch: 对所有项 (最贵)。
        //  - InvestigateUnknowns (S-C/S-G): 对"无主"的 C/E 项 (非容器/非系统关键), 按大小取前 maxInvestigations 个,
        //    AI 既给"是什么"调查 (S-C), 也补"归哪个软件"低置信归属 (S-G) —— 削掉"未归类"大桶。数量可控。
        //  - OnDemand: 扫描不解释, 详情页按需。
        if (AiEnabled)
        {
            if (aiMode == AiMode.Batch)
                await AnnotateAsync(analyses, Enumerable.Range(0, analyses.Count).ToList(), ct);
            else if (aiMode == AiMode.InvestigateUnknowns)
                await AnnotateAsync(analyses, SelectUnattributed(analyses, maxInvestigations), ct);
        }

        var decisions = _decision.Summarize(analyses);
        var totalSize = nodes.Count > 0 ? nodes.Max(n => n.Size) : 0; // 根聚合 = 最大节点
        var task = new ScanTask(0, options.TargetPath, options.Mode, ScanStatus.Completed,
            startedAt, DateTime.UtcNow, totalSize, observed, _appVersion);

        // P1: 用轻量分类 (空证据, 无 I/O) 对收集到的目录节点跑裁决链 → 全盘目录树。
        var tree = dirNodes is null ? null : BuildTree(dirNodes, options.TargetPath, totalSize, ct);

        return new ScanAndAnalyzeResult(new ScanReport(task, decisions), decisions, analyses, tree);
    }

    // P1: 对目录节点做"路径级"分类 (复用规则/归因/风险, 但用空证据包 → 不做逐节点元数据/签名 I/O),
    // 再交决策汇总, 最后按路径建树。逐文件证据留到点开详情时按需做。
    private ScanTreeNode BuildTree(List<FileNode> dirNodes, string targetPath, long totalSize, CancellationToken ct)
    {
        var analyses = new List<FileAnalysis>(dirNodes.Count);
        foreach (var node in dirNodes)
        {
            ct.ThrowIfCancellationRequested();
            var bundle = new EvidenceBundle(node.Id, null, Array.Empty<Evidence>());
            var ruleMatch = _rules.Match(node, bundle);
            var attributions = _attribution.Attribute(node, bundle, ruleMatch);
            var risk = _risk.Assess(node, bundle, ruleMatch, attributions);
            analyses.Add(new FileAnalysis(node, bundle, ruleMatch, attributions, risk, Explanation: null));
        }
        return ScanTreeBuilder.Build(targetPath, _decision.Summarize(analyses), totalSize);
    }

    // AI 兜底归属的置信度上限 (S-G): 刻意 < 0.5 低置信门槛, 永不驱动风险/删除, 仅供展示与"按软件"分组。
    private const double AiAttributionConfidence = 0.45;

    // S-C/S-G: 选出"无主"的 C/E 项下标 —— 非容器、非系统关键、且确定性归因为空 (真正"认不出归谁"的)。
    // 按聚合大小降序取前 cap 个 (优先处理占地方的)。A/B 已是可清理且多有路径归属, 不在此列。
    private static List<int> SelectUnattributed(IReadOnlyList<FileAnalysis> analyses, int cap) =>
        analyses
            .Select((a, idx) => (a, idx))
            .Where(x => x.a.Risk.Level is RiskLevel.C or RiskLevel.E
                        && !x.a.Risk.IsContainer
                        && x.a.RuleMatch?.IsSystemCritical != true
                        && x.a.Attributions.Count == 0)
            .OrderByDescending(x => x.a.Node.Size)
            .Take(Math.Max(0, cap))
            .Select(x => x.idx)
            .ToList();

    // 对给定下标子集并发+缓存跑 AI 注解; 校验通过则写回 Explanation, 并在仍无归属时补一条
    // "AI 推测"低置信归因候选 (S-G), 让"未归类"项也能归到某软件名下 (仅展示, 不改判风险)。
    private async Task AnnotateAsync(List<FileAnalysis> analyses, IReadOnlyList<int> indices, CancellationToken ct)
    {
        if (indices.Count == 0) return;
        var subset = indices.Select(i => analyses[i]).ToList();
        var explanations = await _annotator.AnnotateAllAsync(subset, maxParallel: 8, ct);
        for (var j = 0; j < indices.Count; j++)
        {
            if (explanations[j] is not { Validated: true } ex) continue;
            var a = analyses[indices[j]];
            var attributions = a.Attributions;
            if (attributions.Count == 0 && !string.IsNullOrWhiteSpace(ex.OwnerApp))
                attributions = new[]
                {
                    new AttributionCandidate(0, a.Node.Id, ex.OwnerApp!.Trim(),
                        Math.Min(AiAttributionConfidence, ex.Confidence ?? AiAttributionConfidence),
                        1, Array.Empty<long>(), Source: "AI 推测"),
                };
            analyses[indices[j]] = a with { Explanation = ex, Attributions = attributions };
        }
    }

    // 同步派发的 IProgress (默认 Progress<T> 经 SynchronizationContext 异步派发, 编排里需即时计数)。
    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
