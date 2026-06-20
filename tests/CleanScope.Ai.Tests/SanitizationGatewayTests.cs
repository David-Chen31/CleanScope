using CleanScope.Ai.Sanitization;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Ai.Tests;

// T3.1: SanitizationGateway —— 出云载荷脱敏 (T-15/T-17, PR-1/3/4, IR-7)。
public sealed class SanitizationGatewayTests
{
    private static readonly SanitizationGateway Gw = new();

    private static FileAnalysis Analysis(string path, string name, bool isDir, params Evidence[] ev)
    {
        var node = new FileNode(0, 0, null, path, null, name, isDir, false, 12345,
            NodeType.Cache, null, null, AccessState.Accessible, null, default);
        var risk = new RiskAssessment(0, 0, RiskLevel.B, 30, new[] { "f" }, new[] { 1L }, false, 0.8, default);
        var rule = new RuleMatch(0, 0, "r", "浏览器缓存", RiskLevel.B, false, false, "用浏览器清理", 0.9, 60, true);
        return new FileAnalysis(node, new EvidenceBundle(0, null, ev), rule,
            Array.Empty<AttributionCandidate>(), risk, null);
    }

    private static Evidence Ev(string value, EvidenceKind kind = EvidenceKind.Metadata, bool fact = true) =>
        new(1, 0, kind, value, "src", fact, 0.9, default);

    [Fact] // T-15: 出站载荷不含用户名/文件名原文
    public void Payload_strips_username_and_filename()
    {
        var input = Gw.Sanitize(Analysis(
            @"C:\Users\张三\Documents\私密报告.docx", "私密报告.docx", isDir: false,
            Ev(@"C:\Users\张三\Documents\私密报告.docx")));

        Assert.DoesNotContain("张三", input.PathPattern);
        Assert.DoesNotContain("私密报告", input.PathPattern);
        Assert.Contains("%USER%", input.PathPattern);
        Assert.Contains("%FILE%", input.PathPattern);
        Assert.Equal(".docx", input.Extension);                 // 扩展名 P0 保留

        // 证据值也脱敏
        Assert.All(input.Facts, f =>
        {
            Assert.DoesNotContain("张三", f);
            Assert.DoesNotContain("私密报告", f);
        });
    }

    [Fact] // T-17: 只携带允许上云的最小必要字段
    public void Carries_only_allowed_pii_free_fields()
    {
        var input = Gw.Sanitize(Analysis(
            @"C:\Users\bob\AppData\Local\Google\Chrome\User Data\Default\Cache", "Cache", isDir: true,
            Ev("signed by Google LLC", EvidenceKind.Signature)));

        Assert.Equal("浏览器缓存", input.MatchedRuleCategory);   // P0
        Assert.Equal(RiskLevel.B, input.RuleRiskLevel);
        Assert.Equal(12345, input.Size);
        Assert.Null(input.Extension);                           // 目录无扩展名
        Assert.DoesNotContain("bob", input.PathPattern);
        Assert.Contains("Signature: signed by Google LLC", input.Facts); // 签名者 P0 原样
    }

    [Fact] // 问题#3 均衡档: 保留文件夹/文件名 (让 AI 认出软件), 仍抹用户名
    public void Balanced_keeps_leaf_name_but_strips_username()
    {
        var gw = new SanitizationGateway { Level = SanitizationLevel.Balanced };
        var input = gw.Sanitize(Analysis(
            @"C:\Users\张三\AppData\Local\Steam", "Steam", isDir: true,
            Ev(@"C:\Users\张三\AppData\Local\Steam")));

        Assert.Contains("Steam", input.PathPattern);              // 叶子名保留, AI 能识别
        Assert.DoesNotContain("张三", input.PathPattern);          // 用户名仍脱敏
        Assert.Contains("%USER%", input.PathPattern);
        Assert.DoesNotContain("%FILE%", input.PathPattern);       // 不再抹叶子名
    }

    [Fact] // 问题#3 关闭档: 发送真实路径 (识别最准, 用户知情选择)
    public void Off_sends_real_path()
    {
        var gw = new SanitizationGateway { Level = SanitizationLevel.Off };
        var input = gw.Sanitize(Analysis(
            @"C:\Users\张三\AppData\Local\Steam", "Steam", isDir: true,
            Ev(@"C:\Users\张三\AppData\Local\Steam")));

        Assert.Equal(@"C:\Users\张三\AppData\Local\Steam", input.PathPattern);
        Assert.Contains("张三", input.PathPattern);
    }

    [Fact] // 默认仍是严格档 (隐私优先, 行为不变)
    public void Default_is_strict()
        => Assert.Equal(SanitizationLevel.Strict, new SanitizationGateway().Level);

    [Fact] // 只放事实证据, AI 推测不外发
    public void Only_fact_evidence_included()
    {
        var input = Gw.Sanitize(Analysis(
            @"C:\x\y.dat", "y.dat", isDir: false,
            Ev("真事实", EvidenceKind.Metadata, fact: true),
            Ev("AI猜的", EvidenceKind.AiInference, fact: false)));

        Assert.Contains(input.Facts, f => f.Contains("真事实"));
        Assert.DoesNotContain(input.Facts, f => f.Contains("AI猜的"));
    }
}
