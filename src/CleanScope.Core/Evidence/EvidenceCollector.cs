namespace CleanScope.Core.Evidences;

/// <summary>
/// 证据采集 (架构§3 / 安全§9)。汇集 元数据/签名/已安装应用归属/进程占用/扩展名 → <see cref="EvidenceBundle"/>。
///
/// 安全铁律: **采集器只产事实证据 (IsFact=true)** —— 这些是可观测的系统事实, 可驱动权威结论。
/// AI 推测 (IsFact=false / <see cref="EvidenceKind.AiInference"/>) 永远只来自 AI 管线, 绝不源于此 (§9)。
///
/// 经 <see cref="IWindowsAccess"/> 抽象取数 (Core 不碰具体实现)。已安装应用列表昂贵 → 惰性缓存一次复用。
/// </summary>
public sealed class EvidenceCollector : IEvidenceCollector
{
    private readonly IWindowsAccess _win;
    private readonly Lazy<IReadOnlyList<InstalledApp>> _installedApps;

    public EvidenceCollector(IWindowsAccess windowsAccess)
    {
        _win = windowsAccess;
        _installedApps = new Lazy<IReadOnlyList<InstalledApp>>(_win.GetInstalledApplications);
    }

    public Task<EvidenceBundle> CollectAsync(FileNode node, CancellationToken ct = default)
        => CollectCoreAsync(node, deep: true, ct);

    /// <summary>
    /// 轻量采集 (全盘目录树): 不做进程占用与 Authenticode (建树批量, 省昂贵 I/O), 但**保留 T3 目录二进制采样**
    /// (只读版本信息) —— 这正是让资源管理器里"D:\steam、D:\obsidian…"也能归属的关键。
    /// </summary>
    public Task<EvidenceBundle> CollectForTreeAsync(FileNode node, CancellationToken ct = default)
        => CollectCoreAsync(node, deep: false, ct);

    private async Task<EvidenceBundle> CollectCoreAsync(FileNode node, bool deep, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        var path = node.RealPath ?? node.Path;
        var now = DateTime.UtcNow;
        var list = new List<Evidence>();
        long id = 0;

        Evidence Fact(EvidenceKind kind, string value, string source, double weight) =>
            new(++id, node.Id, kind, value, source, IsFact: true, weight, now);

        void EmitMetadataFacts(FileMetadata m, string source)
        {
            if (HasVersionInfo(m))
                list.Add(Fact(EvidenceKind.Metadata,
                    $"product={m.ProductName}; company={m.CompanyName}; version={m.FileVersion}",
                    source, 0.7));
            if (m.IsSigned == true && !string.IsNullOrWhiteSpace(m.Signer))
                list.Add(Fact(EvidenceKind.Signature, $"signed by {m.Signer}", "authenticode", 0.9));
        }

        // 基础观测事实: 保证证据链非空 (SR-5), 即便无任何其它信号。
        list.Add(Fact(EvidenceKind.Metadata, path, "scan", 0.5));

        FileMetadata? metadata = null;

        if (!node.IsDirectory && deep)
        {
            var ext = Path.GetExtension(path);
            if (ext.Length > 0)
                list.Add(Fact(EvidenceKind.Extension, ext, "scan", 0.3));

            metadata = await _win.ReadMetadataAsync(path, ct);
            if (metadata is not null) EmitMetadataFacts(metadata, "version-info");

            // 进程占用 (强事实): 影响删除前置 (IR-2)。仅对文件、仅深度采集。
            var proc = _win.GetOccupyingProcessName(path);
            if (!string.IsNullOrWhiteSpace(proc))
            {
                list.Add(Fact(EvidenceKind.Process, $"in use by {proc}", "restart-manager", 0.95));
                metadata = WithOccupation(metadata, node.Id, path, proc!);
            }
        }

        // 已安装应用归属 (事实): 优先按安装目录前缀; 否则 (E1) 按 AppData/用户级程序下的目录段名匹配已安装应用名,
        // 把"数据/缓存目录"(不在安装目录下、却正是我们要清理的东西) 接回它的归属。
        var app = MatchInstalledApp(path) ?? MatchInstalledAppByDataDir(path);
        if (app is not null)
            list.Add(Fact(EvidenceKind.InstalledApp, $"under installed app: {app.Name}", "registry", 0.85));

        // T3 (离线 ground-truth): 目录无注册表归属时, 采样目录内代表性二进制的厂商/产品/签名 → 归属。
        // 不依赖任何名表, 对任意机器、任意软件 (含便携绿色软件) 都成立 —— 这是"在别人电脑上也能识别"的根本。
        if (node.IsDirectory && app is null)
        {
            var sampled = await _win.SampleDirectoryBinaryAsync(path, includeSignature: deep, ct);
            if (sampled is not null)
            {
                metadata ??= sampled;
                EmitMetadataFacts(sampled, "dir-binary");
            }
        }

        return new EvidenceBundle(node.Id, metadata, list);
    }

