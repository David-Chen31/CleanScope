namespace CleanScope.Domain.Abstractions;

// 存储契约 (决议4: 核心层只依赖接口, 不依赖 SQLite)。仅签名, 无实现 (T0.5)。
// 实现由 Infrastructure 手写 SQL 完成 (不用 EF, 决议8)。

/// <summary>存储生命周期与事务 (建库/迁移/事务)。</summary>
public interface IStorage
{
    Task InitializeAsync(CancellationToken ct = default);   // 首次启动建库 / 迁移 (DDL)
    Task<IStorageTransaction> BeginTransactionAsync(CancellationToken ct = default);
}

public interface IStorageTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public interface IScanTaskRepository
{
    Task<long> AddAsync(ScanTask task, CancellationToken ct = default);
    Task UpdateAsync(ScanTask task, CancellationToken ct = default);
    Task<ScanTask?> GetAsync(long id, CancellationToken ct = default);
}

public interface IFileNodeRepository
{
    Task<long> AddAsync(FileNode node, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<FileNode> nodes, CancellationToken ct = default);
    Task<IReadOnlyList<FileNode>> GetTopBySizeAsync(long taskId, int topN, CancellationToken ct = default);
    Task<IReadOnlyList<FileNode>> GetChildrenAsync(long parentId, CancellationToken ct = default);
}

public interface IFileMetadataRepository
{
    Task UpsertAsync(FileMetadata metadata, CancellationToken ct = default);
    Task<FileMetadata?> GetAsync(long fileId, CancellationToken ct = default);
}

public interface IEvidenceRepository
{
    Task AddRangeAsync(IEnumerable<Evidence> evidences, CancellationToken ct = default);
    Task<IReadOnlyList<Evidence>> GetByFileAsync(long fileId, CancellationToken ct = default);
}

public interface IRuleMatchRepository
{
    Task AddAsync(RuleMatch match, CancellationToken ct = default);
    Task<IReadOnlyList<RuleMatch>> GetByFileAsync(long fileId, CancellationToken ct = default);
}

public interface IAttributionRepository
{
    Task AddRangeAsync(IEnumerable<AttributionCandidate> candidates, CancellationToken ct = default);
    Task<IReadOnlyList<AttributionCandidate>> GetByFileAsync(long fileId, CancellationToken ct = default);
}

public interface IRiskRepository
{
    // UpsertAsync 内部应保证 EvidenceChain 非空 (SR-5, 配合表 CHECK 约束)。
    Task UpsertAsync(RiskAssessment assessment, CancellationToken ct = default);
    Task<RiskAssessment?> GetByFileAsync(long fileId, CancellationToken ct = default);
}

public interface IAiExplanationRepository
{
    Task UpsertAsync(AiExplanation explanation, CancellationToken ct = default);
    Task<AiExplanation?> GetByFileAsync(long fileId, CancellationToken ct = default);
}

public interface IUserDecisionRepository
{
    Task AddAsync(UserDecision decision, CancellationToken ct = default);
    Task<IReadOnlyList<UserDecision>> GetByFileAsync(long fileId, CancellationToken ct = default);
}

public interface IIgnoreRepository
{
    Task AddAsync(IgnoreEntry entry, CancellationToken ct = default);
    Task RemoveAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<IgnoreEntry>> GetAllAsync(CancellationToken ct = default);
}

public interface IAuditLogRepository
{
    Task AddAsync(ActionLog log, CancellationToken ct = default);   // 先写后执行 (SR-9)
    Task<IReadOnlyList<ActionLog>> GetRecentAsync(int count, CancellationToken ct = default);
}
