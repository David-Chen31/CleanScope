using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Infrastructure.Migration;

namespace CleanScope.Infrastructure.Tests;

// P0 跨盘迁移编排: 复制 → 校验 → 先写审计 → 原目录就地改名留备份 → 建联接; 失败回滚。全程无永久删除。
// 受控临时目录上验证 (放宽白名单/跨盘开关); 联接与审计用 fake。临时目录由测试自行清理。
public sealed class DirectoryMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cs-migrate-" + Guid.NewGuid().ToString("N"));
    private static readonly MigrationOptions Relaxed = new(EnforceUserDataScope: false, EnforceCrossDrive: false);

    private string MakeSourceWithFiles(string name, int files = 3)
    {
        var src = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        for (var i = 0; i < files; i++)
            File.WriteAllText(Path.Combine(src, i == 0 ? "a.txt" : $"sub/f{i}.bin"), new string('x', 100 * (i + 1)));
        return src;
    }

    private string TargetRoot()
    {
        var dst = Path.Combine(_root, "dst");
        Directory.CreateDirectory(dst);
        return dst;
    }

    [Fact]
    public async Task Migrate_copies_verifies_renames_aside_and_creates_junction()
    {
        var src = MakeSourceWithFiles("Postman");
        var junction = new RecordingJunction();
        var audit = new RecordingAudit();
        var migrator = new DirectoryMigrator(junction, audit, options: Relaxed);

        var result = await migrator.MigrateAsync(new MigrationRequest(src, TargetRoot()));

        Assert.Equal(MigrationOutcome.Success, result.Outcome);
        var dest = Path.Combine(_root, "dst", "Postman");
        Assert.True(Directory.Exists(dest));
        Assert.Equal(result.NewLocation, dest);
        // 原目录留作备份 (.cleanscope-bak), 数据未删。
        Assert.NotNull(result.BackupPath);
        Assert.True(Directory.Exists(result.BackupPath!));
        Assert.EndsWith(".cleanscope-bak", result.BackupPath);
        // 联接在原位创建, 指向目标盘副本。
        Assert.Equal((src, dest), junction.Called);
        // 先写审计 (恰一条 MigrateToOtherDrive, 可恢复)。
        Assert.Single(audit.Logs);
        Assert.Equal(ActionType.MigrateToOtherDrive, audit.Logs[0].Action);
        Assert.True(audit.Logs[0].Recoverable);
        // 副本字节数与源一致。
        Assert.Equal(DirSize(result.BackupPath!), DirSize(dest));
    }

    [Fact]
    public async Task Junction_failure_rolls_back_original()
    {
        var src = MakeSourceWithFiles("Notion");
        var junction = new RecordingJunction { Throw = true };
        var migrator = new DirectoryMigrator(junction, new RecordingAudit(), options: Relaxed);

        var result = await migrator.MigrateAsync(new MigrationRequest(src, TargetRoot()));

        Assert.Equal(MigrationOutcome.Failed, result.Outcome);
        // 回滚: 原目录被还原回原路径 (数据未丢)。
        Assert.True(Directory.Exists(src));
        Assert.False(Directory.Exists(src + ".cleanscope-bak"));
    }

    [Fact]
    public async Task Audit_failure_aborts_without_touching_original()
    {
        var src = MakeSourceWithFiles("App");
        var before = DirSize(src);
        var migrator = new DirectoryMigrator(new RecordingJunction(), new RecordingAudit { Throw = true }, options: Relaxed);

        var result = await migrator.MigrateAsync(new MigrationRequest(src, TargetRoot()));

        Assert.Equal(MigrationOutcome.Failed, result.Outcome);
        Assert.True(Directory.Exists(src));                 // 原目录未改名
        Assert.Equal(before, DirSize(src));
    }

    [Fact]
    public async Task Rejects_nonexistent_source()
    {
        var migrator = new DirectoryMigrator(new RecordingJunction(), new RecordingAudit(), options: Relaxed);
        var result = await migrator.MigrateAsync(new MigrationRequest(Path.Combine(_root, "nope"), TargetRoot()));
        Assert.Equal(MigrationOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task Rejects_when_destination_already_exists()
    {
        var src = MakeSourceWithFiles("Dup");
        var dst = TargetRoot();
        Directory.CreateDirectory(Path.Combine(dst, "Dup"));   // 目标已存在同名
        var migrator = new DirectoryMigrator(new RecordingJunction(), new RecordingAudit(), options: Relaxed);

        var result = await migrator.MigrateAsync(new MigrationRequest(src, dst));
        Assert.Equal(MigrationOutcome.Rejected, result.Outcome);
    }

    [Fact] // 生产策略 (默认开): 强制跨盘 —— 源与目标同盘时拒绝 (临时目录恰在 AppData 下, 故白名单放行, 卡在跨盘)。
    public async Task Default_policy_rejects_same_drive_migration()
    {
        var src = MakeSourceWithFiles("Whatever");
        var migrator = new DirectoryMigrator(new RecordingJunction(), new RecordingAudit());  // 默认: 强制白名单 + 跨盘
        var result = await migrator.MigrateAsync(new MigrationRequest(src, TargetRoot()));
        Assert.Equal(MigrationOutcome.Rejected, result.Outcome);
        Assert.Contains("磁盘", result.Message);
    }

    private static long DirSize(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class RecordingJunction : IJunctionCreator
    {
        public (string link, string target)? Called;
        public bool Throw;
        public void Create(string linkPath, string targetDir)
        {
            Called = (linkPath, targetDir);
            if (Throw) throw new IOException("junction boom");
            Directory.CreateDirectory(linkPath);   // 占位: 模拟联接已在原位 (真实环境为重解析点)
        }
    }

    private sealed class RecordingAudit : IAuditLogRepository
    {
        public List<ActionLog> Logs { get; } = new();
        public bool Throw;
        public Task AddAsync(ActionLog log, CancellationToken ct = default)
        {
            if (Throw) throw new IOException("audit down");
            Logs.Add(log);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ActionLog>> GetRecentAsync(int count, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ActionLog>)Logs);
    }
}
