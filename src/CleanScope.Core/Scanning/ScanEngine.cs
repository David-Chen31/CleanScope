namespace CleanScope.Core.Scanning;

/// <summary>
/// 扫描引擎 (裁决链第 1 环, 架构§3)。只读遍历: 递归 DFS + 目录大小自底向上聚合 + 有界最小堆 TopN。
/// 不删除、不评估、不解释 —— 仅产出结构化 <see cref="FileNode"/>; Id/TaskId/ParentId 由编排层持久化时赋值。
///
/// 纯 <c>System.IO</c> 实现, 不依赖 Windows 专有 API (决议9: Core 为 net8.0, 跨平台可测)。
///
/// 安全契约:
///  - SR-10: 无权限目录捕获异常 → 标 <see cref="AccessState.Denied"/> 并记录节点, 绝不崩溃。
///  - IR-4 起步: 识别重解析点 (junction/symlink) → 标 <c>IsReparsePoint</c> 且不递归进入 (防环/越出扫描根);
///             真实路径解析 (<c>IWindowsAccess.ResolveRealPath</c>) 与中断续扫属 T1.4。
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    // 进度回调节流: 每遍历这么多条目才上报一次, 避免淹没 UI/日志。
    private const int ProgressEvery = 512;

    public Task<IReadOnlyList<FileNode>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        // 遍历是同步阻塞 IO; 移入线程池, 保持接口异步语义且不阻塞调用方。
        return Task.Run<IReadOnlyList<FileNode>>(() => ScanCore(options, progress, ct), ct);
    }

    private static IReadOnlyList<FileNode> ScanCore(
        ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var ctx = new ScanContext(options.TopN, progress);
        var now = DateTime.UtcNow;

        if (Directory.Exists(options.TargetPath))
            VisitDirectory(new DirectoryInfo(options.TargetPath), ctx, now, ct);
        else if (File.Exists(options.TargetPath))
            VisitFile(new FileInfo(options.TargetPath), ctx, now, ct);
        else
            throw new DirectoryNotFoundException($"扫描根路径不存在: {options.TargetPath}");

        ctx.Flush();
        return ctx.Heap.ToDescending();
    }

    // 返回该目录子树聚合字节数; 沿途把节点投喂最小堆。
    private static long VisitDirectory(DirectoryInfo dir, ScanContext ctx, DateTime now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 重解析点不递归 (防环 / 越出扫描根); 记录为 0 大小的目录节点。
        if (SafeIsReparsePoint(dir))
        {
            ctx.Offer(MakeNode(dir, isDir: true, isReparse: true, size: 0, AccessState.Accessible, now));
            ctx.EnterDir(dir.FullName);
            return 0;
        }

        var (children, state) = SafeEnumerate(dir);
        long total = 0;
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (child is DirectoryInfo sub)
                total += VisitDirectory(sub, ctx, now, ct);
            else if (child is FileInfo file)
                total += VisitFile(file, ctx, now, ct);
        }

        ctx.Offer(MakeNode(dir, isDir: true, isReparse: false, size: total, state, now));
        ctx.EnterDir(dir.FullName);
        return total;
    }

    private static long VisitFile(FileInfo file, ScanContext ctx, DateTime now, CancellationToken ct)
    {
        long size = SafeLength(file);
        ctx.Offer(MakeNode(file, isDir: false, SafeIsReparsePoint(file), size, AccessState.Accessible, now));
        ctx.CountFile(file.FullName, size);
        return size;
    }

    private static FileNode MakeNode(
        FileSystemInfo info, bool isDir, bool isReparse, long size, AccessState state, DateTime now)
    {
        var (mtime, atime) = SafeTimes(info);
        return new FileNode(
            Id: 0,                  // 持久化时由 SQLite 赋值
            TaskId: 0,              // 编排层 (T1.11) 写入时 stamp 真实 taskId
            ParentId: null,         // TopN 为扁平结果, 不保留树形父子 (按 Path 自描述)
            Path: info.FullName,
            RealPath: null,         // IR-4 真实路径解析 → T1.4
            Name: info.Name,
            IsDirectory: isDir,
            IsReparsePoint: isReparse,
            Size: size,
            NodeType: null,         // 分类是规则引擎职责 (T1.7), 扫描只给结构与大小
            Mtime: mtime,
            Atime: atime,
            AccessState: state,
            PreliminaryClass: null,
            CreatedAt: now);
    }

    private static bool SafeIsReparsePoint(FileSystemInfo info)
    {
        try { return info.Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch { return false; }
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch { return 0; }
    }

    private static (DateTime? Mtime, DateTime? Atime) SafeTimes(FileSystemInfo info)
    {
        try { return (info.LastWriteTimeUtc, info.LastAccessTimeUtc); }
        catch { return (null, null); }
    }

    // 捕获异常以将"当前目录"标记为 Denied 并保留已枚举到的部分结果 (SR-10), 而非静默跳过或崩溃。
    // 注: 不用 EnumerationOptions.IgnoreInaccessible —— 那会静默吞掉被拒子项; 我们要的是可记录的降级。
    private static (IReadOnlyList<FileSystemInfo> Children, AccessState State) SafeEnumerate(DirectoryInfo dir)
    {
        var items = new List<FileSystemInfo>();
        try
        {
            foreach (var fsi in dir.EnumerateFileSystemInfos())
                items.Add(fsi);
            return (items, AccessState.Accessible);
        }
        catch (UnauthorizedAccessException) { return (items, AccessState.Denied); }
        catch (System.Security.SecurityException) { return (items, AccessState.Denied); }
        catch (IOException) { return (items, AccessState.Denied); }
    }

    /// <summary>有界最小堆: 始终保留 size 最大的 N 个节点。基于 <see cref="PriorityQueue{TElement,TPriority}"/> (按 size 的 min-heap)。</summary>
    private sealed class TopNHeap
    {
        private readonly int _capacity;
        private readonly PriorityQueue<FileNode, long> _pq = new();

        public TopNHeap(int capacity) => _capacity = Math.Max(0, capacity);

        public void Offer(FileNode node)
        {
            if (_capacity == 0) return;
            if (_pq.Count < _capacity)
            {
                _pq.Enqueue(node, node.Size);
                return;
            }
            // 堆顶 = 当前 TopN 中最小者; 新节点更大才顶替它 (一次 EnqueueDequeue, O(log N))。
            if (_pq.TryPeek(out _, out long min) && node.Size > min)
                _pq.EnqueueDequeue(node, node.Size);
        }

        public IReadOnlyList<FileNode> ToDescending() =>
            _pq.UnorderedItems
               .Select(static x => x.Element)
               .OrderByDescending(static n => n.Size)
               .ToList();
    }

    /// <summary>遍历过程中的可变状态: 堆 + 进度计数 + 节流上报。</summary>
    private sealed class ScanContext
    {
        public TopNHeap Heap { get; }
        private readonly IProgress<ScanProgress>? _progress;
        private long _files;
        private long _bytes;
        private long _sinceReport;

        public ScanContext(int topN, IProgress<ScanProgress>? progress)
        {
            Heap = new TopNHeap(topN);
            _progress = progress;
        }

        public void Offer(FileNode node) => Heap.Offer(node);

        // 进入目录: 仅推进进度路径 (不计入文件数)。
        public void EnterDir(string path) => Bump(path);

        // 计入一个文件 (文件数 + 字节数)。
        public void CountFile(string path, long size)
        {
            _files++;
            _bytes += size;
            Bump(path);
        }

        private void Bump(string currentPath)
        {
            if (_progress is null) return;
            if (++_sinceReport < ProgressEvery) return;
            _sinceReport = 0;
            _progress.Report(new ScanProgress(_files, _bytes, currentPath));
        }

        // 收尾: 无条件上报最终总量 (CurrentPath=null 表示已完成)。
        public void Flush() => _progress?.Report(new ScanProgress(_files, _bytes, null));
    }
}
