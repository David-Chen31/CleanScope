using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CleanScope.Reporting;

/// <summary>
/// CSV 报告导出 (S7, 实现 <see cref="IReportExporter"/>)。对齐 WizTree 的 CSV 习惯, 便于 Excel 透视:
/// 路径 / 聚合大小 / 真实占用(去重) / 风险 / 归属 / 类别 / 建议。仅本地写文件, 不删除任何东西。
/// </summary>
public sealed class CsvReportExporter : IReportExporter
{
    private static readonly Regex UserNameRx =
        new(@"(\\Users\\)[^\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly bool _sanitizePaths;
    public CsvReportExporter(bool sanitizePaths = false) => _sanitizePaths = sanitizePaths;

    public string Format => "csv";

    public async Task ExportAsync(ScanReport report, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // UTF-8 BOM: 便于 Excel 正确识别中文。
        await File.WriteAllTextAsync(outputPath, Build(report), new UTF8Encoding(true), ct);
    }

    public string Build(ScanReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("路径,聚合大小(字节),真实占用去重(字节),风险,归属,类别,建议");
        foreach (var i in report.Items.OrderByDescending(i => i.ExclusiveSize))
        {
            sb.Append(Q(P(i.Path))).Append(',')
              .Append(i.Size.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(i.ExclusiveSize.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Q(i.RiskLevel.ToString())).Append(',')
              .Append(Q(i.OwnerApp)).Append(',')
              .Append(Q(i.Category)).Append(',')
              .Append(Q(i.RecommendedAction)).Append('\n');
        }
        return sb.ToString();
    }

    private string P(string path) => _sanitizePaths ? UserNameRx.Replace(path, "$1%USER%") : path;

    // CSV 字段转义: 含逗号/引号/换行时用双引号包裹并转义内部引号。
    private static string Q(string? s)
    {
        s ??= "";
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
