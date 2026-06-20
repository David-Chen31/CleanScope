namespace CleanScope.Core.Decisions;

/// <summary>
/// 决策汇总 (架构§3 末环)。把逐文件 <see cref="FileAnalysis"/> 装配成面向用户的 <see cref="DecisionItem"/>:
/// 按风险分组、每项带证据链与建议。不自动执行任何操作 (MVP 零删除)。
///
/// 呈现安全:
///  - 推荐处理以规则权威结论优先, 其次 AI 解释 (仅 Validated 时), 否则按风险默认。
///  - 区分事实/推测: Explanation 仅在 AI 解释已校验时采用其措辞; 否则用事实因素 (risk.Factors)。
///  - 证据链原样透传 (risk.EvidenceChain), 供 UI/报告回溯。
/// </summary>
public sealed class DecisionService : IDecisionService
{
    public IReadOnlyList<DecisionItem> Summarize(IReadOnlyList<FileAnalysis> analyses)
    {
        ArgumentNullException.ThrowIfNull(analyses);

        var items = analyses.Select(ToItem).ToList();
        var exclusive = ComputeExclusiveSizes(items);

        return items
            .Select(i => i with { ExclusiveSize = exclusive[i.Path] })
            // 按风险分组 (A→E), 组内按占用大小降序 (大头优先)。
            .OrderBy(i => i.RiskLevel)
            .ThenByDescending(i => i.Size)
            .ToList();
    }

    /// <summary>
    /// 计算独占大小 (S1: 修复父子目录重复计数)。目录的聚合 Size 含全部子孙, 若父目录与其子目录
    /// 都在分析集中, 求和会重复计入同一批字节。此处把每个被分析节点的大小从其"最近的被分析祖先"中扣除,
    /// 使每个字节只归属到最深的被分析节点; 全集独占大小之和 = 真实占用 (不超过磁盘实际)。
    /// </summary>
    private static IReadOnlyDictionary<string, long> ComputeExclusiveSizes(IReadOnlyList<DecisionItem> items)
    {
        var exclusive = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items) exclusive[i.Path] = i.Size;  // 同路径去重 (后者覆盖, 实际不应重复)

