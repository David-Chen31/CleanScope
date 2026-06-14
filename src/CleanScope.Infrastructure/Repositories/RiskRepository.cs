using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>
/// risk_assessment 仓储。file_id 唯一 → UpsertAsync 用 ON CONFLICT 更新。
/// JSON 列: factors / evidence_chain。evidence_chain 非空由表 CHECK 兜底 (SR-5)。
/// </summary>
public sealed class RiskRepository : IRiskRepository
{
    private readonly SqliteConnectionProvider _p;
    public RiskRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task UpsertAsync(RiskAssessment a, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO risk_assessment(file_id,level,score,factors,evidence_chain,can_delete_directly,confidence,created_at)
            VALUES($fid,$level,$score,$factors,$ec,$cdd,$conf,$created)
            ON CONFLICT(file_id) DO UPDATE SET
              level=excluded.level, score=excluded.score, factors=excluded.factors,
              evidence_chain=excluded.evidence_chain, can_delete_directly=excluded.can_delete_directly,
              confidence=excluded.confidence, created_at=excluded.created_at;
            """;
        cmd.Bind("$fid", a.FileId);
        cmd.Bind("$level", a.Level.ToString());
        cmd.Bind("$score", a.Score);
        cmd.Bind("$factors", SqlValue.Json(a.Factors));
        cmd.Bind("$ec", SqlValue.Json(a.EvidenceChain));
        cmd.Bind("$cdd", SqlValue.B(a.CanDeleteDirectly));
        cmd.Bind("$conf", SqlValue.N(a.Confidence));
        cmd.Bind("$created", SqlValue.Iso(a.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<RiskAssessment?> GetByFileAsync(long fileId, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM risk_assessment WHERE file_id=$f;";
        cmd.Bind("$f", fileId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new RiskAssessment(
            r.Int64("id"), r.Int64("file_id"), r.EnumOf<RiskLevel>("level"), r.Int32("score"),
            r.JsonList<string>("factors"), r.JsonList<long>("evidence_chain"),
            r.Bool("can_delete_directly"), r.DoubleN("confidence"), r.Date("created_at"));
    }
}
