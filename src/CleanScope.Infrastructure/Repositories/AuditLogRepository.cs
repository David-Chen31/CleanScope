using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>
/// action_log 仓储。审计是安全/合规资产, **整表仅本地** (数据模型§4.10), 绝不上云。
/// 配合"先写后执行"(SR-9): 执行器在操作前调用 <see cref="AddAsync"/>; 写失败即中止操作。
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly SqliteConnectionProvider _p;
    public AuditLogRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task AddAsync(ActionLog log, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO action_log(file_id,target_path,action,before_state,recycle_bin_location,
                   recoverable,operator,result,reject_reason,app_version,timestamp)
            VALUES($fid,$tp,$action,$before,$recycle,$recoverable,$operator,$result,$reason,$ver,$ts);
            """;
        cmd.Bind("$fid", SqlValue.N(log.FileId));
        cmd.Bind("$tp", SqlValue.N(log.TargetPath));
        cmd.Bind("$action", log.Action.ToString());
        cmd.Bind("$before", SqlValue.N(log.BeforeState));
        cmd.Bind("$recycle", SqlValue.N(log.RecycleBinLocation));
        cmd.Bind("$recoverable", SqlValue.B(log.Recoverable));
        cmd.Bind("$operator", log.Operator.ToString());
        cmd.Bind("$result", log.Result.ToString());
        cmd.Bind("$reason", SqlValue.N(log.RejectReason));
        cmd.Bind("$ver", log.AppVersion);
        cmd.Bind("$ts", SqlValue.Iso(log.Timestamp));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ActionLog>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM action_log ORDER BY timestamp DESC, id DESC LIMIT $n;";
        cmd.Bind("$n", count);
        var list = new List<ActionLog>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ActionLog(
                r.Int64("id"), r.Int64N("file_id"), r.StrN("target_path"),
                r.EnumOf<ActionType>("action"), r.StrN("before_state"), r.StrN("recycle_bin_location"),
                r.Bool("recoverable"), r.EnumOf<Operator>("operator"), r.EnumOf<ActionResult>("result"),
                r.StrN("reject_reason"), r.Str("app_version"), r.Date("timestamp")));
        }
        return list;
    }
}
