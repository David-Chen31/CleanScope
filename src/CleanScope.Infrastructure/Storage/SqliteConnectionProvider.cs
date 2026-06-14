using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Storage;

/// <summary>
/// SQLite 连接提供者 (Infrastructure 内部)。供 SqliteStorage 与各仓储取已开连接。
/// - 文件库: 每次返回新连接 (连接池复用), 首次确保目录存在。
/// - 内存库 (Mode=Memory): 持有一个 keep-alive 连接, 防止内存库被回收 (供测试)。
/// 每个连接打开后启用外键 (PRAGMA foreign_keys=ON)。
/// </summary>
public sealed class SqliteConnectionProvider : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAlive;

    public SqliteConnectionProvider(string connectionString)
    {
        _connectionString = connectionString;
        var b = new SqliteConnectionStringBuilder(connectionString);

        if (b.Mode == SqliteOpenMode.Memory)
        {
            // 内存库: 保持一个常开连接, 否则库随最后一个连接关闭而消失。
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
        }
        else if (!string.IsNullOrWhiteSpace(b.DataSource))
        {
            // 文件库: 确保父目录存在。
            var dir = Path.GetDirectoryName(Path.GetFullPath(b.DataSource));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }

    /// <summary>打开一个新连接并启用外键。调用方负责释放。</summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        await cmd.ExecuteNonQueryAsync(ct);
        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (_keepAlive is not null)
            await _keepAlive.DisposeAsync();
    }
}
