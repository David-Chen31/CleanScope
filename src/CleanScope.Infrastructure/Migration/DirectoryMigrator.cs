using System.IO;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Models;

namespace CleanScope.Infrastructure.Migration;

/// <summary>
/// 跨盘目录迁移器 (<see cref="IDirectoryMigrator"/> 的实现)。把"占大头但不能删的合法软件目录"搬到其他盘,
/// 在原位建目录联接, 对应用透明。
///
/// 红线 (绝不永久删除, 全程无 Delete API):
///  1. 保守白名单: 仅迁移用户 AppData / 用户级程序下足够深的子目录; 系统/容器/过浅/已联接路径一律拒。
///  2. 复制到目标盘 → 校验 (字节数 + 文件数一致) → **先写审计** → 把原目录就地改名为 .cleanscope-bak (同卷瞬时, 腾出原路径)
///     → 在原位建联接。任一步失败即回滚 (改名还原), 绝不删除原数据。
///  3. 原盘空间的释放交给用户: 确认软件正常后, 由用户经回收站删除那个 .cleanscope-bak 备份 (我们不自动删)。
/// </summary>
public sealed class DirectoryMigrator : IDirectoryMigrator
{
    private const string BackupSuffix = ".cleanscope-bak";

    private readonly IJunctionCreator _junction;
    private readonly IAuditLogRepository _audit;
    private readonly string _appVersion;
    private readonly MigrationOptions _options;

    public DirectoryMigrator(IJunctionCreator junction, IAuditLogRepository audit,
        string appVersion = "0.1.0", MigrationOptions? options = null)
    {
        _junction = junction;
        _audit = audit;
        _appVersion = appVersion;
        _options = options ?? new MigrationOptions();
    }

    public async Task<MigrationResult> MigrateAsync(MigrationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reject = Validate(request, out var source, out var dest);
        if (reject is not null)
            return new MigrationResult(MigrationOutcome.Rejected, reject, null, null, 0);

        long size;
        try { size = DirSize(source); }
        catch (Exception ex) { return Failed($"无法读取源目录大小: {ex.Message}", null, 0); }

        // 目标盘空间校验 (留 256MB 余量)。
        try
        {
            var destRoot = Path.GetPathRoot(dest)!;
            var free = new DriveInfo(destRoot).AvailableFreeSpace;
            if (free < size + 256L * 1024 * 1024)
                return new MigrationResult(MigrationOutcome.Rejected,
                    $"目标磁盘可用空间不足 (需约 {Human(size)}, 仅剩 {Human(free)})。", null, null, 0);
        }
        catch (Exception ex) { return Failed($"无法检测目标磁盘空间: {ex.Message}", null, 0); }

        // 1) 复制到目标盘 (失败则不动原数据; 残留的半成品复制件留给用户清理, 不调用任何 Delete)。
        try { CopyDirectory(source, dest, ct); }
        catch (Exception ex) { return Failed($"复制到目标盘失败: {ex.Message}（如有残留复制件 {dest} 可手动删除）", dest, 0); }

        // 2) 校验: 字节数 + 文件数一致, 否则中止 (不碰原目录)。
        try
        {
            if (DirSize(dest) != size || CountFiles(dest) != CountFiles(source))
                return Failed($"校验不一致, 已中止 (原目录未改动; 复制件 {dest} 可手动删除)。", dest, 0);
        }
        catch (Exception ex) { return Failed($"校验失败: {ex.Message}", dest, 0); }

        // 3) 先写审计 (SR-9): 在做结构性改名前落库; 失败即中止 (绝不在无审计下改动)。
        try { await _audit.AddAsync(BuildLog(source, dest), ct); }
        catch { return Failed($"审计写入失败, 已中止 (原目录未改动; 复制件 {dest} 可手动删除)。", dest, 0); }

        // 4) 把原目录就地改名为备份 (同卷瞬时, 腾出原路径供建联接)。绝不删除。
        var backup = UniqueBackupPath(source);
        try { Directory.Move(source, backup); }
        catch (Exception ex) { return Failed($"备份原目录失败: {ex.Message} (原目录未改动)。", dest, 0); }

        // 5) 在原位建目录联接 → 目标盘。失败则回滚: 把备份改名还原, 撤销联接残留。
        try
        {
            _junction.Create(source, dest);
        }
        catch (Exception ex)
        {
            TryRestore(backup, source);
            return Failed($"创建目录联接失败: {ex.Message}（已尝试还原原目录; 复制件 {dest} 可手动删除）", dest, 0);
        }

        return new MigrationResult(MigrationOutcome.Success,
            $"已迁移到 {dest} 并在原位创建目录联接, 软件可照常使用。" +
            $"原目录已留作备份 {backup}; 确认软件正常后, 可将该备份移入回收站以释放原盘空间 (我们不会自动删除)。",
            dest, backup, size);
    }

