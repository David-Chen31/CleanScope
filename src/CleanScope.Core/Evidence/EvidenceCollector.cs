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

    public async Task<EvidenceBundle> CollectAsync(FileNode node, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var path = node.RealPath ?? node.Path;
        var now = DateTime.UtcNow;
        var list = new List<Evidence>();
        long id = 0;

        Evidence Fact(EvidenceKind kind, string value, string source, double weight) =>
            new(++id, node.Id, kind, value, source, IsFact: true, weight, now);

        // 基础观测事实: 保证证据链非空 (SR-5), 即便无任何其它信号。
        list.Add(Fact(EvidenceKind.Metadata, path, "scan", 0.5));

        FileMetadata? metadata = null;

        if (!node.IsDirectory)
        {
            var ext = Path.GetExtension(path);
            if (ext.Length > 0)
                list.Add(Fact(EvidenceKind.Extension, ext, "scan", 0.3));

            metadata = await _win.ReadMetadataAsync(path, ct);
            if (metadata is not null)
            {
                if (HasVersionInfo(metadata))
                    list.Add(Fact(EvidenceKind.Metadata,
                        $"product={metadata.ProductName}; company={metadata.CompanyName}; version={metadata.FileVersion}",
                        "version-info", 0.7));

                if (metadata.IsSigned == true && !string.IsNullOrWhiteSpace(metadata.Signer))
                    list.Add(Fact(EvidenceKind.Signature, $"signed by {metadata.Signer}", "authenticode", 0.9));
            }
        }

        // 进程占用 (强事实): 影响删除前置 (IR-2)。仅对文件检测。
        if (!node.IsDirectory)
        {
            var proc = _win.GetOccupyingProcessName(path);
            if (!string.IsNullOrWhiteSpace(proc))
            {
                list.Add(Fact(EvidenceKind.Process, $"in use by {proc}", "restart-manager", 0.95));
                metadata = WithOccupation(metadata, node.Id, path, proc!);
            }
        }

        // 已安装应用归属 (事实): 路径位于某应用安装目录下。
        var app = MatchInstalledApp(path);
        if (app is not null)
            list.Add(Fact(EvidenceKind.InstalledApp, $"under installed app: {app.Name}", "registry-uninstall", 0.85));

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
