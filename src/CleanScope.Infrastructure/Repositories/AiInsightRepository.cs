using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>
/// ai_insight 仓储 (F): 按路径缓存"AI 识别"推测结果, 跨会话复用免重复花 token。
/// 整表仅本地; 仅推测/展示, 绝不参与风险或删除判定 (与红线无关, 纯读写)。
/// </summary>
public sealed class AiInsightRepository : IAiInsightRepository
{
    private readonly SqliteConnectionProvider _p;
    public AiInsightRepository(SqliteConnectionProvider provider) => _p = provider;

    public async Task UpsertAsync(AiInsight insight, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ai_insight(path,origin,purpose,created_at)
            VALUES($path,$origin,$purpose,$ts)
            ON CONFLICT(path) DO UPDATE SET origin=$origin, purpose=$purpose, created_at=$ts;
            """;
        cmd.Bind("$path", insight.Path);
        cmd.Bind("$origin", SqlValue.N(insight.Origin));
        cmd.Bind("$purpose", SqlValue.N(insight.Purpose));
        cmd.Bind("$ts", SqlValue.Iso(insight.CreatedAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AiInsight>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT path,origin,purpose,created_at FROM ai_insight;";
        var list = new List<AiInsight>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new AiInsight(r.Str("path"), r.StrN("origin"), r.StrN("purpose"), r.Date("created_at")));
        return list;
    }
}
