namespace CleanScope.Core.Scanning;

/// <summary>
/// 扫描引擎 (裁决链第 1 环, 架构§3)。只读遍历: 递归 DFS + 目录大小自底向上聚合 + 有界最小堆 TopN。
/// 不删除、不评估、不解释 —— 仅产出结构化 <see cref="FileNode"/>; Id/TaskId/ParentId 由编排层持久化时赋值。
///
/// 纯 <c>System.IO</c> 实现, 不依赖 Windows 专有 API (决议9: Core 为 net8.0, 跨平台可测)。
///
/// 安全/健壮性契约:
///  - SR-10: 无权限目录捕获异常 → 记录节点并按扫描模式标 <see cref="AccessState.NeedAdmin"/>(普通模式, 提权或可解)
///           或 <see cref="AccessState.Denied"/>(已提权仍被拒, 真不可访问), 绝不崩溃 (T1.4)。
///  - IR-4: 识别重解析点 (junction/symlink) → 标 <c>IsReparsePoint</c>、解析真实路径写入 <c>RealPath</c>,
///          且不递归进入 (防环 / 越出扫描根)。删除前的权威 Win32 路径校验另由 Safety 层负责。
///  - 中断恢复 (T1.4): 每个定稿节点可流式投递给编排层增量持久化; <c>ScanOptions.SkipPaths</c>
///          令续扫跳过已落库子树 (不下钻, 其大小不计入父聚合, 由编排层合并)。
/// </summary>
public sealed class ScanEngine : IScanEngine
{
    // 进度回调节流: 每遍历这么多条目才上报一次, 避免淹没 UI/日志。
    private const int ProgressEvery = 512;

