using System.Text.RegularExpressions;

namespace CleanScope.Core.Rules;

/// <summary>
/// 规则引擎 (裁决链权威环, 架构§3 / 知识库§3)。对 <see cref="FileNode"/> 匹配已加载规则,
/// 冲突按 priority 就高、再 risk 就高, 输出唯一**权威** <see cref="RuleMatch"/> (Authoritative=true)。
///
/// 安全:
///  - 匹配在真实路径上进行 (node.RealPath ?? node.Path), 防 symlink 绕过黑名单 (IR-4)。
///  - 系统关键规则 priority=100, 必然压过缓存/扩展名等低优先规则 → 黑名单必命中、不被放宽 (SR-6/IR-5)。
///  - 引擎只产出权威结论; AI 旁路, 结构上无法覆盖 (架构 AI 旁路)。
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private readonly IReadOnlyList<CompiledRule> _rules;

    /// <param name="rules">已加载并展开环境变量的规则 (来自 <c>IRuleSource</c>)。顺序无关, 引擎自行裁决最优。</param>
    public RuleEngine(IReadOnlyList<RuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.Select(CompiledRule.Compile).ToList();
    }

    public RuleMatch? Match(FileNode node, EvidenceBundle evidence)
    {
        ArgumentNullException.ThrowIfNull(node);
        var path = Normalize(node.RealPath ?? node.Path);
        var name = node.Name;

        RuleDefinition? best = null;
        foreach (var cr in _rules)
        {
            if (!cr.IsMatch(path, name, node.IsDirectory)) continue;
            if (best is null || IsBetter(cr.Rule, best)) best = cr.Rule;
        }
        return best is null ? null : Build(node, best);
    }

    // 冲突裁决: priority 就高 → risk 就高 (A<B<C<D<E) → pattern 更具体(更长)兜底。
    private static bool IsBetter(RuleDefinition cand, RuleDefinition cur)
    {
        if (cand.Priority != cur.Priority) return cand.Priority > cur.Priority;
        if (cand.RiskLevel != cur.RiskLevel) return cand.RiskLevel > cur.RiskLevel;
        return cand.Pattern.Length > cur.Pattern.Length;
    }

    private static RuleMatch Build(FileNode node, RuleDefinition r) => new(
        Id: 0,
        FileId: node.Id,                 // 0 时由编排层持久化时 stamp 真实 file_id
        RuleId: r.Id,
        Category: r.Category,
        RiskLevel: r.RiskLevel,
        DirectDelete: r.DirectDelete,
        IsSystemCritical: r.IsSystemCritical,
        RecommendedAction: r.RecommendedAction,
        Confidence: r.Confidence,
        Priority: r.Priority,
        Authoritative: true,             // 规则结论恒权威
        Command: r.Command);

    // 统一分隔符 + 去尾分隔符; 比较一律 OrdinalIgnoreCase (Windows 路径)。
    private static string Normalize(string p) =>
        p.Replace('/', '\\').TrimEnd('\\');

    /// <summary>预编译规则: 把四种 match_type 编成一个判定委托, 避免每个文件重复解析模式。</summary>
    private sealed class CompiledRule
    {
        public RuleDefinition Rule { get; }
        private readonly Func<string, string, bool, bool> _match;

        private CompiledRule(RuleDefinition rule, Func<string, string, bool, bool> match)
        {
            Rule = rule;
            _match = match;
        }

        public bool IsMatch(string path, string name, bool isDir) => _match(path, name, isDir);

        public static CompiledRule Compile(RuleDefinition r)
        {
            var pattern = Normalize(r.Pattern);
            switch (r.MatchKind)
            {
                case RuleMatchKind.PathPrefix:
                    return new CompiledRule(r, (path, _, _) => PrefixMatch(path, pattern));

                case RuleMatchKind.PathGlob:
                    // glob → 正则: * 跨段匹配(.*); 命中后允许其下后代 (边界 $ 或 \)。
                    var rx = new Regex("^" + GlobToRegex(pattern) + @"($|\\)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
                    return new CompiledRule(r, (path, _, _) => rx.IsMatch(path));

                case RuleMatchKind.DirName:
                    // 任一路径段等于该名 → 命中 (覆盖目录本身及其后代, 如 $Recycle.Bin)。
                    return new CompiledRule(r, (path, _, _) => SegmentEquals(path, pattern));

                case RuleMatchKind.Extension:
                    var ext = ExtensionOf(r.Pattern);   // "*.log" → ".log"
                    return new CompiledRule(r, (path, _, isDir) =>
                        !isDir && string.Equals(GetExt(path), ext, StringComparison.OrdinalIgnoreCase));

                default:
                    return new CompiledRule(r, static (_, _, _) => false);
            }
        }

        // 前缀命中需落在路径段边界: 等于自身, 或以 "pattern\" 开头 (避免 C:\Windows 误配 C:\WindowsApps)。
        private static bool PrefixMatch(string path, string pattern) =>
            path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(pattern + "\\", StringComparison.OrdinalIgnoreCase);

        private static bool SegmentEquals(string path, string segment)
        {
            foreach (var seg in path.Split('\\'))
                if (seg.Equals(segment, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GlobToRegex(string glob) =>
            Regex.Escape(glob).Replace(@"\*", ".*");   // 转义后 \* 是字面星号 → .*

        private static string ExtensionOf(string pattern)
        {
            var star = pattern.LastIndexOf('*');
            return star >= 0 ? pattern[(star + 1)..] : pattern;
        }

        private static string GetExt(string path)
        {
            var dot = path.LastIndexOf('.');
            var sep = path.LastIndexOf('\\');
            return dot > sep ? path[dot..] : string.Empty;
        }
    }
}
