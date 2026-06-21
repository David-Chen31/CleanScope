using CleanScope.Core.Risk;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T1.8: RiskEngine —— 风险分级细则 §6 全样例对照 + §4 护栏 + §5 置信度 + fail-safe + SR-5。
public sealed class RiskEngineTests
{
    private static readonly IReadOnlyList<AttributionCandidate> NoAttr = Array.Empty<AttributionCandidate>();
    private static readonly RiskEngine Engine = new();

    private static FileNode Node(string path, bool isDir = false) =>
        new(0, 0, null, path, null, path.TrimEnd('\\').Split('\\')[^1], isDir, false, 1,
            null, null, null, AccessState.Accessible, null, default);

    private static Evidence Ev(long id = 1, bool fact = true, EvidenceKind kind = EvidenceKind.Metadata) =>
        new(id, 0, kind, "v", "src", fact, 0.9, default);

    // 每个文件至少一条观测证据 (SR-5 契约)。
    private static EvidenceBundle Bundle(params Evidence[] ev) =>
        new(0, null, ev.Length == 0 ? new[] { Ev() } : ev);

    private static RuleMatch Rm(RiskLevel risk, bool systemCritical = false, bool directDelete = false,
        double conf = 0.9, string id = "rule-x") =>
        new(0, 0, id, "cat", risk, directDelete, systemCritical, "action", conf, 100, true);

    private static RiskAssessment Assess(FileNode n, RuleMatch? rm, EvidenceBundle? b = null) =>
        Engine.Assess(n, b ?? Bundle(), rm, NoAttr);

    // —— §6 样例对照 ——

    [Theory]
    [InlineData(@"C:\Windows\Installer\{GUID}.msi")]
    [InlineData(@"C:\Windows\System32\drivers\xxx.sys")]
    [InlineData(@"C:\ProgramData\Package Cache\{GUID}")]
    public void System_critical_D_rule_yields_D(string path)
    {
        var r = Assess(Node(path), Rm(RiskLevel.D, systemCritical: true));
        Assert.Equal(RiskLevel.D, r.Level);
        Assert.False(r.CanDeleteDirectly);
        Assert.True(r.Score >= 61);                // 护栏: D ≥61
        Assert.NotEmpty(r.EvidenceChain);          // SR-5
    }

