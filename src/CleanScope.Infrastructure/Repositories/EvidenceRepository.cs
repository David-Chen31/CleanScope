using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>evidence 仓储。IsFact 区分事实/推测 (安全§9)。</summary>
public sealed class EvidenceRepository : IEvidenceRepository
{
    private readonly SqliteConnectionProvider _p;
    public EvidenceRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task AddRangeAsync(IEnumerable<Evidence> evidences, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO evidence(file_id,kind,value,source,is_fact,weight,created_at)
            VALUES($fid,$kind,$value,$src,$fact,$weight,$created);
            """;
        foreach (var e in evidences)
        {
            cmd.Parameters.Clear();
            cmd.Bind("$fid", e.FileId);
            cmd.Bind("$kind", e.Kind.ToString());
            cmd.Bind("$value", e.Value);
            cmd.Bind("$src", SqlValue.N(e.Source));
            cmd.Bind("$fact", SqlValue.B(e.IsFact));
            cmd.Bind("$weight", SqlValue.N(e.Weight));
            cmd.Bind("$created", SqlValue.Iso(e.CreatedAt));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<Evidence>> GetByFileAsync(long fileId, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM evidence WHERE file_id=$f ORDER BY id;";
        cmd.Bind("$f", fileId);
        var list = new List<Evidence>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Evidence(
                r.Int64("id"), r.Int64("file_id"), r.EnumOf<EvidenceKind>("kind"),
                r.Str("value"), r.StrN("source"), r.Bool("is_fact"),
                r.DoubleN("weight"), r.Date("created_at")));
        }
        return list;
    }
}
