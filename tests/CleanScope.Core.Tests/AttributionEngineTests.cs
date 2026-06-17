using CleanScope.Core.Attribution;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T2.5: AttributionEngine —— 多证据融合候选归属 + 不臆造(AS-8)。
public sealed class AttributionEngineTests
{
    private static readonly AttributionEngine Engine = new();

    private static FileNode Node(string path = @"C:\x\y.exe") =>
        new(0, 0, null, path, null, "y.exe", false, false, 1,
            null, null, null, AccessState.Accessible, null, default);

    private static Evidence Ev(long id, EvidenceKind kind, string value) =>
        new(id, 0, kind, value, "src", IsFact: true, 0.9, default);

    private static EvidenceBundle Bundle(params Evidence[] ev) => new(0, null, ev);

    [Fact] // VS 样例: 命中已安装 VS → 得 VS 候选 (DoD)
    public void Installed_visual_studio_yields_vs_candidate()
    {
        var b = Bundle(
            Ev(1, EvidenceKind.Metadata, @"C:\Program Files\...\devenv.exe"),
            Ev(2, EvidenceKind.InstalledApp, "under installed app: Visual Studio"));

        var cands = Engine.Attribute(Node(), b, null);

        Assert.NotEmpty(cands);
        Assert.Contains("Visual Studio", cands[0].AppName);
        Assert.Equal(1, cands[0].Rank);
        Assert.Contains(2L, cands[0].SupportingEvidenceIds);
    }

    [Fact] // AS-8: 无可归属证据且路径无模式 → 空 (未知), 不臆造
    public void No_attributable_evidence_returns_empty()
    {
        var b = Bundle(
            Ev(1, EvidenceKind.Metadata, @"C:\some\random"),   // 仅观测路径, 无 product=
            Ev(2, EvidenceKind.Extension, ".dat"));
        Assert.Empty(Engine.Attribute(Node(), b, null));       // 默认路径 C:\x\y.exe 无模式
    }

    // S4: 无事实证据时, 从路径模式推断低置信归属 (填补"小文件夹无归属")。
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\Tencent\QQ", "腾讯系列 (QQ/微信等)")]
    [InlineData(@"C:\Users\me\AppData\Roaming\LarkShell\aha", "飞书 (Lark)")]
    [InlineData(@"C:\Users\me\AppData\Local\Notion\Cache", "Notion")]
    [InlineData(@"C:\Program Files (x86)\Lenovo\LegionZone", "Lenovo")]
    [InlineData(@"C:\Users\me\.cargo\registry", "Rust / Cargo")]
    [InlineData(@"C:\Users\me\AppData\Local\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\x", "Microsoft.WindowsTerminal")]
    public void Path_pattern_infers_owner_when_no_facts(string path, string expected)
    {
        var node = new FileNode(0, 0, null, path, null, "leaf", true, false, 1,
            null, null, null, AccessState.Accessible, null, default);
        var cands = Engine.Attribute(node, new EvidenceBundle(0, null, Array.Empty<Evidence>()), null);

        Assert.Single(cands);
        Assert.Equal(expected, cands[0].AppName);
        Assert.True(cands[0].Confidence < 0.8);                // 低置信: 不驱动风险, 仅展示
        Assert.Empty(cands[0].SupportingEvidenceIds);          // 路径推断, 无证据 id
    }

    [Fact] // 有事实证据时不启用路径推断 (事实优先, 不被稀释)
    public void Path_pattern_not_used_when_facts_exist()
    {
        var node = new FileNode(0, 0, null, @"C:\Program Files (x86)\Lenovo\X", null, "X", true, false, 1,
            null, null, null, AccessState.Accessible, null, default);
        var b = Bundle(Ev(1, EvidenceKind.InstalledApp, "under installed app: Legion Zone"));
        var cands = Engine.Attribute(node, b, null);
        Assert.Single(cands);
        Assert.Equal("Legion Zone", cands[0].AppName);         // 事实候选, 非路径段 "Lenovo"
    }

    [Fact] // 同名候选融合: 多证据增强置信度 + 合并支撑 id
    public void Same_app_from_two_evidences_is_fused()
    {
        var b = Bundle(
            Ev(1, EvidenceKind.InstalledApp, "under installed app: Visual Studio"),
            Ev(2, EvidenceKind.Metadata, "product=Visual Studio; company=Microsoft; version=17.0"));

        var c = Assert.Single(Engine.Attribute(Node(), b, null));
        Assert.Equal("Visual Studio", c.AppName);
        Assert.True(c.Confidence > 0.85);                       // OR 后高于单证据
        Assert.Equal(new[] { 1L, 2L }, c.SupportingEvidenceIds.OrderBy(x => x).ToArray());
    }

    [Fact] // 多候选按置信度降序排名
    public void Candidates_ranked_by_confidence_desc()
    {
        var b = Bundle(
            Ev(1, EvidenceKind.InstalledApp, "under installed app: AppStrong"),   // 0.85
            Ev(2, EvidenceKind.Signature, "signed by VendorWeak"));               // 0.50

        var cands = Engine.Attribute(Node(), b, null);
        Assert.Equal(2, cands.Count);
        Assert.Equal("AppStrong", cands[0].AppName);
        Assert.Equal("VendorWeak", cands[1].AppName);
        Assert.True(cands[0].Confidence > cands[1].Confidence);
        Assert.Equal(new[] { 1, 2 }, cands.Select(c => c.Rank!.Value).ToArray());
    }

    [Fact] // 签名者作为厂商候选
    public void Signer_becomes_vendor_candidate()
    {
        var b = Bundle(Ev(1, EvidenceKind.Signature, "signed by Microsoft Corporation"));
        var c = Assert.Single(Engine.Attribute(Node(), b, null));
        Assert.Equal("Microsoft Corporation", c.AppName);
    }

    [Fact] // 规则类别佐证 → 提升置信度
    public void Rule_category_corroborates_candidate()
    {
        var b = Bundle(Ev(1, EvidenceKind.Signature, "signed by Visual Studio"));
        var rule = new RuleMatch(0, 0, "vs-cache", "Visual Studio 缓存", RiskLevel.C, false, false, "a", 0.85, 70, true);

        var withRule = Assert.Single(Engine.Attribute(Node(), b, rule));
        var without = Assert.Single(Engine.Attribute(Node(), b, null));
        Assert.True(withRule.Confidence > without.Confidence);
    }

    [Fact] // 推测证据(IsFact=false)不参与归因
    public void Non_fact_evidence_is_ignored()
    {
        var ai = new Evidence(1, 0, EvidenceKind.AiInference, "under installed app: Guessed", "ai", IsFact: false, 0.3, default);
        Assert.Empty(Engine.Attribute(Node(), new EvidenceBundle(0, null, new[] { ai }), null));
    }
}
