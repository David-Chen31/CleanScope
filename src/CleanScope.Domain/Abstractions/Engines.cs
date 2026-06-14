namespace CleanScope.Domain.Abstractions;

// 六模块裁决链契约 (架构§3 核心领域层)。仅签名 (T0.5)。
// 规则/风险标 "权威"; AI 不在本链上。

/// <summary>扫描引擎: 遍历 + 目录大小聚合 + TopN。只读, 不删除, 不评估, 不解释。</summary>
public interface IScanEngine
{
    /// <summary>扫描并返回按大小降序的 TopN 节点。</summary>
    Task<IReadOnlyList<FileNode>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 同上, 但额外把"每个定稿节点"流式投递给 <paramref name="onNode"/> (全量, 非仅 TopN)。
    /// 供编排层增量持久化 → 中断后可凭已落库节点续扫 (T1.4)。返回值仍为 TopN。
    /// </summary>
    Task<IReadOnlyList<FileNode>> ScanAsync(
        ScanOptions options,
        IProgress<FileNode>? onNode,
        IProgress<ScanProgress>? progress,
        CancellationToken ct = default);
}

/// <summary>证据采集: 汇集元数据/签名/已安装/注册表/进程 → EvidenceBundle, 标注 IsFact (安全§9)。</summary>
public interface IEvidenceCollector
{
    Task<EvidenceBundle> CollectAsync(FileNode node, CancellationToken ct = default);
}

/// <summary>规则引擎 (权威): 匹配规则库, 冲突就高, 输出权威 RuleMatch。命中黑名单 AI 不可翻案 (SR-6)。</summary>
public interface IRuleEngine
{
    RuleMatch? Match(FileNode node, EvidenceBundle evidence);
}

/// <summary>归因引擎: 多证据融合 → 候选归属列表 + 置信度 (非单一答案); 无证据时标"未知" (AS-8)。</summary>
public interface IAttributionEngine
{
    IReadOnlyList<AttributionCandidate> Attribute(
        FileNode node, EvidenceBundle evidence, RuleMatch? ruleMatch);
}

/// <summary>
/// 风险引擎 (权威): A–E 决策树 + 评分护栏 + 置信度门槛 (风险分级细则)。
/// 永远返回评估 (最坏 E, fail-safe); 返回的 RiskAssessment.EvidenceChain 必须非空 (SR-5)。
/// </summary>
public interface IRiskEngine
{
    RiskAssessment Assess(
        FileNode node,
        EvidenceBundle evidence,
        RuleMatch? ruleMatch,
        IReadOnlyList<AttributionCandidate> attributions);
}

/// <summary>决策汇总: 聚合各文件分析 → 分级的用户视图项 (不自动执行)。</summary>
public interface IDecisionService
{
    IReadOnlyList<DecisionItem> Summarize(IReadOnlyList<FileAnalysis> analyses);
}
