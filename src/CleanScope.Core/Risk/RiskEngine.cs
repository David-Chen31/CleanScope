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

    // S5: 目录名明确表明为"可重建缓存/临时/日志/崩溃转储"的信号 (仅用于目录, 降低 E 泛滥)。
    // 命中 → B (走官方方式清理), 而非 E。永不置 canDelete (MVP 零删除, 安全闸门仍拦一切删除)。
    private static readonly HashSet<string> CacheLeafNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "temp", "tmp", "log", "logs", "crashpad", "crashdumps", "crashes",
        "blob_storage", "thumbnails", "thumbcache", "webcache", "inetcache",
    };

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

        // 无规则命中: 路径启发。
        if (PersonalDataRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.C, 50, new[] { "用户个人数据 (误删=数据丢失)" }, 0.75, false);

        // S5: 目录名表明为可重建缓存/临时/日志 → B (从 E 里救出, 让真实可回收空间显形)。
        if (node.IsDirectory && IsRebuildableCacheDir(node.Name))
            return Build(node, evidence, RiskLevel.B, 35,
                new[] { "目录名表明为可重建缓存/临时, 建议用官方方式清理" }, 0.55, canDelete: false);

        if (RoamingAppDataRx.IsMatch(path) || LocalAppDataRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.C, 50, new[] { "应用数据/配置 (删后软件重置或丢登录态)" }, 0.6, false);

        // S4: 程序安装/共享数据目录 → C (通过卸载程序处理), 比 E 更可行更准确。
        if (InstalledLocationRx.IsMatch(path))
            return Build(node, evidence, RiskLevel.C, 50,
                new[] { "位于程序安装/共享数据目录, 建议通过卸载程序移除, 勿直删" }, 0.6, false);

        // 高置信归因可作为充分证据 (Phase 2 起生效); 否则证据不足 → E。
        if (attributions.Any(a => a.Confidence >= 0.8))
            return Build(node, evidence, RiskLevel.C, 50, new[] { "归因明确但无清理方式, 默认谨慎" }, 0.6, false);

        // Q4: 证据不足/来源不明 → E (无法判断, 不建议删除)。
        return Build(node, evidence, RiskLevel.E, 85, new[] { "证据不足/来源不明, 无法判断" }, 0.2, false);
    }

    // 目录名 (叶子) 是否表明可重建缓存: 含 "cache" 或属已知缓存/临时/日志/崩溃名。
    private static bool IsRebuildableCacheDir(string leaf) =>
        !string.IsNullOrEmpty(leaf) &&
        (leaf.Contains("cache", StringComparison.OrdinalIgnoreCase) || CacheLeafNames.Contains(leaf));

    private static bool IsInUse(EvidenceBundle evidence) =>
        evidence.Metadata?.InUse == true ||
        evidence.Evidences.Any(e => e.Kind == EvidenceKind.Process && e.IsFact);

    private static RiskAssessment Build(
        FileNode node, EvidenceBundle evidence, RiskLevel level, int score,
        IReadOnlyList<string> factors, double confidence, bool canDelete)
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
            CreatedAt: DateTime.UtcNow);
    }
}
