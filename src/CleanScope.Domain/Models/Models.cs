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
    string? AiInvestigation = null,  // S-C: AI 对"未知项"的调查推测 (已校验, 仅供参考, 不改判风险)
    string? Origin = null,        // 统一"来源/归属"短标签: 应用 ▸ 系统来源 ▸ 容器角色 ▸ 未知 (保证非空)
    bool IsDirectory = true);     // 文件 vs 目录 (资源管理器树按此区分图标; 默认目录)

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

/// <summary>
/// 全盘目录树节点 (P1: 资源管理器式浏览)。<paramref name="Size"/>=聚合大小; Children 已按大小降序。
/// <see cref="Remainder"/>=直接文件与被剪枝小目录的余量。带**轻量路径级分类** (来源/用途/风险/可清理),
/// 不含逐文件证据 (那是点开详情时按需做的)。
/// </summary>
public sealed class ScanTreeNode
{
    public ScanTreeNode(string path, string name, long size, RiskLevel riskLevel, bool isContainer,
        bool isCleanable, string origin, string? purpose, string recommendedAction, bool isDirectory = true)
    {
        Path = path;
        Name = name;
        Size = size;
        RiskLevel = riskLevel;
        IsContainer = isContainer;
        IsCleanable = isCleanable;
        Origin = origin;
        Purpose = purpose;
        RecommendedAction = recommendedAction;
        IsDirectory = isDirectory;
    }

    public string Path { get; }
    public string Name { get; }
    public long Size { get; }
    public RiskLevel RiskLevel { get; }
    public bool IsContainer { get; }
    public bool IsDirectory { get; }      // 目录 vs 文件 (图标区分)
    public bool IsCleanable { get; }      // A/B 且非容器: 可移入回收站 (仍走闸门)
    public string Origin { get; }         // 来源/归属 (统一标签, 保证非空)
    public string? Purpose { get; }       // 用途/存在解释
    public string RecommendedAction { get; }

    public List<ScanTreeNode> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public long ChildrenSize => Children.Sum(c => c.Size);
    public long Remainder => Math.Max(0, Size - ChildrenSize);  // 直接文件 + 被剪枝的小子目录
}

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

/// <summary>公司/签名者别名: 原始串含 <see cref="Contains"/> → 归一为友好名 <see cref="Name"/> (① 特征库数据)。</summary>
public readonly record struct VendorAlias(string Contains, string Name);

/// <summary>目录名别名: 叶子名匹配 <see cref="Name"/> → 归属 <see cref="App"/> (可带 <see cref="Purpose"/>; ① 特征库数据)。</summary>
public readonly record struct DirectoryAlias(string Name, string App, string? Purpose);

/// <summary>
/// 软件语义描述 (① 特征库数据): 应用名 <see cref="App"/> → 一句话"它是什么/干嘛的" <see cref="Description"/>
/// (如 Steam→游戏平台、Zed→代码编辑器)。补足"知道是哪个软件、却说不清它是干嘛的"语义缺口 (问题#1)。
/// </summary>
public readonly record struct AppDescription(string App, string Description);

/// <summary>知名软件特征库原始数据 (KnownSoftwareLoader 产物; 由 Core 的 KnownSoftwareCatalog 包装出匹配能力)。</summary>
public record KnownSoftwareData(
    IReadOnlyList<VendorAlias> Vendors,
    IReadOnlyList<DirectoryAlias> Directories,
    IReadOnlyList<AppDescription> Apps)
{
    public static KnownSoftwareData Empty { get; } =
        new(Array.Empty<VendorAlias>(), Array.Empty<DirectoryAlias>(), Array.Empty<AppDescription>());
}

/// <summary>
/// 跨盘迁移请求 (P0): 把 <paramref name="SourceDir"/> 搬到 <paramref name="TargetRootDir"/> 下 (按原名建子目录),
/// 并在原位创建目录联接 (junction), 对应用透明。用于"占大头但不能删的合法软件"——删除是错的工具, 搬家才对。
/// </summary>
public record MigrationRequest(string SourceDir, string TargetRootDir);

/// <summary>迁移结果。Success=已迁移并建联接; Rejected=未通过安全校验; Failed=执行中出错 (尽力回滚)。</summary>
public enum MigrationOutcome { Success, Rejected, Failed }

/// <summary>
/// 迁移器策略开关 (默认全开)。生产环境恒为默认 (保守白名单 + 强制跨盘);
/// 仅单元测试在受控临时目录上验证复制/校验/改名/联接/回滚编排时才放宽。
/// </summary>
public record MigrationOptions(bool EnforceUserDataScope = true, bool EnforceCrossDrive = true);

/// <summary>
/// 迁移结果。<paramref name="NewLocation"/>=目标盘新位置; <paramref name="BackupPath"/>=原目录被改名留存的备份
/// (确认软件正常后可移入回收站以释放原盘空间; 我们绝不自动永久删除); <paramref name="BytesMoved"/>=迁移字节数。
/// </summary>
public record MigrationResult(
    MigrationOutcome Outcome,
    string Message,
    string? NewLocation,
    string? BackupPath,
    long BytesMoved);

/// <summary>
/// 系统级官方清理手段 (P0): 不依赖逐文件扫描的"整盘机会" —— 关闭休眠、清空回收站、组件清理 (DISM)、
/// 磁盘清理 (cleanmgr)、存储感知等。这些不是某个文件的规则命中, 而是机器层面的动作; 由确定性目录
/// (<see cref="CleanScope.Domain.Models"/> 之外的 Core 目录, 非 AI) 维护, 执行走**受控白名单**官方命令/设置跳转。
///
/// 红线: 我们不替厂商删文件 —— 执行 = 启动 Windows 官方工具 (powercfg/DISM/cleanmgr) 或打开设置页,
/// 命令对用户可见, 仍经安全闸门 (非破坏性辅助操作放行) + 先写审计。AI 永不生成这些命令 (确定性目录)。
/// </summary>
public record OfficialCleanupAction(
    string Id,
    string Title,                  // 中文标题, 如 "关闭休眠（移除 hiberfil.sys）"
    string Description,            // 一句话说明
    CleanupActionKind ActionKind,  // RunCommand=跑官方命令; OpenFolder=打开设置/资源管理器
    ActionType ExecAction,         // 经 ActionExecutor 执行的具体动作 (RunCleanupCommand / OpenSettings)
    string Payload,                // 命令 (RunCleanupCommand) 或 ms-settings: URI (OpenSettings)
    long EstimatedBytes,           // 预估可释放字节 (能检测则给, 0=未知)
    bool Detected,                 // 本机是否确实检测到该机会 (如 hiberfil.sys 存在)
    bool NeedsAdmin,               // 是否需要管理员权限
    string Note);                  // 可逆性/风险提示
