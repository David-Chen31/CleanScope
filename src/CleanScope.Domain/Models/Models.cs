namespace CleanScope.Domain.Models;

// 非实体的领域 DTO / 值对象 (接口契约用)。纯形状, 无逻辑 (T0.5)。

/// <summary>规则库条目 (rules/*.json schema, 知识库§0)。区别于 RuleMatch(逐文件匹配结果)。</summary>
public record RuleDefinition(
    string Id,
    string Pattern,            // 已展开环境变量后的匹配模式
    RuleMatchKind MatchKind,
    string Category,
    RiskLevel RiskLevel,
    bool DirectDelete,
    bool IsSystemCritical,
    string Description,
    string RecommendedAction,
    string EvidenceType,
    double Confidence,
    int Priority,
    string? Command = null);    // S-D: 命令型清理的官方命令 (如 conda clean --all); 可空

/// <summary>证据集合 (架构§5 EvidenceBundle): 元数据 + 证据链原子项。</summary>
public record EvidenceBundle(
    long FileId,
    FileMetadata? Metadata,
    IReadOnlyList<Evidence> Evidences);

/// <summary>
/// 扫描选项。<paramref name="SkipPaths"/>: 续扫时跳过的已扫子树完整路径 (T1.4 中断恢复);
/// 引擎不再下钻这些子树, 其大小不计入父目录聚合 —— 编排层 (T1.11) 负责合并已持久化的子树结果。
/// </summary>
public record ScanOptions(
    string TargetPath,
    int TopN,
    ScanMode Mode,
    IReadOnlyList<string>? SkipPaths = null);

/// <summary>扫描进度 (IProgress 回调)。</summary>
public record ScanProgress(long FilesScanned, long BytesScanned, string? CurrentPath);

/// <summary>单文件分析聚合 (编排层装配, 供决策/脱敏/解释使用)。</summary>
public record FileAnalysis(
    FileNode Node,
    EvidenceBundle Evidence,
    RuleMatch? RuleMatch,
    IReadOnlyList<AttributionCandidate> Attributions,
    RiskAssessment Risk,
    AiExplanation? Explanation);

/// <summary>
/// 决策视图项 (DecisionService 输出, 面向用户展示)。
/// <paramref name="Size"/> 为聚合大小 (目录含全部子孙, 用于"最大文件夹"展示);
/// <paramref name="ExclusiveSize"/> 为去重独占大小 —— 每个字节只归属到最深的被分析节点,
/// 用于"占用统计 / 可清理估算"求和时避免父子目录重复计数 (同一字节只计一次)。
/// </summary>
public record DecisionItem(
    string Path,
    long Size,
    string? OwnerApp,
    RiskLevel RiskLevel,
    string RecommendedAction,
    string? Explanation,
    IReadOnlyList<long> EvidenceChain,
    long ExclusiveSize = 0,
    string? Category = null,      // 清理类别 (来自规则 Category 或缓存启发), 供"按类别聚合"
    bool IsContainer = false,     // 顶层容器目录 (仅浏览, 不进风险/可清理统计)
    CleanupActionKind ActionKind = CleanupActionKind.None,  // S-D: 推荐动作类型
    string? Command = null,       // S-D: 命令型动作的官方命令
    string? AiInvestigation = null);  // S-C: AI 对"未知项"的调查推测 (已校验, 仅供参考, 不改判风险)

/// <summary>
/// 可清理类别聚合 (S3, 对标 CCleaner/BleachBit 的"按类别给可回收空间")。
/// 把 A/B 可清理项按 <see cref="Category"/> 归并, 给每类去重后的可回收大小与官方清理方式。
/// 仍不自动删除: 只给方式, 用户决策。
/// </summary>
public record CleanupCategory(
    string Name,
    int ItemCount,
    long ReclaimableSize,           // 去重独占大小之和
    RiskLevel TopRisk,
    string RecommendedAction);

/// <summary>
/// 按软件聚合 (S-F): 把归属相同的项归并, 回答"我的空间被哪些软件占了 + 各能清多少"。
/// 比按风险更贴近用户语言。<see cref="TotalSize"/>/<see cref="CleanableSize"/> 均为去重独占大小之和。
/// </summary>
public record SoftwareUsage(
    string Name,
    int ItemCount,
    long TotalSize,         // 该软件名下全部项的去重独占大小之和
    long CleanableSize,     // 其中 A/B 可清理部分 (去重)
    RiskLevel TopRisk);

/// <summary>
/// 整盘清理参谋的输入 (S-H): **脱敏聚合**——只含软件名/类别名/容量/计数, **绝无路径与文件内容**,
/// 因此可安全出云 (PR-1)。AI 据此做跨项推理 (冗余工具链/重复缓存/优先级)。
/// </summary>
public record CleanupSummary(
    long TotalSize,
    long ReclaimableSize,
    IReadOnlyList<SoftwareUsage> Software,
    IReadOnlyList<CleanupCategory> Categories);

/// <summary>扫描报告 (报告导出输入)。<paramref name="AiCleanupAdvice"/> 为可选的整盘 AI 参谋文本 (S-H)。</summary>
public record ScanReport(
    ScanTask Task,
    IReadOnlyList<DecisionItem> Items,
    string? AiCleanupAdvice = null);

/// <summary>已脱敏的 AI 输入 (脱敏网关唯一产物; 仅 P0 + 脱敏 P1; 永不含文件内容, PR-1)。</summary>
public record AiInput(
    string PathPattern,        // 脱敏: %USER%/%FILE%
    string? Extension,
    long Size,
    NodeType? NodeType,
    string? MatchedRuleCategory,
    RiskLevel? RuleRiskLevel,
    bool IsSystemCritical,
    IReadOnlyList<string> Facts,                       // 仅 is_fact=1 的脱敏证据
    IReadOnlyList<AttributionCandidate> RelatedApps,
    double? Confidence);

/// <summary>操作请求 (辅助操作或删除意图)。<paramref name="Payload"/> 携带命令/URI 等 (如清理命令)。</summary>
public record ActionRequest(
    long? FileId,
    string TargetPath,
    ActionType Action,
    string? Payload = null);

/// <summary>安全闸门判定结果 (架构§安全闸门)。</summary>
public enum GuardOutcome { Allowed, Rejected }

public record GuardDecision(
    GuardOutcome Outcome,
    string Reason,
    string? RecommendedAlternative);

/// <summary>已安装应用 (归因证据来源)。</summary>
public record InstalledApp(
    string Name,
    string? Publisher,
    string? InstallLocation,
    string? Source);           // Registry / WinGet / Appx
