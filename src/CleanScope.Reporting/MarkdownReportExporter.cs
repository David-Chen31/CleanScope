using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CleanScope.Reporting;

/// <summary>
/// Markdown 报告导出 (实现 <see cref="IReportExporter"/>)。产出: 扫描元信息 / 风险统计 /
/// TopN 占用大头 / 高风险提醒 / 分级明细。
///
/// 安全:
///  - 报告显著声明"删除决策由你做出; 应用内删除仅移入回收站 (可还原)"。
///  - <paramref name="sanitizePaths"/>=true 时对路径中的用户名做脱敏 (P1), 便于外发/分享;
///    默认 false (本地报告保留真实路径)。
/// </summary>
public sealed class MarkdownReportExporter : IReportExporter
{
    private static readonly Regex UserNameRx =
        new(@"(\\Users\\)[^\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly bool _sanitizePaths;

    public MarkdownReportExporter(bool sanitizePaths = false) => _sanitizePaths = sanitizePaths;

    public string Format => "markdown";

    public async Task ExportAsync(ScanReport report, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, BuildMarkdown(report), new UTF8Encoding(false), ct);
    }

    public string BuildMarkdown(ScanReport report)
    {
        var t = report.Task;
        var items = report.Items;
        var sb = new StringBuilder();

        sb.AppendLine("# CleanScope 扫描报告").AppendLine();
        sb.AppendLine($"- 扫描目标: `{P(t.TargetPath)}`");
        sb.AppendLine($"- 扫描时间: {Time(t.StartedAt)} ~ {(t.FinishedAt is { } f ? Time(f) : "进行中")}");
        sb.AppendLine($"- 应用版本: {t.AppVersion}　|　扫描模式: {t.Mode}");
        sb.AppendLine($"- 文件/目录数: {t.FileCount?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
        sb.AppendLine($"- 总占用: {Size(t.TotalSize ?? 0)}");
        sb.AppendLine();
        sb.AppendLine("> ⚠️ 本报告仅供参考。删除决策始终由你做出; 应用内删除仅移入回收站 (可还原)。");
        sb.AppendLine();

        AppendAiAdvice(sb, report.AiCleanupAdvice);
        AppendRiskSummary(sb, items);
        AppendSoftwareUsage(sb, items);
        AppendCleanupCategories(sb, items);
        AppendTopN(sb, items, 20);
        AppendAiInvestigation(sb, items);
        AppendHighRisk(sb, items);
        AppendDetails(sb, items);

        return sb.ToString();
    }

    // S3: 按可清理类别聚合 (A/B), 回答"哪类能省多少、怎么省"。
    private void AppendCleanupCategories(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        var cats = CleanupAggregator.Aggregate(items);
        sb.AppendLine("## 可清理类别 (A/B, 按类别聚合)").AppendLine();
        if (cats.Count == 0)
        {
            sb.AppendLine("_未发现可清理类别。_").AppendLine();
            return;
        }
        sb.AppendLine("| 类别 | 项数 | 可回收(去重) | 建议清理方式 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var c in cats)
            sb.AppendLine($"| {Cell(c.Name)} | {c.ItemCount} | {Size(c.ReclaimableSize)} | {Cell(c.RecommendedAction)} |");
        sb.AppendLine().AppendLine("> 以上仅给出每类可回收空间与官方清理方式; 删除由你决定, 应用内删除仅移入回收站 (可还原)。").AppendLine();
    }

    // S-H: 整盘 AI 参谋 (跨项建议)。AI 不进裁决, 仅给建议; 文本原样透传 (不解析/不执行, IR-6)。
    private static void AppendAiAdvice(StringBuilder sb, string? advice)
    {
        if (string.IsNullOrWhiteSpace(advice)) return;
        sb.AppendLine("## 🧭 AI 清理参谋 (跨项建议)").AppendLine();
        sb.AppendLine("> 以下为 **AI 建议, 仅供参考**, 不改变风险等级; 删除请用官方方式或应用内回收站, 并自行确认。");
        sb.AppendLine();
        sb.AppendLine(advice.Trim());
        sb.AppendLine();
    }

    // S-F: 按软件占用 —— 回答"空间被哪些软件占了, 各能清多少", 比按风险更贴近用户语言。
    private static void AppendSoftwareUsage(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        var soft = SoftwareAggregator.Aggregate(items);
        sb.AppendLine("## 按软件占用 (谁占了我的空间)").AppendLine();
        if (soft.Count == 0)
        {
            sb.AppendLine("_无可归并的软件项。_").AppendLine();
            return;
        }
        sb.AppendLine("| 软件/来源 | 项数 | 占用(去重) | 其中可清理(A/B) |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var s in soft.Take(30))
            sb.AppendLine($"| {Cell(s.Name)} | {s.ItemCount} | {Size(s.TotalSize)} | {Size(s.CleanableSize)} |");
        sb.AppendLine().AppendLine("> 占用为去重独占大小; “可清理”为该软件名下 A/B 项 (仍建议确认/官方方式)。").AppendLine();
    }

    private void AppendRiskSummary(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        sb.AppendLine("## 风险统计").AppendLine();
        sb.AppendLine("| 等级 | 数量 | 占用(去重) |");
        sb.AppendLine("|---|---|---|");
        // S-B: 容器目录单列一行, 不计入 A-E (它们只是浏览入口, 非删除对象)。
        var containers = items.Where(i => i.IsContainer).ToList();
        if (containers.Count > 0)
            sb.AppendLine($"| 🗂 容器(仅浏览) | {containers.Count} | {Size(containers.Sum(i => i.ExclusiveSize))} |");
        foreach (var level in new[] { RiskLevel.A, RiskLevel.B, RiskLevel.C, RiskLevel.D, RiskLevel.E })
        {
            var g = items.Where(i => !i.IsContainer && i.RiskLevel == level).ToList();
            // 用独占大小求和: 同一批字节不因父子目录同时入选而被重复计入。
            sb.AppendLine($"| {RiskLabel(level)} | {g.Count} | {Size(g.Sum(i => i.ExclusiveSize))} |");
        }
        var reclaimable = items.Where(i => !i.IsContainer && i.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(i => i.ExclusiveSize);
        sb.AppendLine().AppendLine($"可清理估算 (A+B, 去重, 仍建议确认): **{Size(reclaimable)}**");
        sb.AppendLine().AppendLine("> 占用为**去重独占大小**(每个字节只归属最深的被分析目录), 故各级之和不超过磁盘实际占用; TopN 仍按目录聚合大小展示。").AppendLine();
    }

    // S7: 按"叶子贡献"(独占大小) 排序, 而非聚合大小 —— 避免 C:\→Users→AppData 这类祖先链占满榜单,
    // 让真正占地方的叶子目录浮现。同时给出聚合大小作参考。
    private void AppendTopN(StringBuilder sb, IReadOnlyList<DecisionItem> items, int n)
    {
        sb.AppendLine($"## 占用大头 (前 {n}, 按真实占用/叶子贡献排序)").AppendLine();
        sb.AppendLine("| # | 路径 | 真实占用(去重) | 聚合大小 | 风险 | 建议 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        var top = items
            .OrderByDescending(i => i.ExclusiveSize)
            .ThenByDescending(i => i.Size)
            .Take(n).ToList();
        for (var i = 0; i < top.Count; i++)
            sb.AppendLine($"| {i + 1} | `{P(top[i].Path)}` | {Size(top[i].ExclusiveSize)} | {Size(top[i].Size)} | {BucketLabel(top[i])} | {Cell(top[i].RecommendedAction)} |");
        sb.AppendLine().AppendLine("> “真实占用”= 去重独占大小 (不含已单列的子目录); “聚合大小”= 含全部子孙。").AppendLine();
    }

    // S-C: AI 调查未知项。AI 不进裁决 (风险仍由本地引擎权威判定), 仅对"真正三无"未知项给推测,
    // 帮用户消化"无法判断"。明确标注"AI 推测, 仅供参考, 不改变风险等级"。
    private void AppendAiInvestigation(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        var investigated = items.Where(i => !string.IsNullOrWhiteSpace(i.AiInvestigation)).ToList();
        if (investigated.Count == 0) return;   // 未启用 AI 调查则整节省略

        sb.AppendLine("## 🔍 AI 调查 (未知项推测)").AppendLine();
        sb.AppendLine("> 以下为 **AI 推测, 仅供参考**, 不改变风险等级 (风险仍由本地规则/引擎权威判定)。AI 仅用于帮助理解「无法判断」的项。");
        sb.AppendLine();
        sb.AppendLine("| 路径 | 大小 | 风险 | AI 推测 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var i in investigated.OrderByDescending(i => i.ExclusiveSize))
            sb.AppendLine($"| `{P(i.Path)}` | {Size(i.Size)} | {RiskLabel(i.RiskLevel)} | {Cell(i.AiInvestigation)} |");
        sb.AppendLine();
    }

    private void AppendHighRisk(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        var high = items.Where(i => i.RiskLevel is RiskLevel.D or RiskLevel.E).ToList();
        sb.AppendLine("## ⚠️ 高风险提醒 (不建议删除)").AppendLine();
        if (high.Count == 0)
        {
            sb.AppendLine("_未发现 D/E 级高风险项。_").AppendLine();
            return;
        }
        foreach (var i in high)
            sb.AppendLine($"- **[{i.RiskLevel}]** `{P(i.Path)}` — {Cell(i.RecommendedAction)}");
        sb.AppendLine();
    }

    private void AppendDetails(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        sb.AppendLine("## 分级明细").AppendLine();
        sb.AppendLine("| 路径 | 大小 | 归属 | 风险 | 建议 | 说明 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var i in items)
            sb.AppendLine($"| `{P(i.Path)}` | {Size(i.Size)} | {Cell(i.Origin ?? i.OwnerApp)} | {RiskLabel(i.RiskLevel)} | {Cell(i.RecommendedAction)} | {Cell(i.Explanation)} |");
        sb.AppendLine();
    }

    private string P(string path) => _sanitizePaths ? UserNameRx.Replace(path, "$1%USER%") : path;

    private static string Time(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // 容器单独标识, 否则按风险等级。
    private static string BucketLabel(DecisionItem i) => i.IsContainer ? "🗂 容器" : RiskLabel(i.RiskLevel);

    private static string RiskLabel(RiskLevel l) => l switch
    {
        RiskLevel.A => "A 通常可清理",
        RiskLevel.B => "B 走官方方式",
        RiskLevel.C => "C 谨慎",
        RiskLevel.D => "D 高风险",
        RiskLevel.E => "E 无法判断",
        _ => l.ToString(),
    };

    // Markdown 表格单元: 去 null、转义竖线与换行, 防破表。
    private static string Cell(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "-" : s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string Size(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        double s = bytes;
        int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return i == 0
            ? $"{bytes} {u[i]}"
            : string.Create(CultureInfo.InvariantCulture, $"{s:0.##} {u[i]}");
    }
}
