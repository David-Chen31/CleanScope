using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Safety;

namespace CleanScope.Safety.Tests;

internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> OpenedFolders { get; } = new();
    public List<string> OpenedUris { get; } = new();
    public void OpenFolder(string path) => OpenedFolders.Add(path);
    public void OpenUri(string uri) => OpenedUris.Add(uri);
}

internal sealed class FakeAudit : IAuditLogRepository
{
    public List<ActionLog> Added { get; } = new();
    public bool ThrowOnAdd { get; set; }

    public Task AddAsync(ActionLog log, CancellationToken ct = default)
    {
        if (ThrowOnAdd) throw new InvalidOperationException("audit write failed");
        Added.Add(log);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActionLog>> GetRecentAsync(int count, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ActionLog>>(Added);
}

internal sealed class FakeIgnore : IIgnoreRepository
{
    public List<IgnoreEntry> Added { get; } = new();
    public Task AddAsync(IgnoreEntry entry, CancellationToken ct = default) { Added.Add(entry); return Task.CompletedTask; }
    public Task RemoveAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<IgnoreEntry>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IgnoreEntry>>(Added);
}
