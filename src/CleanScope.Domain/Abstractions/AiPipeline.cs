namespace CleanScope.Domain.Abstractions;

// AI 旁路契约 (架构§4, 仅建议非裁决)。仅签名 (T0.5)。
// 注意: 这些接口定义在 Domain, 实现在 CleanScope.Ai; Ai 程序集不引用 Safety (决议10)。

/// <summary>脱敏网关: 出云唯一通道 (IR-7)。把分析脱敏为 AiInput, 永不含文件内容 (PR-1/3/4)。</summary>
public interface ISanitizationGateway
{
    AiInput Sanitize(FileAnalysis analysis);
}

/// <summary>
/// AI 解释服务: 把脱敏输入转自然语言解释。
/// 硬要求: 支持降级——AI 不可用/离线/超时时回退基于规则的解释, 核心功能不依赖 AI 在线 (决议5)。
/// </summary>
public interface IExplanationService
{
    Task<AiExplanation> ExplainAsync(AiInput input, CancellationToken ct = default);
    bool IsCloudEnabled { get; }     // 隐私开关: 可一键关云端走纯本地 (PR-5)
}

/// <summary>
/// AI 输出校验器 (AS-1~8): 风险不得低于引擎判定; 与规则冲突降级为 E;
/// 缺证据/置信度判为非法 (不展示)。返回 Validated 标记后的解释。
/// </summary>
public interface IAiOutputValidator
{
    AiExplanation Validate(AiExplanation raw, RuleMatch? ruleMatch, RiskAssessment risk);
}

/// <summary>
/// 整盘清理参谋 (S-H): 对脱敏聚合 (<see cref="CleanupSummary"/>, 不含路径/文件内容) 做一次跨项推理,
/// 给冗余工具链/重复缓存/优先级建议。纯建议文本, **绝不产出可执行删除指令**; 输出仅展示, 不被解析/执行 (IR-6)。
/// 降级: AI 不可用/失败 → 返回 null, 核心功能不依赖 (决议5)。
/// </summary>
public interface ICleanupAdvisor
{
    bool Enabled { get; }

    /// <summary>
    /// 生成跨项清理建议。<paramref name="officialActions"/> 为本机适用的系统级官方手段 (关闭休眠/磁盘清理等,
    /// 含预估收益), 让 AI 把"网上常见手段 + 本机实测"结合成带优先级的可执行计划, 而非泛泛而谈。可传空。
    ///
    /// <paramref name="concreteItems"/>: 已按当前脱敏档位处理过的"具体大项"描述行 (如"路径 | 大小 | 风险")。
    /// 关闭脱敏时由宿主附上真实路径以便 AI 给个性化建议; 严格档位下应传空 (保持仅聚合)。可传空。
    /// </summary>
    Task<CleanupPlan?> AdviseAsync(
        CleanupSummary summary,
        IReadOnlyList<OfficialCleanupAction>? officialActions = null,
        IReadOnlyList<string>? concreteItems = null,
        CancellationToken ct = default);
}
