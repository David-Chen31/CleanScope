namespace CleanScope.Domain.Abstractions;

// 跨盘迁移契约 (P0)。实现在 Infrastructure (net8.0-windows)。
// 红线: 迁移**绝不永久删除**原数据 —— 复制到目标盘并校验通过后, 把原目录就地改名留作备份,
// 再在原位创建目录联接; 失败则回滚。释放原盘空间由用户在确认无误后, 经回收站删除该备份完成。

/// <summary>
/// 目录迁移器: 把一个目录搬到其他磁盘, 并在原位建目录联接 (junction), 对应用透明。
/// 仅迁移用户 AppData / 用户级程序目录下足够深的子目录 (保守白名单); 系统/容器/过浅路径一律拒。
/// </summary>
public interface IDirectoryMigrator
{
    Task<MigrationResult> MigrateAsync(MigrationRequest request, CancellationToken ct = default);
}

/// <summary>
/// 目录联接 (junction) 创建器。junction 不需管理员, 对应用透明 (程序以为文件仍在原处)。
/// 实现绝无删除调用; 仅创建 link → target 的重解析点。
/// </summary>
public interface IJunctionCreator
{
    /// <summary>在 <paramref name="linkPath"/> (须不存在) 创建指向 <paramref name="targetDir"/> 的目录联接; 失败抛异常。</summary>
    void Create(string linkPath, string targetDir);
}
