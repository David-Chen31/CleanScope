using CleanScope.Core.Scanning;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// T1.3: 构造真实临时目录树验证遍历 + 自底向上聚合 + 最小堆 TopN + SR-10 降级。
public sealed class ScanEngineTests : IDisposable
{
    private readonly string _root;

    // 临时树:
    //   root/            (聚合 650)
    //     a.txt   100
    //     sub1/          (聚合 500)
    //       b.txt 300
    //       c.txt 200
    //     sub2/          (聚合 50)
    //       d.txt 50
    public ScanEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanscope_scan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "sub1"));
        Directory.CreateDirectory(Path.Combine(_root, "sub2"));
        WriteFile(Path.Combine(_root, "a.txt"), 100);
        WriteFile(Path.Combine(_root, "sub1", "b.txt"), 300);
        WriteFile(Path.Combine(_root, "sub1", "c.txt"), 200);
        WriteFile(Path.Combine(_root, "sub2", "d.txt"), 50);
    }

    private static void WriteFile(string path, int bytes) =>
        File.WriteAllBytes(path, new byte[bytes]);

    private ScanOptions Options(int topN) => new(_root, topN, ScanMode.Normal);

    [Fact]
    public async Task Directory_sizes_aggregate_bottom_up()
    {
        var nodes = await new ScanEngine().ScanAsync(Options(100));
        var byPath = nodes.ToDictionary(n => n.Path, n => n);

        Assert.Equal(650, byPath[_root].Size);                              // 根 = 全部
        Assert.Equal(500, byPath[Path.Combine(_root, "sub1")].Size);       // 子树聚合
        Assert.Equal(50, byPath[Path.Combine(_root, "sub2")].Size);
        Assert.True(byPath[_root].IsDirectory);
        Assert.Equal(AccessState.Accessible, byPath[_root].AccessState);
    }

    [Fact]
    public async Task File_node_carries_real_size_and_structure()
    {
        var nodes = await new ScanEngine().ScanAsync(Options(100));
        var b = nodes.Single(n => n.Name == "b.txt");

        Assert.Equal(300, b.Size);
        Assert.False(b.IsDirectory);
        Assert.False(b.IsReparsePoint);
        Assert.Null(b.NodeType);                 // 扫描不分类 (规则引擎职责)
        Assert.Null(b.ParentId);                 // TopN 扁平结果
    }

    [Fact]
    public async Task TopN_returns_largest_in_descending_order()
    {
        var nodes = await new ScanEngine().ScanAsync(Options(3));

        Assert.Equal(3, nodes.Count);
        // 文件+目录混合按 size 排序: root(650) > sub1(500) > b.txt(300)
        Assert.Equal(new long[] { 650, 500, 300 }, nodes.Select(n => n.Size).ToArray());
        Assert.True(nodes[0].Size >= nodes[1].Size && nodes[1].Size >= nodes[2].Size);
    }

    [Fact]
    public async Task TopN_zero_yields_empty()
    {
        var nodes = await new ScanEngine().ScanAsync(Options(0));
        Assert.Empty(nodes);
    }

    [Fact]
    public async Task Progress_reports_final_totals()
    {
        ScanProgress? last = null;
        var progress = new SyncProgress(p => last = p);

        await new ScanEngine().ScanAsync(Options(100), progress);

        Assert.NotNull(last);
        Assert.Equal(4, last!.FilesScanned);     // a,b,c,d (目录不计入文件数)
        Assert.Equal(650, last.BytesScanned);
        Assert.Null(last.CurrentPath);           // 收尾上报
    }

    [Fact]
    public async Task Missing_root_throws_not_found()
    {
        var opts = new ScanOptions(Path.Combine(_root, "does_not_exist"), 10, ScanMode.Normal);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new ScanEngine().ScanAsync(opts));
    }

    [Fact]
    public async Task Cancellation_is_observed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => new ScanEngine().ScanAsync(Options(100), null, cts.Token));
    }

    // P1: 多个顶层子目录触发并行扇出 —— 验证聚合/计数与预期严格一致 (无并发丢失/重复)。
    [Fact]
    public async Task Parallel_top_level_subdirs_aggregate_correctly()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanscope_par_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        long expected = 0; int files = 0;
        for (var i = 0; i < 12; i++)
        {
            var d = Path.Combine(root, "d" + i);
            Directory.CreateDirectory(d);
            for (var j = 0; j < 5; j++)
            {
                var size = 1000 * (i + 1) + j;
                WriteFile(Path.Combine(d, $"f{j}.bin"), size);
                expected += size; files++;
            }
        }
        try
        {
            ScanProgress? last = null;
            var nodes = await new ScanEngine().ScanAsync(
                new ScanOptions(root, 1000, ScanMode.Normal), new SyncProgress(p => last = p));
            var byPath = nodes.ToDictionary(n => n.Path, n => n);

            Assert.Equal(expected, byPath[root].Size);     // 根聚合 = 全部 (并发求和正确)
            Assert.Equal(files, last!.FilesScanned);       // 文件计数无丢失/重复
            Assert.Equal(expected, last.BytesScanned);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { /* 尽力而为 */ } }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 清理尽力而为 */ }
    }

    // 同步派发的 IProgress (默认 Progress<T> 经 SynchronizationContext 异步派发, 测试不可靠)。
    private sealed class SyncProgress(Action<ScanProgress> onReport) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => onReport(value);
    }
}
