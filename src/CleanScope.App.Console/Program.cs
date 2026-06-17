using System.Globalization;
using CleanScope.Ai.Advice;
using CleanScope.Ai.Chat;
using CleanScope.Ai.Explanation;
using CleanScope.Ai.Sanitization;
using CleanScope.Ai.Validation;
using CleanScope.Application;
using CleanScope.Core.Attribution;
using CleanScope.Core.Decisions;
using CleanScope.Core.Evidences;
using CleanScope.Core.Risk;
using CleanScope.Core.Rules;
using CleanScope.Core.Scanning;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Infrastructure.Rules;
using CleanScope.Infrastructure.Windows;
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

    var windows = new WindowsAccess();   // 真实系统访问 (只读): 元数据/签名/已安装/占用

    // AI 旁路 (可选, --ai 开启): 脱敏→解释→校验。未开启或未配置 → 无 AI, 纯规则/风险。
    ISanitizationGateway? sanitizer = null;
    IExplanationService? explanation = null;
    IAiOutputValidator? validator = null;
    ICleanupAdvisor? advisor = null;
    if (opts.Has("--ai") || opts.Has("--ai-all"))
    {
        var aiOptions = AiOptions.Load(ResolveAiConfig());
        if (aiOptions.IsUsable)
        {
            var chat = new OpenAiChatClient(Http.Shared, aiOptions);
            sanitizer = new SanitizationGateway();
            explanation = new ExplanationService(chat);
            validator = new AiOutputValidator();
            advisor = new CleanupAdvisor(chat);   // S-H: 整盘参谋复用同一 chat
            Console.WriteLine($"AI 解释: 已启用 (模型 {aiOptions.Model}, 脱敏后出云)");
        }
        else
        {
            Console.WriteLine("AI 解释: 已请求 --ai 但未配置可用密钥, 跳过 (纯本地规则/风险)。");
        }
    }

    var useCase = new ScanAndAnalyzeUseCase(
        new ScanEngine(),
        new EvidenceCollector(windows),
        new RuleEngine(rules),
        new AttributionEngine(),
        new RiskEngine(),
        new DecisionService(),
        AppVersion,
        sanitizer, explanation, validator);

    var mode = opts.Has("--admin") ? ScanMode.Admin : ScanMode.Normal;
    var top = opts.GetInt("--top", 100);
    var scanOptions = new ScanOptions(target, top, mode);

    Console.WriteLine($"开始扫描: {target} (TopN={top}, 模式={mode})");
    var progress = new ConsoleProgress();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // S-C: --ai 时仅对"真正三无"未知项 (E) 并发+缓存跑 AI 调查 (数量可控, 不拖慢扫描);
    // --ai-all 才对全部项跑 (最贵)。否则纯本地, 扫描秒级。AI 仅旁路, 不改判风险。
    var aiMode = opts.Has("--ai-all") ? AiMode.Batch
        : opts.Has("--ai") ? AiMode.InvestigateUnknowns
        : AiMode.OnDemand;
    var result = await useCase.ExecuteAsync(scanOptions, progress, default, aiMode);
    sw.Stop();
    progress.Done();

    PrintSummary(result, sw.Elapsed);

    // S-H: 整盘 AI 参谋 (脱敏聚合 → 跨项建议)。失败/未启用 → 跳过。
    var report = result.Report;
    if (advisor is { Enabled: true })
    {
        var advice = await advisor.AdviseAsync(CleanupSummaryBuilder.From(result.Decisions));
        if (!string.IsNullOrWhiteSpace(advice))
        {
            report = report with { AiCleanupAdvice = advice };
            Console.WriteLine("\n🧭 AI 清理参谋 (跨项建议, 仅供参考):");
            Console.WriteLine(advice.Trim());
        }
    }

    var reportPath = opts.Get("--report");
    if (!string.IsNullOrWhiteSpace(reportPath))
    {
        var sanitize = opts.Has("--sanitize");
        // 按扩展名选格式: .csv → CSV (便于 Excel 透视), 否则 Markdown。
        IReportExporter exporter = reportPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? new CsvReportExporter(sanitize)
            : new MarkdownReportExporter(sanitize);
        await exporter.ExportAsync(report, reportPath);
        Console.WriteLine($"\n报告已写入 ({exporter.Format}): {Path.GetFullPath(reportPath)}");
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
    Console.WriteLine("用法: cleanscope scan <path> [--report out.md] [--top N] [--admin] [--sanitize] [--ai|--ai-all] [--rules <dir>]");
    Console.WriteLine("  只读扫描指定路径, 按风险分级并可导出 Markdown 报告。绝不删除文件。");
    Console.WriteLine("  --ai: 仅对未知项 (E) 跑 AI 调查, 推测写入报告 (脱敏后出云, 需 appsettings.ai.local.json 或环境变量); AI 仅建议, 不改判风险。");
    Console.WriteLine("  --ai-all: 对全部项跑 AI 解释 (最贵, 一般不需要)。");
}

// 定位 AI 配置 (appsettings.ai.local.json, 已 gitignore): 输出目录旁或仓库根。
static string? ResolveAiConfig()
{
    const string name = "appsettings.ai.local.json";
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
    {
        var candidate = Path.Combine(d.FullName, name);
        if (File.Exists(candidate)) return candidate;
    }
    return null;
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

    Console.WriteLine("\n风险分级统计 (占用为去重独占大小, 不重复计入父子目录):");
    foreach (var level in new[] { RiskLevel.A, RiskLevel.B, RiskLevel.C, RiskLevel.D, RiskLevel.E })
    {
        var g = items.Where(i => i.RiskLevel == level).ToList();
        if (g.Count > 0)
            Console.WriteLine($"  {level}: {g.Count} 项, {HumanSize(g.Sum(i => i.ExclusiveSize))}");
    }
    var reclaimable = items.Where(i => i.RiskLevel is RiskLevel.A or RiskLevel.B).Sum(i => i.ExclusiveSize);
    Console.WriteLine($"  可清理估算(A+B, 去重, 仍建议确认): {HumanSize(reclaimable)}");

    Console.WriteLine("\nTop 10 占用大头 (按真实占用/叶子贡献):");
    foreach (var (i, n) in items.OrderByDescending(i => i.ExclusiveSize).ThenByDescending(i => i.Size)
                                .Take(10).Select((x, idx) => (x, idx + 1)))
        Console.WriteLine($"  {n,2}. [{i.RiskLevel}] {HumanSize(i.ExclusiveSize),10}  {i.Path}");

    var cats = CleanupAggregator.Aggregate(items);
    if (cats.Count > 0)
    {
        Console.WriteLine("\n可清理类别 (A/B, 去重可回收):");
        foreach (var c in cats.Take(10))
            Console.WriteLine($"  [{c.TopRisk}] {HumanSize(c.ReclaimableSize),10}  {c.Name} ({c.ItemCount} 项) — {c.RecommendedAction}");
    }

    var soft = SoftwareAggregator.Aggregate(items);
    if (soft.Count > 0)
    {
        Console.WriteLine("\n按软件占用 (谁占了我的空间, 去重):");
        foreach (var s in soft.Take(10))
            Console.WriteLine($"  {HumanSize(s.TotalSize),10}  {s.Name} ({s.ItemCount} 项, 可清理 {HumanSize(s.CleanableSize)})");
    }

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

internal static class Http
{
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };
}

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
