using System.Text.RegularExpressions;

namespace CleanScope.Core.Risk;

/// <summary>
/// 风险引擎 (裁决链权威环, 风险分级细则)。实现 §2 决策树 + §4 评分护栏 + §5 置信度门槛,
/// 永远返回评估 (最坏 E, fail-safe IR-8); 输出 <see cref="RiskAssessment"/>。
///
/// 安全:
///  - SR-5: EvidenceChain 必须非空 —— 由本引擎从证据包事实证据 id 构建;
///          调用方须保证每个被评文件至少有 1 条观测证据 (编排层 T1.11 合成), DB CHECK 兜底。
///  - 护栏 (§4.3): 命中 D 规则强制 D; 占用 floor C; 低置信(&lt;0.5)不得 A/B → 收紧为 C; 减分不可跨级降危。
///  - 默认落点 C (谨慎), 非 A; 证据不足一律 E, 绝不 fail-open。
///
/// Phase 1 简化输入: 主要依据 RuleMatch + 路径启发; 占用/签名/归因等真实证据在 T2.6 接入后复跑 §6 样例。
/// </summary>
public sealed class RiskEngine : IRiskEngine
{
    private const double LowConfidence = 0.5;

    private static readonly Regex PersonalDataRx = new(
        @"\\Users\\[^\\]+\\(Documents|Desktop|Pictures|Videos|Music)(\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RoamingAppDataRx = new(
        @"\\AppData\\Roaming\\[^\\]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // S4: Local 应用数据 (非缓存) —— 与 Roaming 同级处理为 C。
    private static readonly Regex LocalAppDataRx = new(
        @"\\AppData\\Local(Low)?\\[^\\]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // S4: 程序安装 / 共享数据目录 → C (通过卸载程序处理, 勿直删), 不再落 E。
    private static readonly Regex InstalledLocationRx = new(
        @"\\(Program Files( \(x86\))?|ProgramData)\\[^\\]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // 系统区: 即便"三无"也应保守标 E (未知=可疑)。Windows 目录、回收站、卷信息、恢复/启动等。
    // 用户区 (数据盘、用户自建目录) 的三无项则视为个人文件 (C), 不再一律 E。
    private static readonly Regex SystemAreaRx = new(
        @"\\Windows(\\|$)" +
        @"|^[A-Za-z]:\\\$Recycle\.Bin" +
        @"|^[A-Za-z]:\\System Volume Information" +
        @"|^[A-Za-z]:\\(Recovery|Boot|PerfLogs|Config\.Msi)(\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // S-B: 顶层容器目录 (盘符根/Users/用户主目录/AppData[\Local|LocalLow|Roaming]/Program Files[(x86)]/ProgramData)。
    // 这些只是"装东西的柜子", 不是删除对象 —— 标 IsContainer, UI 单列"容器"桶, 不进风险/可清理统计。
    private static readonly Regex ContainerRx = new(
        @"^[A-Za-z]:\\?$" +
        @"|^[A-Za-z]:\\Users\\?$" +
        @"|^[A-Za-z]:\\Users\\[^\\]+\\?$" +
        @"|^[A-Za-z]:\\Users\\[^\\]+\\AppData\\?$" +
        @"|^[A-Za-z]:\\Users\\[^\\]+\\AppData\\(Local|LocalLow|Roaming)\\?$" +
        @"|^[A-Za-z]:\\Program Files( \(x86\))?\\?$" +
        @"|^[A-Za-z]:\\ProgramData\\?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);


    public RiskAssessment Assess(
        FileNode node, EvidenceBundle evidence, RuleMatch? ruleMatch, IReadOnlyList<AttributionCandidate> attributions)
    {
        try
        {
            return AssessCore(node, evidence, ruleMatch, attributions);
        }
        catch
        {
            // IR-8: 判定过程异常 → E, 绝不 fail-open。
            return Build(node, evidence, RiskLevel.E, 90, new[] { "判定过程异常 (fail-safe)" }, 0.0, false);
        }
    }

    private static RiskAssessment AssessCore(
        FileNode node, EvidenceBundle evidence, RuleMatch? ruleMatch, IReadOnlyList<AttributionCandidate> attributions)
    {
        var path = node.RealPath ?? node.Path;

        // Q1: 系统关键黑名单 / 禁删类型 → D (权威, AI 不可翻案)。
        if (ruleMatch?.RiskLevel == RiskLevel.D)
        {
            var critical = ruleMatch.IsSystemCritical == true;
            return Build(node, evidence, RiskLevel.D, critical ? 80 : 70,
                new[] { critical ? "命中系统关键黑名单" : "命中高风险/禁删类型", $"规则 {ruleMatch.RuleId}" },
                ruleMatch.Confidence ?? 0.9, canDelete: false);
        }

        // Q2: 被运行中进程占用 → 至少 C (占用是强信号, 抬升 A/B)。
        if (IsInUse(evidence))
            return Build(node, evidence, RiskLevel.C, 55, new[] { "被运行中进程占用" }, 0.7, false);

        // 规则驱动的非 D 等级 (B/A/C): 规则权威优先于通用启发。
        if (ruleMatch is not null)
        {
            var conf = ruleMatch.Confidence ?? 0.6;
            bool lowConf = conf < LowConfidence;
            switch (ruleMatch.RiskLevel)
            {
                case RiskLevel.C:
                    return Build(node, evidence, RiskLevel.C, 50,
                        new[] { "规则判定谨慎", $"规则 {ruleMatch.RuleId}" }, conf, false);
                case RiskLevel.B:
                    return lowConf
                        ? Build(node, evidence, RiskLevel.C, 50, new[] { "低置信收紧为谨慎", $"规则 {ruleMatch.RuleId}" }, conf, false)
                        : Build(node, evidence, RiskLevel.B, 30, new[] { "有官方清理方式", $"规则 {ruleMatch.RuleId}" }, conf, false);
                case RiskLevel.A:
                    return lowConf
                        ? Build(node, evidence, RiskLevel.C, 50, new[] { "低置信收紧为谨慎", $"规则 {ruleMatch.RuleId}" }, conf, false)
                        : Build(node, evidence, RiskLevel.A, 10, new[] { "明确临时/可再生成且无依赖", $"规则 {ruleMatch.RuleId}" },
                            conf, canDelete: ruleMatch.DirectDelete == true);
            }
        }

        // S-B 顶层容器目录: 仅供浏览, 不作删除对象 (标 IsContainer)。放在其它启发之前。
        // 每个容器给具体"存在解释"(ContainerPurpose), 不再是笼统一句。
        if (node.IsDirectory && ContainerRx.IsMatch(path))
        {
            var desc = Attribution.ContainerPurpose.Describe(path)?.Full ?? "顶层容器目录, 内含多个程序的数据";
            return Build(node, evidence, RiskLevel.C, 40,
                new[] { desc, "请展开按子目录判断, 不要整体处理" }, 0.5,
                canDelete: false, isContainer: true);
        }

        // 无规则命中: 路径启发。
        if (PersonalDataRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.C, 50, new[] { "用户个人数据 (误删=数据丢失)" }, 0.75, false);

        // P2: 目录名表明为可重建缓存/临时/日志, 或可重建的开发依赖/产物 → B (任意深度; 让深埋的缓存显形为可清理)。
        if (node.IsDirectory && CacheHeuristics.IsRebuildableCacheDir(node.Name))
        {
            var note = CacheHeuristics.IsDependencyDir(node.Name)
                ? "可重建的开发依赖/产物 (如 node_modules), 删后需重新安装依赖或重新构建; 仍只进回收站可还原"
                : "目录名表明为可重建缓存/临时, 删后会自动重建, 建议用官方方式清理";
            return Build(node, evidence, RiskLevel.B, 35, new[] { note }, 0.55, canDelete: false);
        }

        if (RoamingAppDataRx.IsMatch(path) || LocalAppDataRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.C, 50, new[] { "应用数据/配置 (删后软件重置或丢登录态)" }, 0.6, false);

        // S4: 程序安装/共享数据目录 → C (通过卸载程序处理), 比 E 更可行更准确。
        if (InstalledLocationRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.C, 50,
                new[] { "位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删" }, 0.6, false);

        // S-A 有主即非 E: 只要归因出任意候选 (含路径推断), 就不是"无法判断", 而是"归属 X, 谨慎"。
        // "有主"却"无法判断"自相矛盾; 用户最关心"这归哪个软件", 此处把它从 E 救到 C 并写明归属。
        var owner = attributions.OrderByDescending(a => a.Confidence).FirstOrDefault();
        if (owner is not null)
            return Build(node, evidence, RiskLevel.C, 50,
                new[] { $"归属 {owner.AppName}, 暂无清理方式, 谨慎处理" }, Math.Max(0.4, owner.Confidence), false);

        // 三无 (无规则/无归因/无缓存特征): 区分系统区与用户区, 别把一整盘个人资料都判成最高危。
        //  · 系统区 (Windows/回收站/卷信息/恢复等) 未知项 → E (保守, 未知=可疑, 不建议删)。
        //  · 其余 (数据盘、用户自建目录等) 极可能是用户自己的文件 → C「个人文件, 自行判断」。
        //    仍非可清理桶、无直删入口, 安全模型不变 (闸门仍独立把关); 只是不再渲染成一片危险红。
        if (SystemAreaRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.E, 85,
                new[] { "位于系统区且无规则/无归因, 无法判断, 不建议删除" }, 0.2, false);

        return Build(node, evidence, RiskLevel.C, 45,
            new[] { "个人文件 (非系统位置, 非软件产生), 建议自行判断" }, 0.45, false);
    }

    private static bool IsInUse(EvidenceBundle evidence) =>
        evidence.Metadata?.InUse == true ||
        evidence.Evidences.Any(e => e.Kind == EvidenceKind.Process && e.IsFact);

    private static RiskAssessment Build(
        FileNode node, EvidenceBundle evidence, RiskLevel level, int score,
        IReadOnlyList<string> factors, double confidence, bool canDelete, bool isContainer = false)
    {
        // SR-5: 证据链非空。优先事实证据; 退化时取全部证据 id。契约: 调用方至少提供 1 条。
        // 对 null 证据防御 (仅 fail-safe 异常路径可能出现): fail-safe 本身绝不能再崩。
        var all = evidence?.Evidences ?? Array.Empty<Evidence>();
        var chain = all.Where(e => e.IsFact).Select(e => e.Id).ToList();
        if (chain.Count == 0)
            chain = all.Select(e => e.Id).ToList();

        return new RiskAssessment(
            Id: 0,
            FileId: node.Id,
            Level: level,
            Score: score,
            Factors: factors,
            EvidenceChain: chain,
            CanDeleteDirectly: canDelete,
            Confidence: confidence,
            CreatedAt: DateTime.UtcNow,
            IsContainer: isContainer);
    }
}
