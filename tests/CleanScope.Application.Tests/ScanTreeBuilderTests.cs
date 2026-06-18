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

    private static DecisionItem File(string path, long size, RiskLevel risk = RiskLevel.C,
        string? origin = null) =>
        new(path, size, null, risk, "建议", "说明", new long[] { 1 }, ExclusiveSize: size,
            IsContainer: false, Origin: origin);

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

    [Fact] // P2: 整盘可清理 = 顶层可清理节点之和 (父子不重复计数)。
    public void Cleanable_total_dedups_parent_child()
    {
        var items = new[]
        {
            Dir(@"C:\", 1000, container: true),
            Dir(@"C:\AppCache", 600, RiskLevel.B),       // 可清理 (含其下子缓存)
            Dir(@"C:\AppCache\Inner", 400, RiskLevel.B), // 其子也可清理 → 不再单独累加
            Dir(@"C:\Data", 200, RiskLevel.C),           // 不可清理
            Dir(@"C:\Data\Logs", 150, RiskLevel.B),      // 谨慎目录下的缓存 → 单独计入
        };
        var root = ScanTreeBuilder.Build(@"C:\", items, 1000);

        Assert.Equal(600 + 150, ScanTreeStats.CleanableTotal(root));   // AppCache(600) + Data\Logs(150)
        Assert.Equal(2, ScanTreeStats.CleanableCount(root));          // 两处顶层可清理
    }

    [Fact] // bug 修复: 大文件作为叶子挂到父目录下, 让"只装一个大文件的目录"在树里可见且有内容 (而非整条等大余量)。
    public void Attaches_large_files_as_leaf_children()
    {
        var items = new[]
        {
            Dir(@"C:\", 700, container: true),
            Dir(@"C:\cli-plugins", 690, origin: "Docker"),
            File(@"C:\cli-plugins\docker-buildx.exe", 690, origin: "Docker"),
        };
        var root = ScanTreeBuilder.Build(@"C:\", items, totalSize: 700);

        var pluginDir = root.Children.Single(c => c.Path == @"C:\cli-plugins");
        var file = Assert.Single(pluginDir.Children);
        Assert.Equal(@"C:\cli-plugins\docker-buildx.exe", file.Path);
        Assert.Equal("docker-buildx.exe", file.Name);
        Assert.False(file.IsContainer);
        Assert.False(file.HasChildren);                 // 文件是叶子, 不可再展开
        Assert.Equal(0, pluginDir.Remainder);           // 子项已覆盖全部大小 → 不再冒出"等大余量"
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
