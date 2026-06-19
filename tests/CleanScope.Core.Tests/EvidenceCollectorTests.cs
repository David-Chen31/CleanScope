using CleanScope.Core.Evidences;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T2.4: EvidenceCollector —— 汇集真实信号为证据集, 全部标 is_fact=true (§9), 经 IWindowsAccess 抽象。
public sealed class EvidenceCollectorTests
{
    private static FileNode Node(string path, bool isDir = false) =>
        new(0, 0, null, path, null, path.Split('\\')[^1], isDir, false, 1,
            null, null, null, AccessState.Accessible, null, default);

    [Fact]
    public async Task Always_emits_base_observation_and_all_facts()
    {
        var c = new EvidenceCollector(new FakeWin());
        var b = await c.CollectAsync(Node(@"C:\some\unknown.dat"));

        Assert.NotEmpty(b.Evidences);                              // SR-5
        Assert.All(b.Evidences, e => Assert.True(e.IsFact));       // §9: 只产事实
        Assert.DoesNotContain(b.Evidences, e => e.Kind == EvidenceKind.AiInference);
    }

    [Fact]
    public async Task Signed_file_yields_signature_evidence()
    {
        var win = new FakeWin
        {
            Metadata = Meta(signer: "Microsoft Corporation", signed: true, company: "Microsoft", version: "10.0"),
        };
        var b = await new EvidenceCollector(win).CollectAsync(Node(@"C:\app\tool.exe"));

        var sig = Assert.Single(b.Evidences, e => e.Kind == EvidenceKind.Signature);
        Assert.Contains("Microsoft Corporation", sig.Value);
        Assert.Contains(b.Evidences, e => e.Kind == EvidenceKind.Metadata && e.Value.Contains("company=Microsoft"));
        Assert.Contains(b.Evidences, e => e.Kind == EvidenceKind.Extension && e.Value == ".exe");
    }

    [Fact]
    public async Task Occupied_file_yields_process_evidence_and_metadata_flag()
    {
        var win = new FakeWin { Occupier = "devenv" };
        var b = await new EvidenceCollector(win).CollectAsync(Node(@"C:\proj\locked.db"));

        var proc = Assert.Single(b.Evidences, e => e.Kind == EvidenceKind.Process);
        Assert.Contains("devenv", proc.Value);
        Assert.NotNull(b.Metadata);
        Assert.True(b.Metadata!.InUse);
        Assert.Equal("devenv", b.Metadata.OccupyingProcess);
    }

    [Fact]
    public async Task File_under_installed_app_yields_attribution_evidence()
    {
        var win = new FakeWin
        {
            Apps = new[] { new InstalledApp("Visual Studio", "Microsoft", @"C:\Program Files\Microsoft Visual Studio", "Registry") },
        };
        var b = await new EvidenceCollector(win).CollectAsync(
            Node(@"C:\Program Files\Microsoft Visual Studio\Common7\ide.exe"));

        var attr = Assert.Single(b.Evidences, e => e.Kind == EvidenceKind.InstalledApp);
        Assert.Contains("Visual Studio", attr.Value);
    }

    [Fact]
    public async Task Most_specific_install_location_wins()
    {
        var win = new FakeWin
        {
            Apps = new[]
            {
                new InstalledApp("Outer", null, @"C:\Apps", "Registry"),
                new InstalledApp("Inner", null, @"C:\Apps\Foo", "Registry"),
            },
        };
        var b = await new EvidenceCollector(win).CollectAsync(Node(@"C:\Apps\Foo\bin\x.exe"));
        var attr = Assert.Single(b.Evidences, e => e.Kind == EvidenceKind.InstalledApp);
        Assert.Contains("Inner", attr.Value);
    }

    [Fact]
    public async Task Directory_has_observation_but_no_extension_or_signature()
    {
        var b = await new EvidenceCollector(new FakeWin()).CollectAsync(Node(@"C:\some\dir", isDir: true));

        Assert.Contains(b.Evidences, e => e.Kind == EvidenceKind.Metadata);   // 观测事实
        Assert.DoesNotContain(b.Evidences, e => e.Kind == EvidenceKind.Extension);
        Assert.DoesNotContain(b.Evidences, e => e.Kind == EvidenceKind.Signature);
        Assert.Null(b.Metadata);
    }

