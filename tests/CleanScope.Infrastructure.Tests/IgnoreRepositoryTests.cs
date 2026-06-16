using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Infrastructure.Repositories;
using CleanScope.Infrastructure.Storage;
using MatchType = CleanScope.Domain.Enums.MatchType;

namespace CleanScope.Infrastructure.Tests;

// T5.5: IgnoreRepository 往返 (ignore_entry, 全局忽略名单, 仅本地)。
public sealed class IgnoreRepositoryTests
{
    private static readonly DateTime Utc = new(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc);

    private static SqliteConnectionProvider NewProvider() =>
        new(CleanScopeDb.SharedMemoryConnectionString("ignore_" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task Add_get_remove_roundtrip()
    {
        var provider = NewProvider();
        await using var _d = provider;
        await new SqliteStorage(provider).InitializeAsync();
        var repo = new IgnoreRepository(provider);

        await repo.AddAsync(new IgnoreEntry(0, @"C:\KeepThis", MatchType.Exact, "用户保留", Utc));
        await repo.AddAsync(new IgnoreEntry(0, @"C:\Logs\*", MatchType.Glob, null, Utc.AddSeconds(1)));

        var all = await repo.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(@"C:\Logs\*", all[0].PathOrPattern);   // created_at DESC
        Assert.Equal(MatchType.Glob, all[0].MatchType);

        var exact = all.Single(e => e.MatchType == MatchType.Exact);
        Assert.Equal("用户保留", exact.Reason);

        await repo.RemoveAsync(exact.Id);
        var after = await repo.GetAllAsync();
        Assert.Single(after);
        Assert.DoesNotContain(after, e => e.Id == exact.Id);
    }
}
