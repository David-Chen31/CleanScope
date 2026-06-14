using CleanScope.Core.Scanning;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T1.4: 扫描健壮性 —— 权限降级 / IR-4 真实路径 / 中断续扫 (流式 + 跳过)。
public sealed class ScanEngineRobustnessTests : IDisposable
{
    private readonly string _root;

    // root/ a.txt(100)  sub1/{b.txt(300),c.txt(200)}  sub2/d.txt(50)
    public ScanEngineRobustnessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanscope_rb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "sub1"));
        Directory.CreateDirectory(Path.Combine(_root, "sub2"));
        File.WriteAllBytes(Path.Combine(_root, "a.txt"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "sub1", "b.txt"), new byte[300]);
        File.WriteAllBytes(Path.Combine(_root, "sub1", "c.txt"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_root, "sub2", "d.txt"), new byte[50]);
    }

    [Theory]
    [InlineData(ScanMode.Normal, AccessState.NeedAdmin)] // 普通模式被拒 → 提权或可解
    [InlineData(ScanMode.Admin, AccessState.Denied)]     // 已提权仍被拒 → 真不可访问
    public void DeniedState_depends_on_scan_mode(ScanMode mode, AccessState expected)
        => Assert.Equal(expected, ScanEngine.DeniedStateFor(mode));

    [Fact]
    public async Task Streaming_emits_every_node_for_incremental_persistence()
    {
        var streamed = new List<FileNode>();
        var sink = new Sink<FileNode>(streamed.Add);

        var top = await new ScanEngine().ScanAsync(
            new ScanOptions(_root, 100, ScanMode.Normal), sink, null);

        // 全量流式: 4 文件 + 3 目录 = 7; TopN 返回值是其子集。
        Assert.Equal(7, streamed.Count);
        Assert.True(streamed.Count >= top.Count);
        Assert.Contains(streamed, n => n.Name == "b.txt" && n.Size == 300);
        Assert.Contains(streamed, n => n.Path == _root && n.Size == 650);
    }

    [Fact]
    public async Task SkipPaths_excludes_already_scanned_subtree_on_resume()
    {
        var streamed = new List<FileNode>();
        var sink = new Sink<FileNode>(streamed.Add);
        var sub1 = Path.Combine(_root, "sub1");

        await new ScanEngine().ScanAsync(
            new ScanOptions(_root, 100, ScanMode.Normal, SkipPaths: new[] { sub1 }), sink, null);

        Assert.DoesNotContain(streamed, n => n.Path == sub1);          // 整棵跳过
        Assert.DoesNotContain(streamed, n => n.Name is "b.txt" or "c.txt");
        Assert.Contains(streamed, n => n.Name == "d.txt");             // sub2 仍扫
        // 根聚合不含被跳过子树 (a=100 + sub2=50); 编排层负责合并已落库的 sub1。
        Assert.Equal(150, streamed.Single(n => n.Path == _root).Size);
    }

    [Fact]
    public async Task Reparse_point_is_flagged_resolved_and_not_descended()
    {
        // 重解析点(符号链接)需 Developer Mode/管理员; 无权限则优雅跳过本断言。
        var target = Path.Combine(Path.GetTempPath(), "cleanscope_tgt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "inside.txt"), new byte[999]);
        var link = Path.Combine(_root, "link_to_target");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // 环境无权创建链接, 跳过 (CI/无 Dev Mode)
        }
        finally
        {
            // target 在 _root 外, 单独清理
            try { Directory.Delete(target, true); } catch { }
        }

        var nodes = await new ScanEngine().ScanAsync(new ScanOptions(_root, 100, ScanMode.Normal));
        var linkNode = nodes.Single(n => n.Path == link);

        Assert.True(linkNode.IsReparsePoint);                         // 标记
        Assert.NotNull(linkNode.RealPath);                           // IR-4: 解析出真实目标
        Assert.Equal(0, linkNode.Size);                              // 不下钻 → 不计链接目标大小
        Assert.DoesNotContain(nodes, n => n.Name == "inside.txt");   // 未越过链接递归
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class Sink<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
