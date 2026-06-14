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
                RiskLevel.A, "通常可清理", "用户临时目录", new long[] { 1 }),
            new DecisionItem(@"C:\Windows\System32", 1_000_000_000, null,
                RiskLevel.D, "不建议删除", "命中系统关键黑名单", new long[] { 2 }),
            new DecisionItem(@"C:\Users\alice\AppData\Local\xyz", 500, null,
                RiskLevel.E, "无法判断, 不建议删除", "证据不足", new long[] { 3 }),
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
        Assert.Contains("## TopN 占用大头", md);
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