    [Fact]
    public async Task Unreadable_metadata_does_not_crash()
    {
        var win = new FakeWin { Metadata = null };   // ReadMetadata 返回 null
        var b = await new EvidenceCollector(win).CollectAsync(Node(@"C:\x\y.bin"));
        Assert.NotEmpty(b.Evidences);
        Assert.All(b.Evidences, e => Assert.True(e.IsFact));
    }

    [Fact] // E1: AppData 数据目录的段名匹配已安装应用 → 归属 (即便不在安装目录下)。
    public async Task AppData_dir_matching_installed_app_name_yields_attribution()
    {
        var win = new FakeWin
        {
            Apps = new[] { new InstalledApp("Postman x86_64 11.19.0", "Postman Inc", null, "Registry") },
        };
        var b = await new EvidenceCollector(win).CollectAsync(
            Node(@"C:\Users\me\AppData\Local\Postman\app-11.19.0\resources\x.bin"));

        var attr = Assert.Single(b.Evidences, e => e.Kind == EvidenceKind.InstalledApp);
        Assert.Contains("Postman", attr.Value);
    }

    [Fact] // Roaming\<App> + Programs\<App> 同样可归属。
    public async Task Roaming_and_programs_dir_match()
    {
        var win = new FakeWin { Apps = new[] { new InstalledApp("Notion", null, null, "Registry") } };
        var roaming = await new EvidenceCollector(win).CollectAsync(Node(@"C:\Users\me\AppData\Roaming\Notion\cache\f.dat"));
        Assert.Contains(roaming.Evidences, e => e.Kind == EvidenceKind.InstalledApp && e.Value.Contains("Notion"));

        var prog = await new EvidenceCollector(win).CollectAsync(Node(@"C:\Users\me\AppData\Local\Programs\Notion\app.bin"));
        Assert.Contains(prog.Evidences, e => e.Kind == EvidenceKind.InstalledApp && e.Value.Contains("Notion"));
    }

    [Fact] // 噪声段 (Temp / Microsoft) 不产生误归属。
    public async Task Noise_segments_do_not_attribute()
    {
        var win = new FakeWin { Apps = new[] { new InstalledApp("Temp Cleaner", null, null, "Registry") } };
        var b = await new EvidenceCollector(win).CollectAsync(Node(@"C:\Users\me\AppData\Local\Temp\abc.tmp"));
        Assert.DoesNotContain(b.Evidences, e => e.Kind == EvidenceKind.InstalledApp);
    }

    [Fact] // 无任何已安装应用匹配 → 不臆造归属。
    public async Task Unmatched_data_dir_yields_no_attribution()
    {
        var win = new FakeWin { Apps = new[] { new InstalledApp("Visual Studio", null, null, "Registry") } };
        var b = await new EvidenceCollector(win).CollectAsync(Node(@"C:\Users\me\AppData\Local\ObscureThing\x.bin"));
        Assert.DoesNotContain(b.Evidences, e => e.Kind == EvidenceKind.InstalledApp);
    }

    private static FileMetadata Meta(string? signer = null, bool? signed = null,
        string? company = null, string? version = null) =>
        new(0, ".exe", null, null, company, version, signed, signer, null, null, null);

    // —— 测试替身: 可配置的 IWindowsAccess ——
    private sealed class FakeWin : IWindowsAccess
    {
        public FileMetadata? Metadata { get; set; }
        public string? Occupier { get; set; }
        public IReadOnlyList<InstalledApp> Apps { get; set; } = Array.Empty<InstalledApp>();

        public Task<FileMetadata?> ReadMetadataAsync(string path, CancellationToken ct = default)
            => Task.FromResult(Metadata);
        public IReadOnlyList<InstalledApp> GetInstalledApplications() => Apps;
        public string? GetOccupyingProcessName(string path) => Occupier;
        public string ResolveRealPath(string path) => path;
    }
}
