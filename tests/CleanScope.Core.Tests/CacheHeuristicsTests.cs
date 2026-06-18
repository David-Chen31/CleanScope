using CleanScope.Core.Risk;

namespace CleanScope.Core.Tests;

// P2: 任意深度的可重建缓存/临时目录识别 —— 含 "cache" 或已知可重建名 → 可清理; 真数据名 → 否。
public sealed class CacheHeuristicsTests
{
    [Theory]
    [InlineData("Cache")]
    [InlineData("GPUCache")]
    [InlineData("Code Cache")]
    [InlineData("CacheStorage")]
    [InlineData("Service Worker")]
    [InlineData("blob_storage")]
    [InlineData("ShaderCache")]
    [InlineData("logs")]
    [InlineData("Temp")]
    [InlineData("crashpad")]
    [InlineData("minidumps")]
    public void Recognizes_rebuildable_dirs(string leaf)
        => Assert.True(CacheHeuristics.IsRebuildableCacheDir(leaf));

    [Theory] // 真数据 / 含糊 → 不认 (避免误标可删)
    [InlineData("Data")]
    [InlineData("Plugins")]
    [InlineData("Profile")]
    [InlineData("User")]
    [InlineData("Storage")]
    [InlineData("Documents")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_real_data_dirs(string? leaf)
        => Assert.False(CacheHeuristics.IsRebuildableCacheDir(leaf));
}
