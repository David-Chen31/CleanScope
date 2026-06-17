using CleanScope.Application;
using CleanScope.Core.Attribution;
using CleanScope.Core.Decisions;
using CleanScope.Core.Evidences;
using CleanScope.Core.Risk;
using CleanScope.Core.Rules;
using CleanScope.Core.Scanning;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Infrastructure.Windows;
using CleanScope.Reporting;

namespace CleanScope.Application.Tests;

// T1.11: 端到端编排集成测试 —— 真实 引擎装配, 扫描临时目录树 → 决策 → 报告数据。
public sealed class ScanAndAnalyzeUseCaseTests : IDisposable
{
    private readonly string _root;

    public ScanAndAnalyzeUseCaseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanscope_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Cache"));
        Directory.CreateDirectory(Path.Combine(_root, "mystery"));
        File.WriteAllBytes(Path.Combine(_root, "Cache", "big.bin"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(_root, "mystery", "x.dat"), new byte[100]);
    }

    private ScanAndAnalyzeUseCase BuildUseCase()
    {
        // 规则: 把 <root>\Cache 标为 B (走官方方式)。其余无规则。
        var rules = new[]
        {
            new RuleDefinition("temp-cache", Path.Combine(_root, "Cache"), RuleMatchKind.PathPrefix,
                "缓存", RiskLevel.B, false, false, "测试缓存", "用官方命令清理", "path_rule", 0.9, 60),
        };
        var windows = new WindowsAccess();   // 真实系统访问 (只读)
        return new ScanAndAnalyzeUseCase(
            new ScanEngine(), new EvidenceCollector(windows), new RuleEngine(rules),
            new AttributionEngine(), new RiskEngine(), new DecisionService());
    }

    [Fact]
    public async Task End_to_end_produces_decisions_and_report()
    {
        var result = await BuildUseCase().ExecuteAsync(new ScanOptions(_root, 50, ScanMode.Normal));

        Assert.NotEmpty(result.Decisions);
        Assert.Equal(ScanStatus.Completed, result.Report.Task.Status);
        Assert.True(result.Report.Task.TotalSize >= 5000);     // 根聚合
        Assert.True(result.Report.Task.FileCount > 0);          // 流式计数

        // Cache 子树命中规则 → B
        var cache = result.Decisions.Single(d => d.Path == Path.Combine(_root, "Cache"));
        Assert.Equal(RiskLevel.B, cache.RiskLevel);
        Assert.Equal("用官方命令清理", cache.RecommendedAction);

        // 无规则命中的随机目录: 因临时根位于 %LocalAppData%\Temp 下, S4 归为 C (应用数据/安装区, 谨慎),
        // 不再是 E。纯"来源不明→E"在 RiskEngineTests.Truly_unknown_path_yields_E 覆盖。
        var mystery = result.Decisions.Single(d => d.Path == Path.Combine(_root, "mystery"));
        Assert.Equal(RiskLevel.C, mystery.RiskLevel);

        // 每项证据链非空 (SR-5)
        Assert.All(result.Decisions, d => Assert.NotEmpty(d.EvidenceChain));
    }

    [Fact]
    public async Task Report_renders_to_markdown_end_to_end()
    {
        var result = await BuildUseCase().ExecuteAsync(new ScanOptions(_root, 50, ScanMode.Normal));
        var md = new MarkdownReportExporter().BuildMarkdown(result.Report);

        Assert.Contains("# CleanScope 扫描报告", md);
        Assert.Contains("删除决策始终由你做出", md);
        Assert.Contains("风险统计", md);
    }

    [Fact] // T2.6: 接真实证据后, 被占用文件 → 风险 floor 抬到 ≥C (护栏保持有效)
    public async Task Occupied_file_is_raised_to_at_least_C_end_to_end()
    {
        var locked = Path.Combine(_root, "mystery", "held.bin");
        File.WriteAllBytes(locked, new byte[4000]);   // 够大, 进入 TopN
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await BuildUseCase().ExecuteAsync(new ScanOptions(_root, 50, ScanMode.Normal));
            var held = result.Decisions.Single(d => d.Path == locked);
            // 无规则命中本会是 E; 占用证据把它抬到 C (RiskEngine 护栏)。
            Assert.True(held.RiskLevel <= RiskLevel.D && held.RiskLevel >= RiskLevel.C,
                $"expected ≥C, got {held.RiskLevel}");
            Assert.Equal(RiskLevel.C, held.RiskLevel);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
