using CleanScope.Domain.Abstractions;
using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Storage;

/// <summary>
/// IStorage 的 SQLite 实现 (决议4: 核心层经接口使用, 不见 SQLite)。
/// 负责建库 (DDL) 与事务。仓储实现 (T1.2) 复用同一连接提供者。
/// </summary>
public sealed class SqliteStorage : IStorage, IAsyncDisposable
{
    private readonly SqliteConnectionProvider _provider;

    public SqliteStorage(SqliteConnectionProvider provider) => _provider = provider;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = await _provider.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqliteSchema.CreateScript;   // 批量执行全部 CREATE (幂等)
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IStorageTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var conn = await _provider.OpenAsync(ct);
        var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        return new SqliteStorageTransaction(conn, tx);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

/// <summary>IStorageTransaction 的 SQLite 实现。持有连接+事务, 释放时一并清理。</summary>
internal sealed class SqliteStorageTransaction : IStorageTransaction
{
    private readonly SqliteConnection _conn;
    private readonly SqliteTransaction _tx;

    public SqliteStorageTransaction(SqliteConnection conn, SqliteTransaction tx)
    {
        _conn = conn;
        _tx = tx;
    }

    public Task CommitAsync(CancellationToken ct = default) => _tx.CommitAsync(ct);
    public Task RollbackAsync(CancellationToken ct = default) => _tx.RollbackAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await _tx.DisposeAsync();
        await _conn.DisposeAsync();
    }
}
