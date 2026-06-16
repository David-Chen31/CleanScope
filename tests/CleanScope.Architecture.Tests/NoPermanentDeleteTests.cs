using System.Text;

namespace CleanScope.Architecture.Tests;

// T-07 / T-08 (IR-1/SR-3/IR-3): 静态断言 —— 产品源码 (src) 中不存在任何永久删除 / 批量删除 代码路径。
// 删除能力在 v1 范围内"不存在", 而非"被禁用"。本测试是 CI 硬门禁的一部分。
public sealed class NoPermanentDeleteTests
{
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
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenTokens)
                if (text.Contains(token, StringComparison.Ordinal))
                    offenders.AppendLine($"{Path.GetFileName(file)} 含禁止的删除调用: {token}");
        }

        Assert.True(offenders.Length == 0,
            "src 中不得出现永久/批量删除代码路径 (IR-1/SR-3/IR-3):\n" + offenders);
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