    private static bool HasVersionInfo(FileMetadata m) =>
        !string.IsNullOrWhiteSpace(m.ProductName) ||
        !string.IsNullOrWhiteSpace(m.CompanyName) ||
        !string.IsNullOrWhiteSpace(m.FileVersion);

    private static FileMetadata WithOccupation(FileMetadata? m, long fileId, string path, string proc)
    {
        m ??= new FileMetadata(fileId, Ext(path), null, null, null, null, null, null, null, null, null);
        return m with { InUse = true, OccupyingProcess = proc };
    }

    private static string? Ext(string path) =>
        Path.GetExtension(path) is { Length: > 0 } e ? e : null;

    // E1: AppData / 用户级程序目录下的目录段名 ↔ 已安装应用名 匹配 → 数据/缓存目录也能归属其应用。
    private static readonly HashSet<string> DataDirNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppData", "Local", "LocalLow", "Roaming", "Packages", "Programs", "Temp", "Microsoft",
        "Common Files", "Cache", "CacheStorage", "Data", "Storage",
    };

    // 取最能代表"应用"的目录段: AppData\{Local|Roaming|LocalLow}\<X> 的 X (Programs\<X> 再下钻一层; Packages\<家族>_hash 取家族)。
    private static string? DataDirSegment(string path)
    {
        var segs = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var ai = Array.FindIndex(segs, s => s.Equals("AppData", StringComparison.OrdinalIgnoreCase));
        if (ai < 0 || ai + 2 >= segs.Length) return null;

        var bucket = segs[ai + 1];
        if (!bucket.Equals("Local", StringComparison.OrdinalIgnoreCase) &&
            !bucket.Equals("Roaming", StringComparison.OrdinalIgnoreCase) &&
            !bucket.Equals("LocalLow", StringComparison.OrdinalIgnoreCase))
            return null;

        var x = segs[ai + 2];
        if (x.Equals("Programs", StringComparison.OrdinalIgnoreCase) && ai + 3 < segs.Length) x = segs[ai + 3];
        else if (x.Equals("Packages", StringComparison.OrdinalIgnoreCase) && ai + 3 < segs.Length) x = segs[ai + 3].Split('_')[0];
        return DataDirNoise.Contains(x) ? null : x;
    }

    private InstalledApp? MatchInstalledAppByDataDir(string path)
    {
        var seg = DataDirSegment(path);
        if (seg is null) return null;
        var normSeg = NormalizeName(seg);
        if (normSeg.Length < 3) return null;

        InstalledApp? best = null;
        var bestScore = 0;
        foreach (var app in _installedApps.Value)
        {
            var normApp = NormalizeName(app.Name);
            if (normApp.Length < 3) continue;
            var score = NameMatchScore(normSeg, normApp);
            if (score > bestScore) { bestScore = score; best = app; }
        }
        return bestScore > 0 ? best : null;
    }

    private static string NormalizeName(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // 评分: 完全相等最高; 一方为另一方前缀 (>=4) 次之; 含子串 (>=5) 再次。0 = 不匹配 (避免短词误配)。
    private static int NameMatchScore(string seg, string app)
    {
        if (seg == app) return 3;
        if (seg.Length >= 4 && app.StartsWith(seg, StringComparison.Ordinal)) return 2;
        if (app.Length >= 4 && seg.StartsWith(app, StringComparison.Ordinal)) return 2;
        if (seg.Length >= 5 && app.Contains(seg, StringComparison.Ordinal)) return 1;
        if (app.Length >= 5 && seg.Contains(app, StringComparison.Ordinal)) return 1;
        return 0;
    }

    // 路径前缀落在某安装目录段边界 → 归属该应用。
    private InstalledApp? MatchInstalledApp(string path)
    {
        InstalledApp? best = null;
        var bestLen = -1;
        foreach (var app in _installedApps.Value)
        {
            var loc = app.InstallLocation;
            if (string.IsNullOrWhiteSpace(loc)) continue;
            var norm = loc!.TrimEnd('\\');
            if (norm.Length == 0) continue;
            var under = path.Equals(norm, StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith(norm + "\\", StringComparison.OrdinalIgnoreCase);
            if (under && norm.Length > bestLen)   // 最具体(最长)安装目录胜出
            {
                best = app;
                bestLen = norm.Length;
            }
        }
        return best;
    }
}
