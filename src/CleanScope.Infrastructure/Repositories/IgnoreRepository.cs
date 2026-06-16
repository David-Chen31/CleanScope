using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>
/// ignore_entry 仓储 (全局忽略名单, 跨任务持续生效; 数据模型§4.9)。
/// path_or_pattern 属 P1, reason 属 P2 机密 —— 整表仅本地, 绝不上云。
/// 忽略名单只影响"是否在列表中提示", 不触发任何删除 (与红线无关, 纯读写)。
/// </summary>
public sealed class IgnoreRepository : IIgnoreRepository
{
    private readonly SqliteConnectionProvider _p;
    public IgnoreRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task AddAsync(IgnoreEntry entry, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ignore_entry(path_or_pattern,match_type,reason,created_at)
            VALUES($p,$mt,$reason,$ts);
            """;
        cmd.Bind("$p", entry.PathOrPattern);
        cmd.Bind("$mt", entry.MatchType.ToString());
        cmd.Bind("$reason", SqlValue.N(entry.Reason));
        cmd.Bind("$ts", SqlValue.Iso(entry.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ignore_entry WHERE id = $id;";
        cmd.Bind("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<IgnoreEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ignore_entry ORDER BY created_at DESC, id DESC;";
        var list = new List<IgnoreEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new IgnoreEntry(
                r.Int64("id"), r.Str("path_or_pattern"),
                r.EnumOf<MatchType>("match_type"), r.StrN("reason"), r.Date("created_at")));
        }
        return list;
    }
}
