using System.Text;

namespace CleanScope.Architecture.Tests;

// T-07 / T-08 (IR-1/SR-3/IR-3): 静态断言 —— 产品源码 (src) 中不存在任何永久删除 / 批量删除 代码路径。
// S-E 后: 删除唯一出口是回收站 (可恢复), 集中在单一豁免文件 WindowsRecycleBin.cs, 且该文件被正向断言
// "仅 SendToRecycleBin、绝不 DeletePermanently"。本测试是 CI 硬门禁的一部分。
public sealed class NoPermanentDeleteTests
{
    // 唯一允许触碰删除 API 的文件 (回收站可恢复删除), 其余源码一律禁止。
    private const string RecycleBinFile = "WindowsRecycleBin.cs";

    // 永久删除 / 绕过回收站 / 批量删除 的调用特征。
    private static readonly string[] ForbiddenTokens =
    {
        "File.Delete(", "Directory.Delete(",          // .NET 永久删除 (IR-1/SR-3)
        "FileSystem.DeleteFile", "DeleteFile(",       // VB / Win32 永久删除
        "SHFileOperation", "RemoveDirectory(",        // Win32
        "DeleteAll", "DeleteMany", "PurgeAll",        // 批量删除 (IR-3)
    };

    [Fact]
    public void Product_source_contains_no_permanent_or_batch_delete()
    {
        var srcDir = SrcDir();
        var offenders = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            // 回收站文件单独豁免 (它本就必须含 VisualBasic 的 Delete* 调用), 由下方专项测试正向把关。
            if (Path.GetFileName(file).Equals(RecycleBinFile, StringComparison.OrdinalIgnoreCase)) continue;
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenTokens)
                if (text.Contains(token, StringComparison.Ordinal))
                    offenders.AppendLine($"{Path.GetFileName(file)} 含禁止的删除调用: {token}");
        }

        Assert.True(offenders.Length == 0,
            "src 中不得出现永久/批量删除代码路径 (IR-1/SR-3/IR-3, 回收站文件除外):\n" + offenders);
    }

    [Fact] // S-E: 豁免文件必须仅用回收站可恢复接口, 绝不永久删除。
    public void RecycleBin_file_uses_recoverable_api_only()
    {
        var files = Directory.EnumerateFiles(SrcDir(), RecycleBinFile, SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f)).ToList();
        Assert.Single(files);                                   // 唯一一处删除实现, 集中可审计

        var text = File.ReadAllText(files[0]);
        Assert.Contains("RecycleOption.SendToRecycleBin", text); // 必须移入回收站 (可恢复)
        Assert.DoesNotContain("DeletePermanently", text);       // 绝不永久删除
        Assert.DoesNotContain("SHFileOperation", text);         // 不绕过到 Win32 永久删除
    }

    [Fact] // 除豁免文件外, 无其它文件出现回收站/VB 删除 API (删除能力集中、不扩散)。
    public void Only_the_recycle_bin_file_touches_delete_api()
    {
        var offenders = Directory.EnumerateFiles(SrcDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f))
            .Where(f => !Path.GetFileName(f).Equals(RecycleBinFile, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("RecycleOption", StringComparison.Ordinal)
                     || File.ReadAllText(f).Contains("FileSystem.Delete", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "仅 WindowsRecycleBin.cs 可触碰删除 API, 以下文件违规:\n" + string.Join("\n", offenders));
    }

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    private static string SrcDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CleanScope.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = Path.Combine(dir!.FullName, "src");
        Assert.True(Directory.Exists(src), "未找到 src 目录");
        return src;
    }
}
