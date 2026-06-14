using System.Globalization;
using CleanScope.Application;
using CleanScope.Core.Decisions;
using CleanScope.Core.Risk;
using CleanScope.Core.Rules;
using CleanScope.Core.Scanning;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Infrastructure.Rules;
using CleanScope.Reporting;

// CleanScope CLI 宿主 + 手写 DI 组合根 (T1.12, 最小闭环里程碑)。
// 用法: cleanscope scan <path> [--report out.md] [--top N] [--admin] [--sanitize] [--rules <dir>]
// 只读扫描, 绝不删除任何文件 (MVP 零删除)。

const string AppVersion = "0.1.0";

if (args.Length < 2 || !string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
{
    PrintUsage();
    return 2;
}

var target = args[1];
var opts = new Options(args);

try
{
    // —— 组合根: 装配裁决链各环 (经接口编排) ——
    var rulesDir = ResolveRulesDir(opts.Get("--rules"));
    Console.WriteLine($"CleanScope {AppVersion} —— 只读扫描, 不会删除任何文件。");
    Console.WriteLine($"规则目录: {rulesDir}");

    var rules = await new RulePackLoader(rulesDir).LoadAsync();
    Console.WriteLine($"已加载规则: {rules.Count} 条");

    var useCase = new ScanAndAnalyzeUseCase(
        new ScanEngine(),
        new RuleEngine(rules),
        new RiskEngine(),
        new DecisionService(),
        AppVersion);

    var mode = opts.Has("--admin") ? ScanMode.Admin : ScanMode.Normal;
    var top = opts.GetInt("--top", 100);
    var scanOptions = new ScanOptions(target, top, mode);

    Console.WriteLine($"开始扫描: {target} (TopN={top}, 模式={mode})");
    var progress = new ConsoleProgress();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await useCase.ExecuteAsync(scanOptions, progress);
    sw.Stop();
    progress.Done();

    PrintSummary(result, sw.Elapsed);

    var reportPath = opts.Get("--report");
    if (!string.IsNullOrWhiteSpace(reportPath))
    {
        var exporter = new MarkdownReportExporter(sanitizePaths: opts.Has("--sanitize"));
        await exporter.ExportAsync(result.Report, reportPath);
        Console.WriteLine($"\n报告已写入: {Path.GetFullPath(reportPath)}");
    }
    return 0;
}
catch (RulePackException ex)
{
    Console.Error.WriteLine($"[规则加载失败] {ex.Message}");
    return 3;
}
catch (DirectoryNotFoundException ex)
{
    Console.Error.WriteLine($"[路径错误] {ex.Message}");
    return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[未预期错误] {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("用法: cleanscope scan <path> [--report out.md] [--top N] [--admin] [--sanitize] [--rules <dir>]");
    Console.WriteLine("  只读扫描指定路径, 按风险分级并可导出 Markdown 报告。绝不删除文件。");
}

// 优先用输出目录旁的 rules/; 开发期回退到仓库根 (CleanScope.sln 旁) 的 rules/。
static string ResolveRulesDir(string? explicitDir)
{
    if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;
    var def = RulePackLoader.DefaultRulesDirectory;
    if (Directory.Exists(def)) return def;
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
    {
        var candidate = Path.Combine(d.FullName, "rules");
        if (File.Exists(Path.Combine(d.FullName, "CleanScope.sln")) && Directory.Exists(candidate))
            return candidate;
    }
    return def; // 交给 loader 抛清晰错误
}

static void PrintSummary(ScanAndAnalyzeResult result, TimeSpan elapsed)
{
    var t = result.Report.Task;
    var items = result.Decisions;
    Console.WriteLine($"\n扫描完成, 用时 {elapsed.TotalSeconds:0.0}s。");
    Console.WriteLine($"总占用(根聚合): {HumanSize(t.TotalSize ?? 0)}　文件/目录数: {t.FileCount}");

    Console.WriteLine("\n风险分级统计:");
    foreach (var level in new[] { RiskLevel.A, RiskLevel.B, RiskLevel.C, RiskLevel.D, RiskLevel.E })
    {
        var g = items.Where(i => i.RiskLevel == level).ToList();
        if (g.Count > 0)
            Console.WriteLine($"  {level}: {g.Count} 项, {HumanSize(g.Sum(i => i.Size))}");
    }
    var reclaimable = items.Where(i => i.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(i => i.Size);
    Console.WriteLine($"  可清理估算(A+B, 仍建议确认): {HumanSize(reclaimable)}");

    Console.WriteLine("\nTop 10 占用大头:");
    foreach (var (i, n) in items.OrderByDescending(i => i.Size).Take(10).Select((x, idx) => (x, idx + 1)))
        Console.WriteLine($"  {n,2}. [{i.RiskLevel}] {HumanSize(i.Size),10}  {i.Path}");

    var high = items.Where(i => i.RiskLevel is RiskLevel.D or RiskLevel.E).ToList();
    if (high.Count > 0)
        Console.WriteLine($"\n⚠️ {high.Count} 项为 D/E 级 —— 不建议删除。");
}

static string HumanSize(long bytes)
{
    string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
    double s = bytes;
    int i = 0;
    while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
    return i == 0
        ? $"{bytes} {u[i]}"
        : string.Create(CultureInfo.InvariantCulture, $"{s:0.##} {u[i]}");
}

// —— 小工具 ——

internal sealed class Options
{
    private readonly string[] _args;
    public Options(string[] args) => _args = args;

    public string? Get(string name)
    {
        var idx = Array.FindIndex(_args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 && idx + 1 < _args.Length ? _args[idx + 1] : null;
    }

    public bool Has(string name) =>
        Array.Exists(_args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    public int GetInt(string name, int fallback) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

// 节流的控制台进度 (单行刷新)。
internal sealed class ConsoleProgress : IProgress<ScanProgress>
{
    private DateTime _last = DateTime.MinValue;

    public void Report(ScanProgress value)
    {
        var now = DateTime.UtcNow;
        if ((now - _last).TotalMilliseconds < 200) return;
        _last = now;
        Console.Write($"\r  扫描中... {value.FilesScanned} 项, {value.CurrentPath}".PadRight(Math.Min(Console.IsOutputRedirected ? 120 : Console.WindowWidth - 1, 120)));
    }

    public void Done() => Console.Write("\r".PadRight(Console.IsOutputRedirected ? 1 : 0) + "\r");
}