        var paths = new HashSet<string>(exclusive.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            var ancestor = NearestAncestorInSet(i.Path, paths);
            if (ancestor is not null && !string.Equals(ancestor, i.Path, StringComparison.OrdinalIgnoreCase))
                exclusive[ancestor] -= i.Size;
        }
        return exclusive;
    }

    // 最近的祖先目录 (按路径段, 不含自身); 不在集合中则继续上溯, 到根仍无则 null。
    private static string? NearestAncestorInSet(string path, HashSet<string> set)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            if (set.Contains(dir)) return dir;
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (string.Equals(parent, dir, StringComparison.Ordinal)) break;
            dir = parent;
        }
        return null;
    }

    private static DecisionItem ToItem(FileAnalysis a)
    {
        var validatedAi = a.Explanation is { Validated: true } ? a.Explanation : null;

        return new DecisionItem(
            Path: a.Node.Path,
            Size: a.Node.Size,
            OwnerApp: OwnerOf(a, validatedAi),
            RiskLevel: a.Risk.Level,
            RecommendedAction: RecommendedActionOf(a, validatedAi),
            Explanation: ExplanationOf(a, validatedAi),
            EvidenceChain: a.Risk.EvidenceChain,
            Category: CategoryOf(a),
            IsContainer: a.Risk.IsContainer,
            ActionKind: ActionKindOf(a),
            Command: a.RuleMatch?.Command,
            AiInvestigation: AiInvestigationOf(validatedAi),
            Origin: OriginOf(a, validatedAi),
            IsDirectory: a.Node.IsDirectory);
    }

    // 统一"来源/归属"短标签 (保证非空, 落实"每个文件夹都知道是什么"):
    // 归属应用 (含系统来源/AI 推测, 经 OwnerOf) ▸ 容器角色 ▸ 容器兜底 ▸ 未知来源。
    private static string OriginOf(FileAnalysis a, AiExplanation? ai)
    {
        var owner = OwnerOf(a, ai);
        if (!string.IsNullOrWhiteSpace(owner)) return owner!;
        var path = a.Node.RealPath ?? a.Node.Path;
        if (a.Risk.IsContainer)
            return Attribution.ContainerPurpose.Describe(path)?.Short ?? "容器目录";
        // 兜底: 凭目录名能确定的 (图片/.claude 等), 给确定性来源标签, 不再笼统"未知来源"。
        return Attribution.NameHeuristics.Resolve(path)?.Origin ?? "未知来源";
    }

    // S-C: AI 对未知项的调查推测 (已校验), 作为独立、明确标注的"推测", 与规则/风险事实区分开。
    // 仅当 AI 真给出"是什么/为什么"时才有值; 否则 null (报告/UI 不显示 AI 行)。
    private static string? AiInvestigationOf(AiExplanation? ai)
    {
        if (ai is null) return null;
        var what = ai.WhatIsIt?.Trim();
        var why = ai.UserFriendlyExplanation?.Trim();
        var text = string.Join(" ", new[] { what, why }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // S-D 推荐动作: 命令型(规则带 command) → RunCommand; 容器/D/E/系统关键 → None;
    // 安装/共享目录(C, 在 Program Files/ProgramData) → Uninstall; 其余可清理/谨慎 → OpenFolder(定位)。
    private static CleanupActionKind ActionKindOf(FileAnalysis a)
    {
        if (a.Risk.IsContainer) return CleanupActionKind.None;
        if (!string.IsNullOrWhiteSpace(a.RuleMatch?.Command)) return CleanupActionKind.RunCommand;
        if (a.Risk.Level is RiskLevel.D or RiskLevel.E) return CleanupActionKind.None;

        var path = a.Node.RealPath ?? a.Node.Path;
        if (a.Risk.Level == RiskLevel.C && InstalledDirRx.IsMatch(path))
            return CleanupActionKind.Uninstall;

        return CleanupActionKind.OpenFolder;   // A/B 文件夹缓存 + 其它 C → 资源管理器定位
    }

    private static readonly System.Text.RegularExpressions.Regex InstalledDirRx = new(
        @"\\(Program Files( \(x86\))?|ProgramData)\\",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant |
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // 清理类别: 规则类别优先; 无规则但被缓存启发命中 → 推断类别 (供 S3 按类别聚合)。
    private static string? CategoryOf(FileAnalysis a)
    {
        if (!string.IsNullOrWhiteSpace(a.RuleMatch?.Category))
            return a.RuleMatch!.Category;
        if (a.Risk.Factors.Any(f => f.Contains("可重建缓存", StringComparison.Ordinal)))
            return "可重建缓存(按目录名推断)";
        return null;
    }

    private static string? OwnerOf(FileAnalysis a, AiExplanation? ai)
    {
        var top = a.Attributions.OrderByDescending(c => c.Confidence).FirstOrDefault();
        return top?.AppName ?? ai?.OwnerApp;
    }

    // 规则权威优先 → 已校验 AI → 按风险默认。
    private static string RecommendedActionOf(FileAnalysis a, AiExplanation? ai)
    {
        // 容器只浏览不处理: 给贴切动作, 而非"建议先备份"。
        if (a.Risk.IsContainer) return "展开按子目录查看，勿整体处理";
        if (!string.IsNullOrWhiteSpace(a.RuleMatch?.RecommendedAction))
            return a.RuleMatch!.RecommendedAction!;
        if (!string.IsNullOrWhiteSpace(ai?.RecommendedAction))
            return ai!.RecommendedAction!;
        return DefaultActionFor(a.Risk.Level);
    }

    // 已校验 AI 措辞 (推测) 优先; 否则: 系统/共享路径先给"用途"(SystemOrigin), 再附事实因素。
    private static string? ExplanationOf(FileAnalysis a, AiExplanation? ai)
    {
        if (!string.IsNullOrWhiteSpace(ai?.UserFriendlyExplanation))
            return ai!.UserFriendlyExplanation;

        var path = a.Node.RealPath ?? a.Node.Path;
        var factors = a.Risk.Factors.Count > 0 ? string.Join("; ", a.Risk.Factors) : null;
        // 用途优先级: 系统/共享路径用途 → 凭目录名可定的用途 (图片/.claude 等) → T3/特征库归属推出的用途 → 无。
        var purpose = Attribution.SystemOrigin.Resolve(path)?.Purpose
                      ?? Attribution.NameHeuristics.Resolve(path)?.Purpose
                      ?? DirectoryPurposeFromAttribution(a);
        if (purpose is null) return factors;
        return factors is null ? $"用途: {purpose}" : $"用途: {purpose}; {factors}";
    }

    // T3/特征库: 目录已归属某软件但路径/目录名表都没给出用途时, 据归属来源给一句诚实的用途
    // (区分"据二进制/已安装识别"的事实 与"据目录名推断", 守安全§9)。
    private static string? DirectoryPurposeFromAttribution(FileAnalysis a)
    {
        if (!a.Node.IsDirectory) return null;
        var top = a.Attributions.OrderByDescending(c => c.Confidence).FirstOrDefault();
        if (top is null) return null;
        return top.Source switch
        {
            null => $"{top.AppName} 的程序/数据目录（据目录内主程序或已安装信息识别）",
            "特征库" => $"{top.AppName} 相关目录（内置特征库按目录名推断）",
            "路径推断" => $"可能与 {top.AppName} 相关（据路径推断）",
            _ => $"可能与 {top.AppName} 相关（{top.Source}）",
        };
    }

    private static string DefaultActionFor(RiskLevel level) => level switch
    {
        RiskLevel.A => "通常可清理 (仍建议确认)",
        RiskLevel.B => "建议用官方方式清理 (命令/设置)",
        RiskLevel.C => "谨慎处理: 建议先备份或确认用途",
        RiskLevel.D => "不建议删除: 高风险, 见官方替代方案",
        RiskLevel.E => "无法判断, 不建议删除: 请进一步确认",
        _ => "不建议删除",
    };
}
