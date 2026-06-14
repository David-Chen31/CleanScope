using Microsoft.Data.Sqlite;

namespace CleanScope.Infrastructure.Storage;

/// <summary>数据库位置与连接串帮助。默认库位于用户数据目录, 不入 Git 仓库 (PR-1/4)。</summary>
public static class CleanScopeDb
{
    /// <summary>默认库路径: %LocalAppData%\CleanScope\cleanscope.db。</summary>
    public static string DefaultDbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanScope", "cleanscope.db");

    /// <summary>文件库连接串。</summary>
    public static string FileConnectionString(string dbPath) =>
        new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

    /// <summary>默认文件库连接串。</summary>
    public static string DefaultConnectionString() => FileConnectionString(DefaultDbPath);

    /// <summary>共享内存库连接串 (供测试; 需 keep-alive 连接维持)。</summary>
    public static string SharedMemoryConnectionString(string name) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();
}
