using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Infrastructure.Repositories;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Tests;

// T1.2: 仓储 CRUD + JSON 列往返测试 (闭环必需的 5 个仓储)。
public class RepositoriesTests
{
    private static readonly DateTime Utc = new(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc);

    private static SqliteConnectionProvider NewProvider() =>
        new(CleanScopeDb.SharedMemoryConnectionString("t_" + Guid.NewGuid().ToString("N")));

    // 建库 + 一个 scan_task + 一个 file_node, 满足后续 FK。
    private static async Task<(SqliteConnectionProvider provider, long taskId, long fileId)> SetupAsync()
    {
        var provider = NewProvider();
        await new SqliteStorage(provider).InitializeAsync();
        var taskId = await new ScanTaskRepository(provider).AddAsync(SampleTask());
        var fileId = await new FileNodeRepository(provider).AddAsync(SampleNode(taskId, "x", 100));
        return (provider, taskId, fileId);
    }

    private static ScanTask SampleTask() =>
        new(0, @"C:\", ScanMode.Normal, ScanStatus.Running, Utc, null, null, null, "0.1.0");

    private static FileNode SampleNode(long taskId, string name, long size) =>
        new(0, taskId, null, @"C:\" + name, @"C:\" + name, name, true, false, size,
            NodeType.Directory, Utc, Utc, AccessState.Accessible, PreliminaryClass.System, Utc);

    [Fact]
    public async Task ScanTask_add_get_update_roundtrip()
    {
        var (provider, taskId, _) = await SetupAsync();
        await using var _d = provider;
        var repo = new ScanTaskRepository(provider);

        var got = await repo.GetAsync(taskId);
        Assert.NotNull(got);
        Assert.Equal(@"C:\", got!.TargetPath);
        Assert.Equal(ScanStatus.Running, got.Status);
        Assert.Equal(Utc, got.StartedAt);

        await repo.UpdateAsync(got with { Status = ScanStatus.Completed, FileCount = 42 });
        var updated = await repo.GetAsync(taskId);
        Assert.Equal(ScanStatus.Completed, updated!.Status);
        Assert.Equal(42, updated.FileCount);
    }

    [Fact]
    public async Task FileNode_addrange_and_top_by_size_orders_desc()
    {
        var (provider, taskId, _) = await SetupAsync();
        await using var _d = provider;
        var repo = new FileNodeRepository(provider);

        await repo.AddRangeAsync(new[]
        {
            SampleNode(taskId, "big", 5000),
            SampleNode(taskId, "small", 200),
        });

        var top = await repo.GetTopBySizeAsync(taskId, 2);
        Assert.Equal(2, top.Count);
        Assert.Equal(5000, top[0].Size);              // 降序
        Assert.True(top[0].Size >= top[1].Size);
        Assert.Equal(NodeType.Directory, top[0].NodeType); // 枚举往返
        Assert.Equal(PreliminaryClass.System, top[0].PreliminaryClass);
    }

    [Fact]
    public async Task Evidence_addrange_and_isfact_roundtrip()
    {
        var (provider, _, fileId) = await SetupAsync();
        await using var _d = provider;
        var repo = new EvidenceRepository(provider);

        await repo.AddRangeAsync(new[]
        {
            new Evidence(0, fileId, EvidenceKind.PathRule, "路径在 Installer", "rule", true, 0.9, Utc),
            new Evidence(0, fileId, EvidenceKind.AiInference, "可能是缓存", "ai", false, 0.3, Utc),
        });

        var list = await repo.GetByFileAsync(fileId);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsFact);                  // 事实
        Assert.False(list[1].IsFact);                 // AI 推测
        Assert.Equal(EvidenceKind.AiInference, list[1].Kind);
    }

    [Fact]
    public async Task RuleMatch_add_and_get_roundtrip()
    {
        var (provider, _, fileId) = await SetupAsync();
        await using var _d = provider;
        var repo = new RuleMatchRepository(provider);

        await repo.AddAsync(new RuleMatch(0, fileId, "win-installer-cache", "Installer Cache",
            RiskLevel.D, false, true, "勿直删", 0.95, 100, true));

        var list = await repo.GetByFileAsync(fileId);
        var m = Assert.Single(list);
        Assert.Equal(RiskLevel.D, m.RiskLevel);
        Assert.True(m.IsSystemCritical);
        Assert.Equal(100, m.Priority);
        Assert.True(m.Authoritative);
    }

    [Fact] // Upsert + JSON 列 (factors / evidence_chain) 往返
    public async Task Risk_upsert_inserts_then_updates_with_json_roundtrip()
    {
        var (provider, _, fileId) = await SetupAsync();
        await using var _d = provider;
        var repo = new RiskRepository(provider);

        await repo.UpsertAsync(new RiskAssessment(0, fileId, RiskLevel.D, 78,
            new[] { "命中黑名单" }, new[] { 1L, 2L, 3L }, false, 0.85, Utc));

        var got = await repo.GetByFileAsync(fileId);
        Assert.NotNull(got);
        Assert.Equal(RiskLevel.D, got!.Level);
        Assert.Equal(new[] { "命中黑名单" }, got.Factors);
        Assert.Equal(new[] { 1L, 2L, 3L }, got.EvidenceChain);   // JSON 列往返

        // 再次 Upsert 同 file_id -> 更新而非新增 (file_id UNIQUE)
        await repo.UpsertAsync(got with { Level = RiskLevel.C, Score = 40, EvidenceChain = new[] { 9L } });
        var updated = await repo.GetByFileAsync(fileId);
        Assert.Equal(RiskLevel.C, updated!.Level);
        Assert.Equal(40, updated.Score);
        Assert.Equal(new[] { 9L }, updated.EvidenceChain);
    }
}
