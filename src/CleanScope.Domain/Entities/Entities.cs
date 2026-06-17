namespace CleanScope.Domain.Entities;

// 领域实体 —— 纯数据形状, 不可变 record, 无业务逻辑 (T0.4)。
// 字段与「数据模型设计.md §4/§5」表结构一一对应。
// 安全契约字段: Evidence.IsFact (事实/推测), RiskAssessment.EvidenceChain (非空, SR-5),
//               AiExplanation.Validated (未校验不展示)。
// 持久化映射由 Infrastructure 手写 SQL 完成 (不用 EF, 决议8); 此处仅定义形状。

/// <summary>扫描任务 (表 scan_task)。一次扫描的根记录。</summary>
public record ScanTask(
    long Id,
    string TargetPath,        // P1: 扫描根路径
    ScanMode Mode,
    ScanStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    long? TotalSize,
    long? FileCount,
    string AppVersion);       // 产生数据的应用版本, 可追溯

/// <summary>文件/目录节点 (表 file_node)。ParentId 自引用构成目录树。</summary>
public record FileNode(
    long Id,
    long TaskId,
    long? ParentId,           // 目录树父节点, 根为 null
    string Path,              // P1: 完整路径(原始)
    string? RealPath,         // P1: 解析符号链接后的真实路径 (IR-4 防护)
    string Name,              // P1: 文件/目录名
    bool IsDirectory,
    bool IsReparsePoint,      // 是否 symlink/junction
    long Size,                // 字节(目录为聚合大小)
    NodeType? NodeType,
    DateTime? Mtime,
    DateTime? Atime,          // 弱参考
    AccessState AccessState,
    PreliminaryClass? PreliminaryClass,
    DateTime CreatedAt);

/// <summary>文件元数据 (表 file_metadata)。归因关键。1:0..1 于 FileNode。</summary>
public record FileMetadata(
    long FileId,
    string? Extension,
    string? Description,
    string? ProductName,
    string? CompanyName,
    string? FileVersion,
    bool? IsSigned,
    string? Signer,           // 签名者(如 Microsoft Corporation)
    string? Sha256,           // 内容哈希摘要(不可逆, 非内容本身)
    bool? InUse,              // 是否被进程占用 (IR-2 删除前置)
    string? OccupyingProcess);// P1: 占用进程名(其完整路径属 P1)

/// <summary>证据 (表 evidence)。证据链原子项。IsFact 区分事实与 AI 推测 (安全§9)。</summary>
public record Evidence(
    long Id,
    long FileId,
    EvidenceKind Kind,
    string Value,             // P1: 证据内容(可能含路径), 上云需脱敏
    string? Source,
    bool IsFact,              // true=事实证据(可驱动权威结论); false=AI推测(仅供解释)
    double? Weight,
    DateTime CreatedAt);

/// <summary>规则匹配 (表 rule_match)。规则引擎权威输出, AI 不可覆盖。</summary>
public record RuleMatch(
    long Id,
    long FileId,
    string RuleId,
    string? Category,
    RiskLevel? RiskLevel,
    bool? DirectDelete,
    bool? IsSystemCritical,   // 系统关键 → 黑名单, priority 最高
    string? RecommendedAction,
    double? Confidence,
    int? Priority,            // 冲突时就高
    bool Authoritative);      // 规则权威标记

/// <summary>归因候选 (表 attribution_candidate)。候选列表 + 置信度, 非单一答案。</summary>
public record AttributionCandidate(
    long Id,
    long FileId,
    string AppName,
    double Confidence,
    int? Rank,
    IReadOnlyList<long> SupportingEvidenceIds); // 支撑证据 id (JSON 列映射)

/// <summary>风险评估 (表 risk_assessment)。权威, EvidenceChain 必须非空 (SR-5)。</summary>
public record RiskAssessment(
    long Id,
    long FileId,
    RiskLevel Level,
    int Score,                // 0-100, 评分护栏: 黑名单强制 ≥61
    IReadOnlyList<string> Factors,
    IReadOnlyList<long> EvidenceChain, // 非空; 无证据不出结论
    bool CanDeleteDirectly,
    double? Confidence,
    DateTime CreatedAt,
    bool IsContainer = false); // 顶层容器目录 (仅供浏览, 不作删除对象; UI 单列"容器"桶)

/// <summary>AI 解释 (表 ai_explanation)。Validated=false 禁止展示 (架构§5)。</summary>
public record AiExplanation(
    long Id,
    long FileId,
    string? WhatIsIt,
    string? OwnerApp,
    RiskLevel? RiskLevel,     // 不得低于引擎判定 (AS-2, 由校验器保证)
    bool? CanDeleteDirectly,
    string? RecommendedAction,
    IReadOnlyList<string> Reasoning,
    double? Confidence,
    string? UserFriendlyExplanation,
    bool Validated,           // 通过 AiOutputValidator 才为 true
    string? ModelUsed,
    bool IsCloud,             // 是否走了云端(隐私审计)
    DateTime CreatedAt);

/// <summary>用户决策 (表 user_decision)。</summary>
public record UserDecision(
    long Id,
    long FileId,
    DecisionType Decision,
    string? Note,             // P2 机密: 用户自由文本, 永不上云
    DateTime DecidedAt);

/// <summary>忽略名单 (表 ignore_entry)。全局表, 跨任务持续生效。</summary>
public record IgnoreEntry(
    long Id,
    string PathOrPattern,     // P1
    CleanScope.Domain.Enums.MatchType MatchType, // 全限定: 避免与 System.IO.MatchType 冲突
    string? Reason,           // P2 机密
    DateTime CreatedAt);

/// <summary>操作日志 (表 action_log)。审计, 先写后执行 (SR-9)。整表仅本地。</summary>
public record ActionLog(
    long Id,
    long? FileId,
    string? TargetPath,       // P1
    ActionType Action,
    string? BeforeState,      // 操作前状态快照(JSON)
    string? RecycleBinLocation, // 删除后回收站定位(可恢复)
    bool Recoverable,
    Operator Operator,
    ActionResult Result,
    string? RejectReason,     // 被拒原因(如命中黑名单)
    string AppVersion,
    DateTime Timestamp);