    public Task<IReadOnlyList<FileNode>> ScanAsync(
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
        => ScanAsync(options, onNode: null, progress, ct);

    public Task<IReadOnlyList<FileNode>> ScanAsync(
        ScanOptions options,
        IProgress<FileNode>? onNode,
        IProgress<ScanProgress>? progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        // 遍历是同步阻塞 IO; 移入线程池, 保持接口异步语义且不阻塞调用方。
        return Task.Run<IReadOnlyList<FileNode>>(() => ScanCore(options, onNode, progress, ct), ct);
    }

    private static IReadOnlyList<FileNode> ScanCore(
        ScanOptions options, IProgress<FileNode>? onNode, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var ctx = new ScanContext(options.TopN, options.Mode, options.SkipPaths, onNode, progress);
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

    // 返回该目录子树聚合字节数; 沿途把节点投喂最小堆 + 流式 sink。
    private static long VisitDirectory(DirectoryInfo dir, ScanContext ctx, DateTime now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 续扫: 已落库子树整棵跳过 (不下钻、不产节点); 其大小由编排层从库中合并。
        if (ctx.ShouldSkip(dir.FullName))
            return 0;

        // 重解析点不递归 (防环 / 越出扫描根); 解析真实路径写入 RealPath (IR-4)。
        if (SafeIsReparsePoint(dir))
        {
            ctx.Emit(MakeNode(dir, isDir: true, isReparse: true, size: 0,
                AccessState.Accessible, ResolveRealPath(dir), now));
            ctx.EnterDir(dir.FullName);
            return 0;
        }

        var (children, state) = SafeEnumerate(dir, ctx.Mode);
        long total = 0;
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (child is DirectoryInfo sub)
                total += VisitDirectory(sub, ctx, now, ct);
            else if (child is FileInfo file)
                total += VisitFile(file, ctx, now, ct);
        }

        ctx.Emit(MakeNode(dir, isDir: true, isReparse: false, size: total, state, realPath: null, now));
        ctx.EnterDir(dir.FullName);
        return total;
    }

    private static long VisitFile(FileInfo file, ScanContext ctx, DateTime now, CancellationToken ct)
    {
        long size = SafeLength(file);
        bool isReparse = SafeIsReparsePoint(file);
        string? realPath = isReparse ? ResolveRealPath(file) : null;
        ctx.Emit(MakeNode(file, isDir: false, isReparse, size, AccessState.Accessible, realPath, now));
        ctx.CountFile(file.FullName, size);
        return size;
    }

    private static FileNode MakeNode(
        FileSystemInfo info, bool isDir, bool isReparse, long size,
        AccessState state, string? realPath, DateTime now)
    {
        var (mtime, atime) = SafeTimes(info);
        return new FileNode(
            Id: 0,                  // 持久化时由 SQLite 赋值
            TaskId: 0,              // 编排层 (T1.11) 写入时 stamp 真实 taskId
            ParentId: null,         // TopN/流式为扁平结果, 不保留树形父子 (按 Path 自描述)
            Path: info.FullName,
            RealPath: realPath,     // IR-4: 仅重解析点解析出真实目标; 普通节点为 null
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

    /// <summary>无权限子树的降级状态: 普通模式下提权或可解 → NeedAdmin; 已提权仍被拒 → 真不可访问 Denied。</summary>
    internal static AccessState DeniedStateFor(ScanMode mode) =>
        mode == ScanMode.Admin ? AccessState.Denied : AccessState.NeedAdmin;

    private static bool SafeIsReparsePoint(FileSystemInfo info)
    {
        try { return info.Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch { return false; }
    }

    // IR-4: 解析重解析点的真实目标路径 (跟随链到最终目标)。失败返回 null, 不抛。
    private static string? ResolveRealPath(FileSystemInfo info)
    {
        try { return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.LinkTarget; }
        catch { return null; }
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

    // 捕获异常以将"当前目录"按扫描模式降级标记并保留已枚举到的部分结果 (SR-10), 而非静默跳过或崩溃。
    // 注: 不用 EnumerationOptions.IgnoreInaccessible —— 那会静默吞掉被拒子项; 我们要的是可记录的降级。
    private static (IReadOnlyList<FileSystemInfo> Children, AccessState State) SafeEnumerate(
        DirectoryInfo dir, ScanMode mode)
    {
        var items = new List<FileSystemInfo>();
        try
        {
            foreach (var fsi in dir.EnumerateFileSystemInfos())
                items.Add(fsi);
            return (items, AccessState.Accessible);
        }
        catch (UnauthorizedAccessException) { return (items, DeniedStateFor(mode)); }
        catch (System.Security.SecurityException) { return (items, DeniedStateFor(mode)); }
        catch (IOException) { return (items, DeniedStateFor(mode)); }
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

    /// <summary>遍历过程中的可变状态: 堆 + 流式 sink + 跳过集 + 进度计数。</summary>
    private sealed class ScanContext
    {
        public TopNHeap Heap { get; }
        public ScanMode Mode { get; }
        private readonly HashSet<string> _skip;
        private readonly IProgress<FileNode>? _onNode;
        private readonly IProgress<ScanProgress>? _progress;
        private long _files;
        private long _bytes;
        private long _sinceReport;

        public ScanContext(
            int topN, ScanMode mode, IReadOnlyList<string>? skipPaths,
            IProgress<FileNode>? onNode, IProgress<ScanProgress>? progress)
        {
            Heap = new TopNHeap(topN);
            Mode = mode;
            // Windows 路径大小写不敏感; 续扫跳过集按 OrdinalIgnoreCase 匹配。
            _skip = skipPaths is { Count: > 0 }
                ? new HashSet<string>(skipPaths, StringComparer.OrdinalIgnoreCase)
                : EmptySkip;
            _onNode = onNode;
            _progress = progress;
        }

        private static readonly HashSet<string> EmptySkip = new(StringComparer.OrdinalIgnoreCase);

        public bool ShouldSkip(string fullPath) => _skip.Count > 0 && _skip.Contains(fullPath);

        // 定稿一个节点: 入 TopN 堆, 并全量流式投递给编排层 (中断恢复的耐久性基础)。
        public void Emit(FileNode node)
        {
            Heap.Offer(node);
            _onNode?.Report(node);
        }

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
