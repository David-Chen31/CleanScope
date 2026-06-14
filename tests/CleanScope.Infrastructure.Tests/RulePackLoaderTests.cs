using CleanScope.Domain.Enums;
using CleanScope.Infrastructure.Rules;

namespace CleanScope.Infrastructure.Tests;

// T1.5: RulePackLoader —— 载入/排序/占位符展开 + 非法输入"报错不崩"(受控异常)。
public sealed class RulePackLoaderTests : IDisposable
{
    private readonly string _dir;

    public RulePackLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cleanscope_rules_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private void WriteRules(string fileName, string json) =>
        File.WriteAllText(Path.Combine(_dir, fileName), json);

    // 测试用展开器: 跨平台确定, 不依赖真实系统环境变量。
    private static string Expand(string s) => s.Replace("%TESTROOT%", @"C:\W");

    private RulePackLoader Loader() => new(_dir, Expand);

    [Fact]
    public async Task Loads_sorts_by_priority_desc_and_expands_vars()
    {
        WriteRules("50-cache.json", """
            [ { "id":"app-cache", "pattern":"%TESTROOT%\\Cache", "match_type":"path_prefix",
                "category":"Cache", "risk_level":"B", "direct_delete":true, "is_system_critical":false,
                "description":"d", "recommended_action":"清缓存", "evidence_type":"path_rule",
                "confidence":0.9, "priority":50 } ]
            """);
        WriteRules("00-system.json", """
            [ { "id":"win-system32", "pattern":"%TESTROOT%\\System32", "match_type":"path_prefix",
                "category":"System", "risk_level":"D", "direct_delete":false, "is_system_critical":true,
                "description":"核心", "recommended_action":"勿动", "evidence_type":"path_rule",
                "confidence":0.99, "priority":100 } ]
            """);

        var rules = await Loader().LoadAsync();

        Assert.Equal(2, rules.Count);
        Assert.Equal("win-system32", rules[0].Id);          // priority 100 在前 (就高)
        Assert.Equal(100, rules[0].Priority);
        Assert.Equal(@"C:\W\System32", rules[0].Pattern);   // 占位符展开
        Assert.Equal(RuleMatchKind.PathPrefix, rules[0].MatchKind);
        Assert.Equal(RiskLevel.D, rules[0].RiskLevel);
        Assert.True(rules[0].IsSystemCritical);
        Assert.Equal("app-cache", rules[1].Id);
    }

    [Theory]
    [InlineData("path_prefix", RuleMatchKind.PathPrefix)]
    [InlineData("path_glob", RuleMatchKind.PathGlob)]
    [InlineData("dir_name", RuleMatchKind.DirName)]
    [InlineData("extension", RuleMatchKind.Extension)]
    public async Task Maps_all_match_kinds(string raw, RuleMatchKind expected)
    {
        WriteRules("r.json", $$"""
            [ { "id":"x", "pattern":"p", "match_type":"{{raw}}", "category":"c", "risk_level":"C",
                "direct_delete":false, "is_system_critical":false, "description":"d",
                "recommended_action":"r", "evidence_type":"path_rule", "confidence":0.5, "priority":10 } ]
            """);
        var rules = await Loader().LoadAsync();
        Assert.Equal(expected, Assert.Single(rules).MatchKind);
    }

    [Fact]
    public async Task Empty_directory_yields_empty_list()
    {
        var rules = await Loader().LoadAsync();
        Assert.Empty(rules);
    }

    [Fact]
    public async Task Invalid_json_throws_named_exception_not_crash()
    {
        WriteRules("broken.json", "[ { not valid json ");
        var ex = await Assert.ThrowsAsync<RulePackException>(() => Loader().LoadAsync());
        Assert.Equal("broken.json", ex.File);
    }

    [Fact]
    public async Task System_critical_rule_that_relaxes_safety_is_rejected()
    {
        // is_system_critical=true 却 direct_delete=true → 放宽黑名单, 必须拒绝。
        WriteRules("bad.json", """
            [ { "id":"weak", "pattern":"p", "match_type":"path_prefix", "category":"c", "risk_level":"D",
                "direct_delete":true, "is_system_critical":true, "description":"d",
                "recommended_action":"r", "evidence_type":"path_rule", "confidence":0.9, "priority":100 } ]
            """);
        var ex = await Assert.ThrowsAsync<RulePackException>(() => Loader().LoadAsync());
        Assert.Contains("放宽", ex.Message);
    }

    [Fact]
    public async Task Unknown_match_type_is_rejected()
    {
        WriteRules("u.json", """
            [ { "id":"x", "pattern":"p", "match_type":"regex_evil", "category":"c", "risk_level":"C",
                "direct_delete":false, "is_system_critical":false, "description":"d",
                "recommended_action":"r", "evidence_type":"path_rule", "confidence":0.5, "priority":10 } ]
            """);
        await Assert.ThrowsAsync<RulePackException>(() => Loader().LoadAsync());
    }

    [Fact]
    public async Task Duplicate_id_is_rejected()
    {
        var rule = """
            { "id":"dup", "pattern":"p", "match_type":"path_prefix", "category":"c", "risk_level":"C",
              "direct_delete":false, "is_system_critical":false, "description":"d",
              "recommended_action":"r", "evidence_type":"path_rule", "confidence":0.5, "priority":10 }
            """;
        WriteRules("a.json", $"[ {rule} ]");
        WriteRules("b.json", $"[ {rule} ]");
        var ex = await Assert.ThrowsAsync<RulePackException>(() => Loader().LoadAsync());
        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public async Task Missing_directory_throws()
    {
        var loader = new RulePackLoader(Path.Combine(_dir, "nope"), Expand);
        await Assert.ThrowsAsync<RulePackException>(() => loader.LoadAsync());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
