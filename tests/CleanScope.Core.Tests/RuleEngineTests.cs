using CleanScope.Core.Rules;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T1.7: RuleEngine —— 四种匹配 / 冲突就高 / 系统关键命中D / IR-4 真实路径。
// (单测对应安全门禁 T-02/T-03 的"类路径命中D"切片。)
public sealed class RuleEngineTests
{
    private static readonly EvidenceBundle Bundle = new(0, null, Array.Empty<Evidence>());

    // —— 构造器 ——
    private static RuleDefinition Rule(
        string id, string pattern, RuleMatchKind kind, RiskLevel risk, int priority,
        bool systemCritical = false, bool directDelete = false) =>
        new(id, pattern, kind, $"cat-{id}", risk, directDelete, systemCritical,
            "desc", "action", "path_rule", 0.9, priority);

    private static FileNode Node(string path, bool isDir = false, string? realPath = null) =>
        new(0, 0, null, path, realPath, NameOf(path), isDir, realPath is not null, 1,
            null, null, null, AccessState.Accessible, null, default);

    private static string NameOf(string p) => p.TrimEnd('\\').Split('\\')[^1];

    // 真实环境里规则已展开环境变量; 测试直接给展开后的字面路径。
    private static readonly RuleDefinition SystemRoot =
        Rule("win-system-root", @"C:\Windows", RuleMatchKind.PathPrefix, RiskLevel.D, 100, systemCritical: true);
    private static readonly RuleDefinition System32 =
        Rule("win-system32", @"C:\Windows\System32", RuleMatchKind.PathPrefix, RiskLevel.D, 100, systemCritical: true);
    private static readonly RuleDefinition InstallerCache =
        Rule("win-installer-cache", @"C:\Windows\Installer", RuleMatchKind.PathPrefix, RiskLevel.D, 100, systemCritical: true);
    private static readonly RuleDefinition LogExt =
        Rule("log-files", "*.log", RuleMatchKind.Extension, RiskLevel.B, 40);

    [Fact] // T-02 类: System32 下文件 → 权威 D + 系统关键
    public void System32_path_matches_authoritative_D_system_critical()
    {
        var engine = new RuleEngine(new[] { SystemRoot, System32 });
        var m = engine.Match(Node(@"C:\Windows\System32\drivers\nv.sys"), Bundle);

        Assert.NotNull(m);
        Assert.Equal(RiskLevel.D, m!.RiskLevel);
        Assert.True(m.IsSystemCritical);
        Assert.False(m.DirectDelete!.Value);
        Assert.True(m.Authoritative);
        Assert.Equal("win-system32", m.RuleId);   // 更具体(更长前缀)胜出
    }

    [Fact] // T-03 类: Installer\*.msi → 权威 D
    public void Installer_msi_matches_D()
    {
        var engine = new RuleEngine(new[] { InstallerCache });
        var m = engine.Match(Node(@"C:\Windows\Installer\1a2b.msi"), Bundle);
        Assert.Equal(RiskLevel.D, m!.RiskLevel);
        Assert.True(m.IsSystemCritical);
    }

    [Fact]
    public void No_rule_matches_returns_null()
    {
        var engine = new RuleEngine(new[] { System32, LogExt });
        Assert.Null(engine.Match(Node(@"C:\Users\me\notes.txt"), Bundle));
    }

    [Fact] // 冲突就高: 系统关键(100,D) 压过扩展名(40,B)
    public void System_critical_wins_over_extension_rule()
    {
        var engine = new RuleEngine(new[] { LogExt, System32 });
        var m = engine.Match(Node(@"C:\Windows\System32\config\setup.log"), Bundle);
        Assert.Equal(RiskLevel.D, m!.RiskLevel);
        Assert.True(m.IsSystemCritical);
    }

    [Fact] // 同 priority → risk 就高
    public void Same_priority_takes_higher_risk()
    {
        var low = Rule("low", @"C:\X", RuleMatchKind.PathPrefix, RiskLevel.B, 60);
        var high = Rule("high", @"C:\X\Y", RuleMatchKind.PathPrefix, RiskLevel.C, 60);
        var engine = new RuleEngine(new[] { low, high });
        var m = engine.Match(Node(@"C:\X\Y\f"), Bundle);
        Assert.Equal(RiskLevel.C, m!.RiskLevel);
        Assert.Equal("high", m.RuleId);
    }

    [Fact] // IR-4: 经 RealPath 命中黑名单 (Path 在别处也拦得住)
    public void Matches_on_real_path_when_reparse()
    {
        var engine = new RuleEngine(new[] { System32 });
        var node = Node(@"C:\Users\me\shortcut", isDir: true, realPath: @"C:\Windows\System32\evil");
        var m = engine.Match(node, Bundle);
        Assert.Equal(RiskLevel.D, m!.RiskLevel);
        Assert.True(m.IsSystemCritical);
    }

    [Fact]
    public void Prefix_respects_segment_boundary()
    {
        var engine = new RuleEngine(new[] { SystemRoot });           // C:\Windows
        Assert.Null(engine.Match(Node(@"C:\WindowsApps\app\x.exe"), Bundle));  // 不误配
        Assert.NotNull(engine.Match(Node(@"C:\Windows"), Bundle));            // 等于自身命中
    }

    [Fact]
    public void Glob_matches_segment_and_descendants()
    {
        var chrome = Rule("chrome-cache", @"C:\Users\me\AppData\Local\Google\Chrome\User Data\*\Cache",
            RuleMatchKind.PathGlob, RiskLevel.B, 60);
        var engine = new RuleEngine(new[] { chrome });

        Assert.NotNull(engine.Match(
            Node(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache", isDir: true), Bundle));
        Assert.NotNull(engine.Match(
            Node(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Profile 1\Cache\data_0"), Bundle));
        Assert.Null(engine.Match(
            Node(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\History"), Bundle));
    }

    [Fact]
    public void DirName_matches_any_segment_including_descendants()
    {
        var recycle = Rule("sys-recyclebin", "$Recycle.Bin", RuleMatchKind.DirName, RiskLevel.D, 100, systemCritical: true);
        var engine = new RuleEngine(new[] { recycle });
        Assert.NotNull(engine.Match(Node(@"C:\$Recycle.Bin", isDir: true), Bundle));
        Assert.NotNull(engine.Match(Node(@"C:\$Recycle.Bin\S-1-5\file"), Bundle));  // 后代
        Assert.Null(engine.Match(Node(@"C:\Users\me\Recyclething"), Bundle));
    }

    [Fact]
    public void Extension_matches_files_only()
    {
        var engine = new RuleEngine(new[] { LogExt });
        Assert.NotNull(engine.Match(Node(@"C:\app\trace.LOG"), Bundle));    // 大小写不敏感
        Assert.Null(engine.Match(Node(@"C:\app\logs", isDir: true), Bundle)); // 目录不按扩展名命中
    }

}
