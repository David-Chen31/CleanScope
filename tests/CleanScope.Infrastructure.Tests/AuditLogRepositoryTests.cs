using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Infrastructure.Repositories;
using CleanScope.Infrastructure.Storage;

namespace CleanScope.Infrastructure.Tests;

// T4.4: AuditLogRepository 往返 (action_log, 仅本地审计)。
public sealed class AuditLogRepositoryTests
{
    private static readonly DateTime Utc = new(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);

    private static SqliteConnectionProvider NewProvider() =>
        new(CleanScopeDb.SharedMemoryConnectionString("audit_" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Add_and_get_recent_roundtrip()
    {
        var provider = NewProvider();
        await using var _d = provider;
        await new SqliteStorage(provider).InitializeAsync();
        var repo = new AuditLogRepository(provider);

        // file_id 置 null (避免 FK); 记录一次被拒删除的审计。
        await repo.AddAsync(new ActionLog(0, null, @"C:\Windows\System32", ActionType.MoveToRecycleBin,
            null, null, false, Operator.User, ActionResult.Rejected, "命中黑名单", "0.1.0", Utc));
        await repo.AddAsync(new ActionLog(0, null, @"C:\some\dir", ActionType.OpenDir,
            null, null, true, Operator.User, ActionResult.Success, null, "0.1.0", Utc.AddSeconds(1)));

        var recent = await repo.GetRecentAsync(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal(ActionType.OpenDir, recent[0].Action);          // 最近优先 (timestamp DESC)
        var rejected = recent.Single(l => l.Result == ActionResult.Rejected);
        Assert.Equal("命中黑名单", rejected.RejectReason);
        Assert.Equal(ActionType.MoveToRecycleBin, rejected.Action);
        Assert.False(rejected.Recoverable);
    }
}