    // —— 保守白名单校验: 只放行用户 AppData / 用户级程序下足够深的子目录 ——
    private string? Validate(MigrationRequest req, out string source, out string dest)
    {
        source = ""; dest = "";
        if (string.IsNullOrWhiteSpace(req.SourceDir)) return "未指定源目录。";
        if (string.IsNullOrWhiteSpace(req.TargetRootDir)) return "未指定目标根目录。";

        source = Path.GetFullPath(req.SourceDir).TrimEnd('\\');
        if (!Directory.Exists(source)) return "源目录不存在或不是目录。";

        // 已是联接/符号链接 → 多半已迁移, 不重复处理。
        if (File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
            return "源目录已是联接/符号链接 (可能已迁移), 未处理。";

        // 保守白名单 (生产恒开): 必须位于某个用户的 AppData 之下, 且足够深 (排除 AppData/Local 等容器本身)。
        if (_options.EnforceUserDataScope)
        {
            var lower = source.ToLowerInvariant();
            var segs = source.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (!lower.Contains(@"\users\") || !lower.Contains(@"\appdata\"))
                return "出于安全, 仅支持迁移用户 AppData 下的软件数据目录。";
            if (lower.Contains(@"\microsoft\windows"))
                return "该目录属于 Windows 用户级系统数据, 不可迁移。";
            if (segs.Length < 6)
                return "该目录层级过浅 (疑似容器目录), 不可整体迁移; 请选择更具体的软件子目录。";
        }

        var targetRoot = Path.GetFullPath(req.TargetRootDir).TrimEnd('\\');
        var srcDrive = Path.GetPathRoot(source);
        var dstDrive = Path.GetPathRoot(targetRoot);
        if (string.IsNullOrEmpty(srcDrive) || string.IsNullOrEmpty(dstDrive)) return "无法解析磁盘根。";
        if (_options.EnforceCrossDrive && srcDrive.Equals(dstDrive, StringComparison.OrdinalIgnoreCase))
            return "目标需是不同的磁盘 (迁移的目的是把占用挪到其他盘)。";

        dest = Path.Combine(targetRoot, Path.GetFileName(source));
        if (Directory.Exists(dest) || File.Exists(dest))
            return $"目标位置已存在同名项: {dest}";
        return null;
    }

    private static void CopyDirectory(string source, string dest, CancellationToken ct)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(dir.Replace(source, dest));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, file.Replace(source, dest), overwrite: false);
        }
    }

    private static long DirSize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    private static int CountFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();

    private static string UniqueBackupPath(string source)
    {
        var candidate = source + BackupSuffix;
        if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        return $"{source}{BackupSuffix}-{DateTime.Now:yyyyMMddHHmmss}";
    }

    private static void TryRestore(string backup, string source)
    {
        try { if (Directory.Exists(backup) && !Directory.Exists(source)) Directory.Move(backup, source); }
        catch { /* 尽力还原; 失败时备份仍在, 数据未丢 */ }
    }

    private ActionLog BuildLog(string source, string dest) => new(
        Id: 0, FileId: null, TargetPath: source, Action: ActionType.MigrateToOtherDrive,
        BeforeState: null, RecycleBinLocation: dest, Recoverable: true, Operator: Operator.User,
        Result: ActionResult.Success, RejectReason: null, AppVersion: _appVersion, Timestamp: DateTime.UtcNow);

    private static MigrationResult Failed(string message, string? dest, long bytes) =>
        new(MigrationOutcome.Failed, message, dest, null, bytes);

    private static string Human(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{s:0.##} {u[i]}";
    }
}
