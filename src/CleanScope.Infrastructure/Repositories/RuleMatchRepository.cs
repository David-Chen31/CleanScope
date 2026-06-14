using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>rule_match 仓储。规则引擎权威输出。</summary>
public sealed class RuleMatchRepository : IRuleMatchRepository
{
    private readonly SqliteConnectionProvider _p;
    public RuleMatchRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task AddAsync(RuleMatch m, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO rule_match(file_id,rule_id,category,risk_level,direct_delete,is_system_critical,
                   recommended_action,confidence,priority,authoritative)
            VALUES($fid,$rid,$cat,$risk,$dd,$crit,$action,$conf,$prio,$auth);
            """;
        cmd.Bind("$fid", m.FileId);
        cmd.Bind("$rid", m.RuleId);
        cmd.Bind("$cat", SqlValue.N(m.Category));
        cmd.Bind("$risk", SqlValue.EnumN(m.RiskLevel));
        cmd.Bind("$dd", SqlValue.BN(m.DirectDelete));
        cmd.Bind("$crit", SqlValue.BN(m.IsSystemCritical));
        cmd.Bind("$action", SqlValue.N(m.RecommendedAction));
        cmd.Bind("$conf", SqlValue.N(m.Confidence));
        cmd.Bind("$prio", SqlValue.N(m.Priority));
        cmd.Bind("$auth", SqlValue.B(m.Authoritative));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RuleMatch>> GetByFileAsync(long fileId, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM rule_match WHERE file_id=$f ORDER BY priority DESC;";
        cmd.Bind("$f", fileId);
        var list = new List<RuleMatch>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RuleMatch(
                r.Int64("id"), r.Int64("file_id"), r.Str("rule_id"), r.StrN("category"),
                r.EnumOfN<RiskLevel>("risk_level"), r.BoolN("direct_delete"), r.BoolN("is_system_critical"),
                r.StrN("recommended_action"), r.DoubleN("confidence"),
                r.Int64N("priority") is { } p ? (int)p : null, r.Bool("authoritative")));
        }
        return list;
    }
}
