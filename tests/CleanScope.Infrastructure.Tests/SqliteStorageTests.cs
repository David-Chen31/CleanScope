using CleanScope.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Tests;

// T1.1: SQLite 建库与约束测试。用共享内存库 (keep-alive), 不落盘。
public class SqliteStorageTests
{
    private static SqliteConnectionProvider NewProvider() =>
        new(CleanScopeDb.SharedMemoryConnectionString("t_" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Initialize_creates_all_11_tables()
    {
        await using var provider = NewProvider();
        var storage = new SqliteStorage(provider);
        await storage.InitializeAsync();

        await using var conn = await provider.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        var found = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) found.Add(r.GetString(0));

        foreach (var table in SqliteSchema.TableNames)
            Assert.Contains(table, found);
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        await using var provider = NewProvider();
        var storage = new SqliteStorage(provider);
        await storage.InitializeAsync();
        await storage.InitializeAsync();   // 再次执行不应抛 (IF NOT EXISTS)
    }

    [Fact] // SR-5: 空证据链应被 CHECK 拒绝, 非空应通过
    public async Task EvidenceChain_nonempty_check_is_enforced()
    {
        await using var provider = NewProvider();
        var storage = new SqliteStorage(provider);
        await storage.InitializeAsync();
        await SeedFileNodeAsync(provider);

        await Assert.ThrowsAsync<SqliteException>(() =>
            InsertRiskAsync(provider, evidenceChain: "[]"));     // length 2 -> 违反 CHECK

        await InsertRiskAsync(provider, evidenceChain: "[1,2]"); // 非空 -> 成功
    }

    [Fact] // FK: 无父 file_node 时插入风险应失败 (PRAGMA foreign_keys=ON 生效)
    public async Task ForeignKey_is_enforced()
    {
        await using var provider = NewProvider();
        var storage = new SqliteStorage(provider);
        await storage.InitializeAsync();

        await Assert.ThrowsAsync<SqliteException>(() =>
            InsertRiskAsync(provider, evidenceChain: "[1]"));    // file_id=1 不存在
    }

    private static async Task SeedFileNodeAsync(SqliteConnectionProvider provider)
    {
        await using var conn = await provider.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scan_task(id,target_path,mode,status,started_at,app_version)
            VALUES(1,'C:\','Normal','Completed','2026-06-14T00:00:00Z','0.1.0');
            INSERT INTO file_node(id,task_id,path,name,is_directory,size,access_state,created_at)
            VALUES(1,1,'C:\x','x',1,100,'Accessible','2026-06-14T00:00:00Z');
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertRiskAsync(SqliteConnectionProvider provider, string evidenceChain)
    {
        await using var conn = await provider.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO risk_assessment(file_id,level,score,evidence_chain,can_delete_directly,created_at)
            VALUES(1,'D',78,$ec,0,'2026-06-14T00:00:00Z');
            """;
        cmd.Parameters.AddWithValue("$ec", evidenceChain);
        await cmd.ExecuteNonQueryAsync();
    }
}
