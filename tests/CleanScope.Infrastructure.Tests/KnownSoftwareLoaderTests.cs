using CleanScope.Infrastructure.Attribution;

namespace CleanScope.Infrastructure.Tests;

// ① 特征库加载: 合并 signatures/*.json; 缺目录/坏文件静默降级 (纯增强, 非安全关键)。
public sealed class KnownSoftwareLoaderTests : IDisposable
{
    private readonly string _dir;

    public KnownSoftwareLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cleanscope_sig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task Missing_directory_returns_empty()
    {
        var data = await new KnownSoftwareLoader(Path.Combine(_dir, "nope")).LoadAsync();
        Assert.Empty(data.Vendors);
        Assert.Empty(data.Directories);
    }

    [Fact]
    public async Task Loads_and_merges_vendors_and_directories()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.json"),
            "{ \"vendors\": [ { \"contains\": \"Valve\", \"name\": \"Steam\" } ], " +
            "\"directories\": [ { \"name\": \"wechat\", \"app\": \"微信\", \"purpose\": \"数据\" } ] }");
        await File.WriteAllTextAsync(Path.Combine(_dir, "b.json"),
            "{ \"vendors\": [ { \"contains\": \"Tencent\", \"name\": \"腾讯\" } ] }");

        var data = await new KnownSoftwareLoader(_dir).LoadAsync();

        Assert.Equal(2, data.Vendors.Count);
        Assert.Single(data.Directories);
        Assert.Contains(data.Vendors, v => v.Name == "Steam");
        Assert.Contains(data.Vendors, v => v.Name == "腾讯");
        Assert.Equal("微信", data.Directories[0].App);
    }

    [Fact] // 坏文件被跳过, 不影响其余 (不抛)
    public async Task Corrupt_file_is_skipped()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "good.json"),
            "{ \"vendors\": [ { \"contains\": \"Valve\", \"name\": \"Steam\" } ] }");
        await File.WriteAllTextAsync(Path.Combine(_dir, "bad.json"), "{ this is not json ]");

        var data = await new KnownSoftwareLoader(_dir).LoadAsync();
        Assert.Single(data.Vendors);
        Assert.Equal("Steam", data.Vendors[0].Name);
    }

    [Fact] // 内置特征库文件真实可解析 (随仓库分发的 signatures/known-software.json)
    public async Task Shipped_pack_parses_when_present()
    {
        var repoPack = FindRepoSignatures();
        if (repoPack is null) return;   // CI 无仓库根时跳过
        var data = await new KnownSoftwareLoader(repoPack).LoadAsync();
        Assert.NotEmpty(data.Vendors);
        Assert.NotEmpty(data.Directories);
    }

    private static string? FindRepoSignatures()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "signatures");
            if (File.Exists(Path.Combine(d.FullName, "CleanScope.sln")) && Directory.Exists(c)) return c;
        }
        return null;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
