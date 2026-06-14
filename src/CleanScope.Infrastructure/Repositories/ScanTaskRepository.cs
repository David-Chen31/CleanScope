using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>scan_task 仓储 (手写 SQL)。</summary>
public sealed class ScanTaskRepository : IScanTaskRepository
{
    private readonly SqliteConnectionProvider _p;
    public ScanTaskRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task<long> AddAsync(ScanTask task, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scan_task(target_path,mode,status,started_at,finished_at,total_size,file_count,app_version)
            VALUES($tp,$mode,$status,$sa,$fa,$ts,$fc,$ver);
            """;
        cmd.Bind("$tp", task.TargetPath);
        cmd.Bind("$mode", task.Mode.ToString());
        cmd.Bind("$status", task.Status.ToString());
        cmd.Bind("$sa", SqlValue.Iso(task.StartedAt));
        cmd.Bind("$fa", SqlValue.IsoN(task.FinishedAt));
        cmd.Bind("$ts", SqlValue.N(task.TotalSize));
        cmd.Bind("$fc", SqlValue.N(task.FileCount));
        cmd.Bind("$ver", task.AppVersion);
        await cmd.ExecuteNonQueryAsync(ct);

        cmd.CommandText = "SELECT last_insert_rowid();";
        cmd.Parameters.Clear();
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task UpdateAsync(ScanTask task, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE scan_task SET target_path=$tp,mode=$mode,status=$status,started_at=$sa,
                   finished_at=$fa,total_size=$ts,file_count=$fc,app_version=$ver
            WHERE id=$id;
            """;
        cmd.Bind("$tp", task.TargetPath);
        cmd.Bind("$mode", task.Mode.ToString());
        cmd.Bind("$status", task.Status.ToString());
        cmd.Bind("$sa", SqlValue.Iso(task.StartedAt));
        cmd.Bind("$fa", SqlValue.IsoN(task.FinishedAt));
        cmd.Bind("$ts", SqlValue.N(task.TotalSize));
        cmd.Bind("$fc", SqlValue.N(task.FileCount));
        cmd.Bind("$ver", task.AppVersion);
        cmd.Bind("$id", task.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ScanTask?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM scan_task WHERE id=$id;";
        cmd.Bind("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ScanTask(
            r.Int64("id"), r.Str("target_path"), r.EnumOf<ScanMode>("mode"),
            r.EnumOf<ScanStatus>("status"), r.Date("started_at"), r.DateN("finished_at"),
            r.Int64N("total_size"), r.Int64N("file_count"), r.Str("app_version"));
    }
}
