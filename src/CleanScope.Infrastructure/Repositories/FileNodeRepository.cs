using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Repositories;

/// <summary>file_node 仓储 (手写 SQL)。批量插入用事务。</summary>
public sealed class FileNodeRepository : IFileNodeRepository
{
    private readonly SqliteConnectionProvider _p;
    public FileNodeRepository(SqliteConnectionProvider provider) => _p = provider;

    private const string InsertSql = """
        INSERT INTO file_node(task_id,parent_id,path,real_path,name,is_directory,is_reparse_point,
               size,node_type,mtime,atime,access_state,preliminary_class,created_at)
        VALUES($task,$parent,$path,$real,$name,$isdir,$reparse,$size,$ntype,$mtime,$atime,$astate,$pclass,$created);
        """;

    private static void BindNode(SqliteCommand cmd, FileNode n)
    {
        cmd.Bind("$task", n.TaskId);
        cmd.Bind("$parent", SqlValue.N(n.ParentId));
        cmd.Bind("$path", n.Path);
        cmd.Bind("$real", SqlValue.N(n.RealPath));
        cmd.Bind("$name", n.Name);
        cmd.Bind("$isdir", SqlValue.B(n.IsDirectory));
        cmd.Bind("$reparse", SqlValue.B(n.IsReparsePoint));
        cmd.Bind("$size", n.Size);
        cmd.Bind("$ntype", SqlValue.EnumN(n.NodeType));
        cmd.Bind("$mtime", SqlValue.IsoN(n.Mtime));
        cmd.Bind("$atime", SqlValue.IsoN(n.Atime));
        cmd.Bind("$astate", n.AccessState.ToString());
        cmd.Bind("$pclass", SqlValue.EnumN(n.PreliminaryClass));
        cmd.Bind("$created", SqlValue.Iso(n.CreatedAt));
    }

    public async Task<long> AddAsync(FileNode node, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = InsertSql;
        BindNode(cmd, node);
        await cmd.ExecuteNonQueryAsync(ct);
        cmd.CommandText = "SELECT last_insert_rowid();";
        cmd.Parameters.Clear();
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task AddRangeAsync(IEnumerable<FileNode> nodes, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertSql;
        foreach (var n in nodes)
        {
            cmd.Parameters.Clear();
            BindNode(cmd, n);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<FileNode>> GetTopBySizeAsync(long taskId, int topN, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM file_node WHERE task_id=$t ORDER BY size DESC LIMIT $n;";
        cmd.Bind("$t", taskId);
        cmd.Bind("$n", topN);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<FileNode>> GetChildrenAsync(long parentId, CancellationToken ct = default)
    {
        await using var conn = await _p.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM file_node WHERE parent_id=$p ORDER BY size DESC;";
        cmd.Bind("$p", parentId);
        return await ReadAllAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<FileNode>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<FileNode>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new FileNode(
                r.Int64("id"), r.Int64("task_id"), r.Int64N("parent_id"),
                r.Str("path"), r.StrN("real_path"), r.Str("name"),
                r.Bool("is_directory"), r.Bool("is_reparse_point"), r.Int64("size"),
                r.EnumOfN<NodeType>("node_type"), r.DateN("mtime"), r.DateN("atime"),
                r.EnumOf<AccessState>("access_state"), r.EnumOfN<PreliminaryClass>("preliminary_class"),
                r.Date("created_at")));
        }
        return list;
    }
}
