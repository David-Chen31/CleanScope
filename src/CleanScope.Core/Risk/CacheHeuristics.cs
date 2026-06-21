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

    // 可重建的开发依赖/产物: 删后需"重新安装依赖或重新构建"(非自动重建)。
    // 只收**无歧义**的名字 —— 故意不含 build/dist/bin/obj/out/target 等常见词, 避免把用户自建同名文件夹误标可清理。
    private static readonly HashSet<string> DependencyOrBuildDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__",
        ".pytest_cache", ".mypy_cache", ".ruff_cache", ".tox",
    };

    /// <summary>目录叶子名是否表明为可重建缓存/临时/日志/转储, 或可重建的开发依赖/产物。</summary>
    public static bool IsRebuildableCacheDir(string? leaf) =>
        !string.IsNullOrWhiteSpace(leaf) &&
        (leaf.Contains("cache", StringComparison.OrdinalIgnoreCase)
         || RebuildableNames.Contains(leaf.Trim())
         || DependencyOrBuildDirs.Contains(leaf.Trim()));

    /// <summary>是否为"开发依赖/产物"(node_modules 等): 删后需重新安装/构建, 解释文案据此更诚实。</summary>
    public static bool IsDependencyDir(string? leaf) =>
        !string.IsNullOrWhiteSpace(leaf) && DependencyOrBuildDirs.Contains(leaf.Trim());
}
