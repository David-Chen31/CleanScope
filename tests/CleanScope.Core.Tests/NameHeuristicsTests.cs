using CleanScope.Core.Attribution;

namespace CleanScope.Core.Tests;

// E5: 凭目录名就能确定用途/归属的常见目录, 给确定性结论 (不依赖 AI), 消除"无法判断/未知来源"。
public sealed class NameHeuristicsTests
{
    [Theory]
    [InlineData(@"C:\图片", "图片库", "图片")]
    [InlineData(@"C:\Users\28170\Pictures", "图片库", "图片")]
    [InlineData(@"C:\Users\28170\Downloads", "下载", "下载")]
    [InlineData(@"C:\Users\28170\.claude", "Claude", "Claude")]
    [InlineData(@"C:\proj\.vscode", "VS Code", "VS Code")]
    [InlineData(@"C:\proj\.git", "Git", "Git")]
    public void Resolves_known_names(string path, string origin, string purposeFragment)
    {
        var hint = NameHeuristics.Resolve(path);
        Assert.NotNull(hint);
        Assert.Equal(origin, hint!.Value.Origin);
        Assert.Contains(purposeFragment, hint.Value.Purpose);
    }

    [Fact] // 未收录的点目录: 通用兜底, 凭名给出推断 (并明确标注"推断自目录名")。
    public void Generic_dot_dir_falls_back_with_inferred_label()
    {
        var hint = NameHeuristics.Resolve(@"C:\Users\28170\.foobar");
        Assert.NotNull(hint);
        Assert.Contains("foobar", hint!.Value.Origin);
        Assert.Contains("推断", hint.Value.Origin);
    }

    [Theory] // 普通目录/文件名 → null, 交回常规归因链
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files\App\bin")]
    [InlineData(@"")]
    public void Unknown_names_return_null(string path)
        => Assert.Null(NameHeuristics.Resolve(path));
}
