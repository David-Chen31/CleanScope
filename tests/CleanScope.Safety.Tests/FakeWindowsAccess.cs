using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Models;

namespace CleanScope.Safety.Tests;

// 可配置的 IWindowsAccess 测试替身 (Safety 测试共用)。
internal sealed class FakeWindowsAccess : IWindowsAccess
{
    public string? Occupier { get; set; }
    public Func<string, string>? RealPathOf { get; set; }

    public Task<FileMetadata?> ReadMetadataAsync(string path, CancellationToken ct = default)
        => Task.FromResult<FileMetadata?>(null);
    public IReadOnlyList<InstalledApp> GetInstalledApplications() => Array.Empty<InstalledApp>();
    public string? GetOccupyingProcessName(string path) => Occupier;
    public string ResolveRealPath(string path) => RealPathOf?.Invoke(path) ?? path;
}
