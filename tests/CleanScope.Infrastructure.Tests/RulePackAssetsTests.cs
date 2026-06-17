using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Infrastructure.Rules;

namespace CleanScope.Infrastructure.Tests;

// T1.6: 校验仓库真实 rules/*.json 资产 —— 能被 RulePackLoader 加载、schema 与不变量成立。
// 用默认环境变量展开器 (本测试项目为 net8.0-windows, %SystemRoot% 等可用)。
public sealed class RulePackAssetsTests
{
    private const int ExpectedFiles = 12;   // 知识库 §1: 11 包 + 33-dev-cache (S3 可清理类别扩充)
    private const int ExpectedRules = 59;   // 12 包合计 59 条 (新增 7 条开发缓存)
    private const int ExpectedSystemCritical = 17; // 00-system-critical 16 + 10-installer:package-cache 1

    private static string RulesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CleanScope.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "rules");
    }

    private static async Task<IReadOnlyList<RuleDefinition>> LoadRealRulesAsync()
        => await new RulePackLoader(RulesDir()).LoadAsync();

    [Fact]
    public void Has_eleven_rule_pack_files()
        => Assert.Equal(ExpectedFiles, Directory.GetFiles(RulesDir(), "*.json").Length);

    [Fact]
    public async Task All_rules_load_without_error_and_count_matches()
    {
        var rules = await LoadRealRulesAsync();
        Assert.Equal(ExpectedRules, rules.Count);
    }

    [Fact]
    public async Task Rules_are_sorted_by_priority_descending()
    {
        var rules = await LoadRealRulesAsync();
        Assert.Equal(100, rules[0].Priority);                 // 系统关键带在前
        Assert.True(rules.Select(r => r.Priority).SequenceEqual(
            rules.Select(r => r.Priority).OrderByDescending(p => p)));
    }

    [Fact]
    public async Task System_critical_rules_are_locked_to_D_and_no_direct_delete()
    {
        var rules = await LoadRealRulesAsync();
        var critical = rules.Where(r => r.IsSystemCritical).ToList();

        Assert.Equal(ExpectedSystemCritical, critical.Count);
        Assert.All(critical, r =>
        {
            Assert.Equal(RiskLevel.D, r.RiskLevel);           // 黑名单恒 D
            Assert.False(r.DirectDelete);                     // 永不直删
            Assert.Equal(100, r.Priority);                    // 最高优先级
        });
    }

    [Fact]
    public async Task Direct_delete_is_allowed_only_for_A_level()
    {
        var rules = await LoadRealRulesAsync();
        // 仅 A 级少数 (user-temp / thumbcache) 允许直删 (知识库§4)。
        Assert.All(rules.Where(r => r.DirectDelete), r => Assert.Equal(RiskLevel.A, r.RiskLevel));
        Assert.Equal(
            new[] { "thumbcache", "user-temp" },
            rules.Where(r => r.DirectDelete).Select(r => r.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Env_var_patterns_are_expanded_to_real_paths()
    {
        var rules = await LoadRealRulesAsync();
        var sys32 = rules.Single(r => r.Id == "win-system32");

        Assert.DoesNotContain('%', sys32.Pattern);            // %SystemRoot% 已展开
        Assert.EndsWith(@"\System32", sys32.Pattern, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(":", sys32.Pattern);                  // 含真实盘符 (如 C:\Windows\System32)
    }

    [Fact]
    public async Task Glob_patterns_without_env_vars_are_left_intact()
    {
        var rules = await LoadRealRulesAsync();
        Assert.Equal(@"*\Steam\steamapps", rules.Single(r => r.Id == "steam-apps").Pattern);
    }
}
