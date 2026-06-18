using CleanScope.Application;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Application.Tests;

// P1: 从分类后的目录项重建全盘目录树 —— 祖先挂接、同级按大小降序、根处理、余量。
public sealed class ScanTreeBuilderTests
{
    private static DecisionItem Dir(string path, long size, RiskLevel risk = RiskLevel.C,
        string? origin = null, bool container = false) =>
        new(path, size, null, risk, "建议", "说明", new long[] { 1 }, ExclusiveSize: size,
            IsContainer: container, Origin: origin);

    [Fact]
    public void Builds_hierarchy_sorted_by_size_with_cleanable_flag()
    {
        var items = new[]
        {
            Dir(@"C:\", 1000, container: true, origin: "磁盘根目录"),
            Dir(@"C:\App", 600, origin: "App"),
            Dir(@"C:\App\Cache", 400, RiskLevel.B, origin: "App"),   // 可清理
            Dir(@"C:\App\Data", 150, RiskLevel.C, origin: "App"),
            Dir(@"C:\Small", 300, origin: "X"),
        };

        var root = ScanTreeBuilder.Build(@"C:\", items, totalSize: 1000);

        Assert.Equal(@"C:\", root.Path);
        // 顶层按大小降序: App(600) 在 Small(300) 前。
        Assert.Equal(@"C:\App", root.Children[0].Path);
        Assert.Equal(@"C:\Small", root.Children[1].Path);
        // App 下: Cache(400) 在 Data(150) 前; Cache 标可清理。
        var app = root.Children[0];
        Assert.Equal(@"C:\App\Cache", app.Children[0].Path);
        Assert.True(app.Children[0].IsCleanable);
        Assert.False(app.Children[1].IsCleanable);
        // 余量 = App.Size - 子之和 = 600 - 550 = 50 (直接文件/小目录)。
        Assert.Equal(50, app.Remainder);
        // 容器项不算可清理。
        Assert.False(root.IsCleanable);
        Assert.Equal("磁盘根目录", root.Origin);
    }

    [Fact] // 扫描目标不是节点本身时, 合成一个根把顶层挂上。
    public void Synthesizes_root_when_target_absent()
    {
        var items = new[] { Dir(@"C:\A", 100), Dir(@"C:\B", 200) };
        var root = ScanTreeBuilder.Build(@"C:\", items, totalSize: 500);

        Assert.Equal(@"C:\", root.Path);
        Assert.Equal(500, root.Size);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal(@"C:\B", root.Children[0].Path);   // 大的在前
    }
}
