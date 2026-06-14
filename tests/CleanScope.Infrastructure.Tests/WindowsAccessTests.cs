using CleanScope.Infrastructure.Windows;

namespace CleanScope.Infrastructure.Tests;

// T2.1: WindowsAccess —— 文件版本/签名读取 (已知系统文件) + 异常不崩 + 真实路径解析。
public sealed class WindowsAccessTests
{
    private static readonly WindowsAccess Wa = new();

    [Fact]
    public async Task Reads_version_and_company_of_known_system_dll()
    {
        var path = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        if (!File.Exists(path)) return; // 非 Windows/缺文件环境跳过

        var md = await Wa.ReadMetadataAsync(path);
        Assert.NotNull(md);
        Assert.Equal(".dll", md!.Extension);
        Assert.Contains("Microsoft", md.CompanyName ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(md.FileVersion));
    }

    [Fact]
    public async Task Nonexistent_file_returns_null_not_crash()
        => Assert.Null(await Wa.ReadMetadataAsync(@"C:\no\such\__cleanscope_missing__.dll"));

    [Fact]
    public async Task Directory_returns_null()
        => Assert.Null(await Wa.ReadMetadataAsync(Environment.SystemDirectory));

    [Fact]
    public async Task Unsigned_temp_file_reports_not_signed()
    {
        var path = Path.Combine(Path.GetTempPath(), "cs_unsigned_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "hello");
        try
        {
            var md = await Wa.ReadMetadataAsync(path);
            Assert.NotNull(md);
            Assert.False(md!.IsSigned);
            Assert.Null(md.Signer);
            Assert.Equal(".txt", md.Extension);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Signature_reading_is_robust_and_covers_embedded_signed_assemblies()
    {
        var dlls = Directory.GetFiles(AppContext.BaseDirectory, "*.dll");
        int signed = 0;
        foreach (var p in dlls)
        {
            var md = await Wa.ReadMetadataAsync(p);
            Assert.NotNull(md);                       // 读取永不崩
            if (md!.IsSigned == true)
            {
                signed++;
                Assert.False(string.IsNullOrWhiteSpace(md.Signer)); // 已签名必有签名者
            }
        }
        // 输出目录里至少有 Microsoft 框架/包程序集为嵌入签名 → 正向覆盖签名者读取路径。
        Assert.True(signed > 0, "预期输出目录至少有一个嵌入式签名程序集");
    }

    [Fact]
    public void ResolveRealPath_returns_full_path_for_regular_directory()
    {
        var sysDir = Environment.SystemDirectory;
        Assert.Equal(sysDir, Wa.ResolveRealPath(sysDir), ignoreCase: true);
    }

    [Fact]
    public void ResolveRealPath_does_not_throw_on_bad_input()
        => Assert.Equal(@"C:\nope\x", Wa.ResolveRealPath(@"C:\nope\x"));
}
