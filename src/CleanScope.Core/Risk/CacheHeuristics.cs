namespace CleanScope.Core.Risk;

/// <summary>
/// 可重建缓存/临时目录的名称启发 (P2)。在**任意深度**按目录名识别"删了会自动重建、不丢用户数据"的目录,
/// 让深埋在各 app 里的缓存 (Chromium/Electron 的 Cache/GPUCache/Service Worker、各类 temp/log/dump 等) 显形为可清理。
///
/// 保守原则: 只认**明确可重建**的名字; 含糊或可能是真数据的 (如 Data/Storage/Profile/Plugins) 一律不认,
/// 避免误标可删 (即便回收站可恢复, 也不该侵蚀信任)。
/// </summary>
public static class CacheHeuristics
{
    // 不含 "cache" 字样、但明确可重建的目录名 (含 "cache" 的由下方 Contains 兜底, 无需在此罗列)。
    private static readonly HashSet<string> RebuildableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "temp", "tmp", "logs", "log",
        "crashpad", "crashdumps", "crashes", "dumps", "minidumps", "minidump",
        "blob_storage", "service worker", "serviceworker",
        "thumbnails", "thumbcache",
        "gpucache", "shadercache", "grshadercache", "dawncache",
        "code cache", "codecache",
    };

    /// <summary>目录叶子名是否表明为可重建缓存/临时/日志/转储。</summary>
    public static bool IsRebuildableCacheDir(string? leaf) =>
        !string.IsNullOrWhiteSpace(leaf) &&
        (leaf.Contains("cache", StringComparison.OrdinalIgnoreCase) || RebuildableNames.Contains(leaf.Trim()));
}
