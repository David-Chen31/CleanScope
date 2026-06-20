using System.Text.RegularExpressions;

namespace CleanScope.Ai.Sanitization;

/// <summary>
/// 脱敏网关 (实现 <see cref="ISanitizationGateway"/>): 出云**唯一通道** (IR-7)。
/// 把 <see cref="FileAnalysis"/> 转为最小必要的 <see cref="AiInput"/>:
///  - 用户名 → <c>%USER%</c>, 文件/目录名 → <c>%FILE%</c> (P1 脱敏, 数据模型§6)。
///  - 只携带 §7 允许上云的 P0 字段 + is_fact=1 的脱敏证据; 进程仅名不带路径。
///  - **永不含文件内容** (系统从源头不存内容, PR-1); 不含原始完整路径/文件名 (PR-3/4)。
/// </summary>
public sealed class SanitizationGateway : ISanitizationGateway
{
    private static readonly Regex UserRx =
        new(@"(\\Users\\)[^\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 当前出云脱敏档位 (问题#3, 运行时可由设置页热更新)。默认 <see cref="SanitizationLevel.Strict"/> (最保守)。
    /// 仅影响"路径/名称"如何脱敏; 三档都永不发送文件内容 (PR-1)。
    /// </summary>
    public SanitizationLevel Level { get; set; } = SanitizationLevel.Strict;

    public AiInput Sanitize(FileAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var node = analysis.Node;

        var facts = analysis.Evidence.Evidences
            .Where(e => e.IsFact && e.Kind != EvidenceKind.AiInference)   // 仅事实证据 (§9/§7)
            .Select(e => $"{e.Kind}: {SanitizeValue(e.Value, node)}")
            .ToList();

        return new AiInput(
            PathPattern: BuildPathPattern(node),
            Extension: node.IsDirectory ? null : Ext(node.Name),
            Size: node.Size,
            NodeType: node.NodeType,
            MatchedRuleCategory: analysis.RuleMatch?.Category,
            RuleRiskLevel: analysis.RuleMatch?.RiskLevel,
            IsSystemCritical: analysis.RuleMatch?.IsSystemCritical == true,
            Facts: facts,
            RelatedApps: analysis.Attributions,   // 应用名+置信度, P0 (§7)
            Confidence: analysis.Risk.Confidence);
    }

    // 路径模式按档位:
    //  Off      → 真实相对路径 (不脱敏, 识别最准)。
    //  Balanced → 抹用户名, **保留叶子名** (AI 能认出 Steam/Zed 等具体软件)。
    //  Strict   → 抹用户名 + 叶子名→%FILE% (名称绝不出本机, 默认)。
    private string BuildPathPattern(FileNode node)
    {
        if (Level == SanitizationLevel.Off)
            return node.Path;

        var dir = Path.GetDirectoryName(node.Path) ?? string.Empty;
        var sdir = UserRx.Replace(dir, "$1%USER%");
        var leaf = Level == SanitizationLevel.Balanced
            ? node.Name                                              // 保留真实文件夹/文件名
            : node.IsDirectory ? "%FILE%" : "%FILE%" + Ext(node.Name);
        return sdir.Length == 0 ? leaf : sdir + "\\" + leaf;
    }

    // 证据值脱敏: Off 不脱敏; 其余抹用户名; Strict 再把真实叶子名替换为 %FILE%。
    private string SanitizeValue(string value, FileNode node)
    {
        if (Level == SanitizationLevel.Off) return value;
        var s = UserRx.Replace(value, "$1%USER%");
        if (Level == SanitizationLevel.Strict && !string.IsNullOrEmpty(node.Name))
            s = s.Replace(node.Name, "%FILE%", StringComparison.OrdinalIgnoreCase);
        return s;
    }

    private static string? Ext(string name) =>
        Path.GetExtension(name) is { Length: > 0 } e ? e : null;
}
