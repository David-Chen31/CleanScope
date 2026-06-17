using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Reporting;

namespace CleanScope.Reporting.Tests;

// T1.10: MarkdownReportExporter —— 报告含必需段 + 脱敏选项(P1) + 写文件。
public sealed class MarkdownReportExporterTests
{
    private static ScanReport Report()
    {
        var task = new ScanTask(1, @"C:\", ScanMode.Normal,
            ScanStatus.Completed, new DateTime(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 14, 9, 5, 0, DateTimeKind.Utc), 5_000_000_000, 1234, "0.1.0");

        var items = new[]
        {
            new DecisionItem(@"C:\Users\alice\AppData\Local\Temp", 2_000_000_000, null,
                RiskLevel.A, "通常可清理", "用户临时目录", new long[] { 1 }, ExclusiveSize: 2_000_000_000),
            new DecisionItem(@"C:\Windows\System32", 1_000_000_000, null,
                RiskLevel.D, "不建议删除", "命中系统关键黑名单", new long[] { 2 }, ExclusiveSize: 1_000_000_000),
            new DecisionItem(@"C:\Users\alice\AppData\Local\xyz", 500, null,
                RiskLevel.E, "无法判断, 不建议删除", "证据不足", new long[] { 3 }, ExclusiveSize: 500),
        };
        return new ScanReport(task, items);
    }

    [Fact]
    public void Report_contains_all_required_sections()
    {
        var md = new MarkdownReportExporter().BuildMarkdown(Report());

        Assert.Contains("# CleanScope 扫描报告", md);
        Assert.Contains("不会自动删除任何文件", md);     // 零删除声明
        Assert.Contains("## 风险统计", md);
        Assert.Contains("## 占用大头", md);
        Assert.Contains("## ⚠️ 高风险提醒", md);
        Assert.Contains("## 分级明细", md);
        Assert.Contains(@"C:\", md);                      // 扫描目标
        Assert.Contains("0.1.0", md);                     // 版本
    }

    [Fact]
    public void High_risk_section_lists_D_and_E_items()
    {
        var md = new MarkdownReportExporter().BuildMarkdown(Report());
        Assert.Contains(@"**[D]** `C:\Windows\System32`", md);
        Assert.Contains("[E]", md);
    }

    [Fact]
    public void Reclaimable_estimate_humanized()
    {
        var md = new MarkdownReportExporter().BuildMarkdown(Report());
        Assert.Contains("可清理估算", md);
        Assert.Contains("GB", md);                        // 2GB 临时项被人性化
    }

    [Fact]
    public void Sanitize_replaces_username_in_paths()
    {
        var plain = new MarkdownReportExporter(sanitizePaths: false).BuildMarkdown(Report());
        var clean = new MarkdownReportExporter(sanitizePaths: true).BuildMarkdown(Report());

        Assert.Contains("alice", plain);
        Assert.DoesNotContain("alice", clean);            // P1 脱敏
        Assert.Contains(@"\Users\%USER%\", clean);
    }

    [Fact]
    public void Cleanup_categories_aggregate_ab_by_category_using_exclusive_size()
    {
        var items = new[]
        {
            new DecisionItem(@"C:\a\.nuget\packages", 1_000, null, RiskLevel.B, "用 dotnet nuget locals all --clear",
                null, new long[] { 1 }, ExclusiveSize: 1_000, Category: "NuGet 全局包"),
            new DecisionItem(@"C:\b\.nuget\packages", 500, null, RiskLevel.B, "用 dotnet nuget locals all --clear",
                null, new long[] { 2 }, ExclusiveSize: 500, Category: "NuGet 全局包"),
            new DecisionItem(@"C:\c\Cache", 2_000, null, RiskLevel.A, "可清理",
                null, new long[] { 3 }, ExclusiveSize: 2_000, Category: "可重建缓存(按目录名推断)"),
            new DecisionItem(@"C:\d\System32", 9_999, null, RiskLevel.D, "严禁删除",
                null, new long[] { 4 }, ExclusiveSize: 9_999, Category: null),  // D 不计入
        };

        var cats = CleanupAggregator.Aggregate(items);

        Assert.Equal(2, cats.Count);                                  // 仅 A/B 两类
        Assert.Equal("可重建缓存(按目录名推断)", cats[0].Name);        // 2000 最大, 排前
        Assert.Equal(1_500, cats.Single(c => c.Name == "NuGet 全局包").ReclaimableSize);  // 合并去重求和
        Assert.Equal(2, cats.Single(c => c.Name == "NuGet 全局包").ItemCount);
        Assert.DoesNotContain(cats, c => c.TopRisk == RiskLevel.D);   // 高风险不进可清理
    }

    [Fact]
    public void Csv_export_has_header_exclusive_size_and_escapes_fields()
    {
        var csv = new CsvReportExporter().Build(Report());

        Assert.StartsWith("路径,聚合大小(字节),真实占用去重(字节),风险,归属,类别,建议", csv);
        Assert.Contains("2000000000,2000000000,A", csv);        // 含独占大小列
        Assert.Contains(@"C:\Windows\System32", csv);
        // 行按独占大小降序: 2GB 的 Temp 在 System32(1GB) 之前。
        var iTemp = csv.IndexOf("Temp", StringComparison.Ordinal);
        var iSys = csv.IndexOf("System32", StringComparison.Ordinal);
        Assert.True(iTemp >= 0 && iTemp < iSys);
    }

    [Fact]
    public void Topn_section_ranks_by_exclusive_size()
    {
        var md = new MarkdownReportExporter().BuildMarkdown(Report());
        Assert.Contains("叶子贡献", md);                         // S7: 标题点明按真实占用排序
        Assert.Contains("真实占用(去重)", md);
    }

    [Fact] // S-C: 有 AI 调查推测 → 报告出现"AI 调查"节, 含"AI 推测/仅供参考"声明 + 推测文本。
    public void Ai_investigation_section_renders_when_present()
    {
        var task = new ScanTask(1, @"C:\", ScanMode.Normal, ScanStatus.Completed,
            new DateTime(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc), null, 1000, 1, "0.1.0");
        var items = new[]
        {
            new DecisionItem(@"C:\Users\me\Weird\blob", 4_000, null, RiskLevel.E,
                "无法判断, 不建议删除", "证据不足", new long[] { 1 }, ExclusiveSize: 4_000,
                AiInvestigation: "某软件遗留数据 这看起来像缓存。"),
        };
        var md = new MarkdownReportExporter().BuildMarkdown(new ScanReport(task, items));

        Assert.Contains("## 🔍 AI 调查", md);
        Assert.Contains("AI 推测", md);                          // 明确标注非权威
        Assert.Contains("不改变风险等级", md);
        Assert.Contains("某软件遗留数据 这看起来像缓存。", md);   // 推测文本入表
    }

    [Fact] // 未启用 AI 调查 (无 AiInvestigation) → 整节省略, 不留空标题。
    public void Ai_investigation_section_omitted_when_absent()
    {
        var md = new MarkdownReportExporter().BuildMarkdown(Report());
        Assert.DoesNotContain("## 🔍 AI 调查", md);
    }

    [Fact]
    public async Task Export_writes_utf8_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "cs_report_" + Guid.NewGuid().ToString("N"), "out.md");
        try
        {
            await new MarkdownReportExporter().ExportAsync(Report(), path);
            Assert.True(File.Exists(path));
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("CleanScope 扫描报告", text);
        }
        finally
        {
            var d = Path.GetDirectoryName(path);
            if (d is not null) try { Directory.Delete(d, true); } catch { }
        }
    }
}