    [Fact]
    public void Temp_tmp_yields_A_and_allows_direct_delete()
    {
        var r = Assess(Node(@"C:\Users\me\AppData\Local\Temp\x.tmp"),
            Rm(RiskLevel.A, directDelete: true, conf: 0.85, id: "user-temp"));
        Assert.Equal(RiskLevel.A, r.Level);
        Assert.True(r.CanDeleteDirectly);
        Assert.InRange(r.Score, 0, 20);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache", "chrome-cache")]
    [InlineData(@"C:\Users\me\.gradle\caches", "gradle-caches")]
    public void Official_cleanup_cache_yields_B(string path, string ruleId)
    {
        var r = Assess(Node(path, isDir: true), Rm(RiskLevel.B, conf: 0.9, id: ruleId));
        Assert.Equal(RiskLevel.B, r.Level);
        Assert.False(r.CanDeleteDirectly);
    }

    [Fact]
    public void User_personal_data_without_rule_yields_C()
    {
        var r = Assess(Node(@"C:\Users\me\Documents\paper.docx"), null);
        Assert.Equal(RiskLevel.C, r.Level);
    }

    [Fact]
    public void Roaming_app_data_without_rule_yields_C()
    {
        var r = Assess(Node(@"C:\Users\me\AppData\Roaming\SomeApp", isDir: true), null);
        Assert.Equal(RiskLevel.C, r.Level);
    }

    // S4: AppData\Local 下的应用数据目录 → C (应用数据/配置), 不再落 E。
    [Fact]
    public void Local_appdata_app_dir_yields_C()
    {
        var r = Assess(Node(@"C:\Users\me\AppData\Local\a8f3kd9", isDir: true), null);
        Assert.Equal(RiskLevel.C, r.Level);
        Assert.NotEmpty(r.EvidenceChain);          // SR-5
    }

    // S4: 程序安装/共享数据目录 → C (通过卸载程序处理), 不再落 E。
    [Theory]
    [InlineData(@"C:\Program Files\Docker")]
    [InlineData(@"C:\Program Files (x86)\Lenovo\LegionZone")]
    [InlineData(@"C:\ProgramData\Lenovo")]
    public void Install_location_yields_C_not_E(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.Equal(RiskLevel.C, r.Level);
    }

    // 系统区 (Windows/回收站/恢复等) 内真正来源不明 → E (保守, 未知=可疑)。
    [Theory]
    [InlineData(@"C:\Windows\System32\a8f3kd9")]
    [InlineData(@"C:\Windows\weird\unknownthing")]
    [InlineData(@"C:\$Recycle.Bin\stale")]
    public void Truly_unknown_system_path_yields_E(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.Equal(RiskLevel.E, r.Level);
        Assert.NotEmpty(r.EvidenceChain);          // 即便 E 也有观测证据 (SR-5)
    }

    // 用户区 (数据盘/用户自建目录) 的三无项 → C「个人文件」, 不再一律 E
    // (个人资料不应整盘渲染成最高危; 仍非可清理、无直删入口, 安全不变)。
    [Theory]
    [InlineData(@"D:\randomstuff\a8f3kd9")]
    [InlineData(@"D:\吉林大学\学习\编译原理")]
    [InlineData(@"E:\我的项目\notes")]
    public void Unknown_user_area_yields_C_personal(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.Equal(RiskLevel.C, r.Level);
        Assert.False(r.CanDeleteDirectly);          // C 非可直删, 安全不变
        Assert.NotEmpty(r.EvidenceChain);
    }

    // S-B: 顶层容器目录 → IsContainer (仅浏览), 不再"无法判断"。
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\28170")]
    [InlineData(@"C:\Users\28170\AppData")]
    [InlineData(@"C:\Users\28170\AppData\Local")]
    [InlineData(@"C:\Users\28170\AppData\Roaming")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\ProgramData")]
    public void Top_level_containers_are_flagged_container(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.True(r.IsContainer);
        Assert.NotEqual(RiskLevel.E, r.Level);     // 不再"无法判断"
    }

    [Fact]
    public void Non_container_subdir_is_not_flagged_container()
    {
        // AppData\Local\<具体应用> 不是容器, 而是应用数据 (C)。
        var r = Assess(Node(@"C:\Users\28170\AppData\Local\Google", isDir: true), null);
        Assert.False(r.IsContainer);
    }

    // S-A: 有归因 (含路径推断) → C 并写明归属, 不再"无法判断"。
    [Fact]
    public void Attributed_path_yields_C_with_owner_not_E()
    {
        var attr = new[] { new AttributionCandidate(0, 0, "Rust / Cargo", 0.5, 1, Array.Empty<long>()) };
        var r = Engine.Assess(Node(@"D:\dev\.cargo", isDir: true), Bundle(), null, attr);
        Assert.Equal(RiskLevel.C, r.Level);                 // 非 E
        Assert.Contains(r.Factors, f => f.Contains("Rust / Cargo"));   // 写明归属
    }

    // S5: 无规则命中但目录名表明可重建缓存 → B (从 E 救出), 但绝不可直删。
    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache")]
    [InlineData(@"C:\Users\me\AppData\Local\Slack\GPUCache")]
    [InlineData(@"C:\Users\me\AppData\Roaming\Code\Code Cache")]
    [InlineData(@"C:\Users\me\AppData\Local\SomeApp\logs")]
    [InlineData(@"C:\Users\me\AppData\Local\SomeApp\Crashpad")]
    public void Cache_like_directory_without_rule_yields_B(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.Equal(RiskLevel.B, r.Level);
        Assert.False(r.CanDeleteDirectly);         // 启发式不授予直删权
    }

    // L1: 可重建的开发依赖/产物 (node_modules 等无歧义名) → B, 但文案诚实标注"需重新安装/构建"。
    [Theory]
    [InlineData(@"D:\proj\app\node_modules")]
    [InlineData(@"D:\proj\src\__pycache__")]
    [InlineData(@"D:\proj\.pytest_cache")]
    public void Dev_artifact_directory_yields_B_with_rebuild_note(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.Equal(RiskLevel.B, r.Level);
        Assert.False(r.CanDeleteDirectly);
        Assert.Contains(r.Factors, f => f.Contains("重新安装") || f.Contains("重新构建"));
    }

    // L1 反例: 故意不收 build/dist/bin 等常见词, 避免把用户自建同名文件夹误判可清理 (用户区 → C)。
    [Theory]
    [InlineData(@"D:\我的资料\build")]
    [InlineData(@"D:\photos\dist")]
    public void Ambiguous_names_are_not_treated_as_cleanable(string path)
    {
        var r = Assess(Node(path, isDir: true), null);
        Assert.NotEqual(RiskLevel.B, r.Level);     // 不被误标可清理
    }

    [Fact]
    public void Cache_heuristic_only_applies_to_directories_not_files()
    {
        // 名为 CacheManager.dll 的文件不应被误判为可清理缓存。
        var r = Assess(Node(@"C:\Program Files\App\CacheManager.dll", isDir: false), null);
        Assert.NotEqual(RiskLevel.B, r.Level);
    }

    [Fact]
    public void System_critical_rule_overrides_cache_name()
    {
        // 即使叫 Temp, 命中系统关键 D 规则仍恒 D (规则权威优先于启发式)。
        var r = Assess(Node(@"C:\Windows\Temp", isDir: true), Rm(RiskLevel.D, systemCritical: true));
        Assert.Equal(RiskLevel.D, r.Level);
    }

    [Fact]
    public void Wsl_vhdx_in_use_yields_D_via_rule()
    {
        var b = Bundle(Ev(1), Ev(2, kind: EvidenceKind.Process));   // 占用
        var r = Engine.Assess(Node(@"C:\Users\me\AppData\Local\Packages\Distro\LocalState\ext4.vhdx"),
            b, Rm(RiskLevel.D, systemCritical: false, id: "wsl-distro-vhdx"), NoAttr);
        Assert.Equal(RiskLevel.D, r.Level);
    }

    // —— 护栏 / 置信度 / fail-safe ——

    [Fact]
    public void Occupation_raises_B_rule_to_C()
    {
        var b = Bundle(Ev(1), Ev(2, kind: EvidenceKind.Process));
        var r = Engine.Assess(Node(@"C:\some\cache"), b, Rm(RiskLevel.B), NoAttr);
        Assert.Equal(RiskLevel.C, r.Level);        // 占用 floor C, 压过 B
    }

    [Fact]
    public void Low_confidence_tightens_B_to_C()
    {
        var r = Assess(Node(@"C:\some\thing"), Rm(RiskLevel.B, conf: 0.3));
        Assert.Equal(RiskLevel.C, r.Level);        // §5: <0.5 不得 A/B
    }

    [Fact]
    public void Low_confidence_tightens_A_to_C()
    {
        var r = Assess(Node(@"C:\some\thing"), Rm(RiskLevel.A, directDelete: true, conf: 0.4));
        Assert.Equal(RiskLevel.C, r.Level);
        Assert.False(r.CanDeleteDirectly);         // 收紧后不再可直删
    }

    [Fact]
    public void Exception_during_assessment_fails_safe_to_E()
    {
        // 传入 null 证据包 → 内部 NRE → 捕获为 E (IR-8, 绝不 fail-open)。
        var r = Engine.Assess(Node(@"C:\x"), null!, Rm(RiskLevel.A, directDelete: true), NoAttr);
        Assert.Equal(RiskLevel.E, r.Level);
        Assert.False(r.CanDeleteDirectly);
    }

    [Fact]
    public void Evidence_chain_references_fact_evidence_ids()
    {
        var b = Bundle(Ev(7, fact: true), Ev(8, fact: false, kind: EvidenceKind.AiInference));
        var r = Engine.Assess(Node(@"C:\Users\me\Documents\x"), b, null, NoAttr);
        Assert.Equal(new[] { 7L }, r.EvidenceChain);   // 仅事实证据, 不含 AI 推测
    }
}
