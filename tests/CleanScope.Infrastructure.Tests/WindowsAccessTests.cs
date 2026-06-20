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

    [Fact] // T3: 采样含程序集的目录 → 返回某代表性二进制的版本信息 (不崩, 有归因字段)。
    public async Task SampleDirectoryBinary_returns_metadata_for_dir_with_binaries()
    {
        var md = await Wa.SampleDirectoryBinaryAsync(AppContext.BaseDirectory, includeSignature: false);
        Assert.NotNull(md);   // 测试输出目录满是带版本信息的 .dll/.exe
        Assert.True(!string.IsNullOrWhiteSpace(md!.ProductName)
                    || !string.IsNullOrWhiteSpace(md.CompanyName)
                    || !string.IsNullOrWhiteSpace(md.FileVersion));
    }

    [Fact] // T3: includeSignature 时可读签名 (有签名程序集则给签名者)。
    public async Task SampleDirectoryBinary_with_signature_does_not_crash()
    {
        var md = await Wa.SampleDirectoryBinaryAsync(AppContext.BaseDirectory, includeSignature: true);
        Assert.NotNull(md);
    }

    [Fact] // 空目录无可执行文件 → null。
    public async Task SampleDirectoryBinary_returns_null_for_empty_dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs_sample_empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { Assert.Null(await Wa.SampleDirectoryBinaryAsync(dir, includeSignature: false)); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact] // 不存在的目录 → null, 不崩。
    public async Task SampleDirectoryBinary_returns_null_for_missing_dir()
        => Assert.Null(await Wa.SampleDirectoryBinaryAsync(@"C:\no\such\__cs_missing__", includeSignature: false));

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
