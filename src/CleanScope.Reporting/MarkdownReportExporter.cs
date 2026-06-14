using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CleanScope.Reporting;

/// <summary>
/// Markdown 报告导出 (实现 <see cref="IReportExporter"/>)。产出: 扫描元信息 / 风险统计 /
/// TopN 占用大头 / 高风险提醒 / 分级明细。
///
/// 安全:
///  - 报告显著声明"不会自动删除任何文件"(MVP 零删除)。
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
        sb.AppendLine("> ⚠️ 本报告仅供参考。CleanScope **不会自动删除任何文件**, 删除决策始终由你做出。");
        sb.AppendLine();

        AppendRiskSummary(sb, items);
        AppendTopN(sb, items, 20);
        AppendHighRisk(sb, items);
        AppendDetails(sb, items);

        return sb.ToString();
    }

    private void AppendRiskSummary(StringBuilder sb, IReadOnlyList<DecisionItem> items)
    {
        sb.AppendLine("## 风险统计").AppendLine();
        sb.AppendLine("| 等级 | 数量 | 分析项占用 |");
        sb.AppendLine("|---|---|---|");
        foreach (var level in new[] { RiskLevel.A, RiskLevel.B, RiskLevel.C, RiskLevel.D, RiskLevel.E })
        {
            var g = items.Where(i => i.RiskLevel == level).ToList();
            sb.AppendLine($"| {RiskLabel(level)} | {g.Count} | {Size(g.Sum(i => i.Size))} |");
        }
        var reclaimable = items.Where(i => i.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(i => i.Size);
        sb.AppendLine().AppendLine($"可清理估算 (A+B, 仍建议确认): **{Size(reclaimable)}**").AppendLine();
    }

    private void AppendTopN(StringBuilder sb, IReadOnlyList<DecisionItem> items, int n)
    {
        sb.AppendLine($"## TopN 占用大头 (前 {n})").AppendLine();
        sb.AppendLine("| # | 路径 | 大小 | 风险 | 建议 |");
        sb.AppendLine("|---|---|---|---|---|");
        var top = items.OrderByDescending(i => i.Size).Take(n).ToList();
        for (var i = 0; i < top.Count; i++)
            sb.AppendLine($"| {i + 1} | `{P(top[i].Path)}` | {Size(top[i].Size)} | {RiskLabel(top[i].RiskLevel)} | {Cell(top[i].RecommendedAction)} |");
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
            sb.AppendLine($"| `{P(i.Path)}` | {Size(i.Size)} | {Cell(i.OwnerApp)} | {RiskLabel(i.RiskLevel)} | {Cell(i.RecommendedAction)} | {Cell(i.Explanation)} |");
        sb.AppendLine();
    }

    private string P(string path) => _sanitizePaths ? UserNameRx.Replace(path, "$1%USER%") : path;

    private static string Time(DateTime dt) => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
