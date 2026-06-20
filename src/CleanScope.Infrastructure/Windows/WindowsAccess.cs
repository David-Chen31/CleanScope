using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// Windows 系统访问 (实现 <see cref="IWindowsAccess"/>, 只读取证)。net8.0-windows。
/// 删除能力不在此 (仅 Safety 可改盘)。
///
/// T2.1 范围: 文件版本/产品/公司/描述 (FileVersionInfo) + 嵌入式 Authenticode 签名者 + 真实路径解析 (IR-4)。
/// 已安装应用列表 → T2.2; 进程占用检测 → T2.3。所有读取异常不崩 (返回 null/原值)。
/// </summary>
public sealed class WindowsAccess : IWindowsAccess
{
    public Task<FileMetadata?> ReadMetadataAsync(string path, CancellationToken ct = default)
        => Task.Run(() => ReadMetadata(path), ct);

    private static FileMetadata? ReadMetadata(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;   // 目录 / 不存在 → 无文件元数据

            var fvi = FileVersionInfo.GetVersionInfo(path);
            var (signed, signer) = TryReadEmbeddedSigner(path);

            return new FileMetadata(
                FileId: 0,
                Extension: Path.GetExtension(path) is { Length: > 0 } e ? e : null,
                Description: NullIfBlank(fvi.FileDescription),
                ProductName: NullIfBlank(fvi.ProductName),
                CompanyName: NullIfBlank(fvi.CompanyName),
                FileVersion: NullIfBlank(fvi.FileVersion),
                IsSigned: signed,
                Signer: signer,
                Sha256: null,            // 内容哈希按需计算 (大文件昂贵), T2.1 不默认算
                InUse: null,             // 占用检测 → T2.3
                OccupyingProcess: null); // → T2.3
        }
        catch
        {
            return null;                 // 异常不崩 (DoD)
        }
    }

    /// <summary>读取嵌入式 Authenticode 签名者 (CN)。未签名/仅目录签名/读取失败 → (false, null)。</summary>
    private static (bool IsSigned, string? Signer) TryReadEmbeddedSigner(string path)
    {
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            var name = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return (true, NullIfBlank(name) ?? cert.Subject);
        }
        catch
        {
            return (false, null);
        }
    }

    // T3 采样预算: 浅层 (主程序几乎都在根层或一层内) + 文件数封顶, 避免在巨树上无界 I/O。
    private const int SampleMaxDepth = 2;
    private const int SampleMaxFiles = 256;

    /// <summary>
    /// T3: 采样目录内代表性二进制的元数据 (版本信息 + 可选签名)。有界搜索, 无可执行文件或无可用元数据 → null。
    /// 这是"离线 ground-truth 归因"的核心 —— 不依赖任何名表, 厂商写进 exe 的公司/产品/签名直接定身份。
    /// </summary>
    public Task<FileMetadata?> SampleDirectoryBinaryAsync(
        string dirPath, bool includeSignature, CancellationToken ct = default)
        => Task.Run(() => SampleDirectoryBinary(dirPath, includeSignature), ct);

    private static FileMetadata? SampleDirectoryBinary(string dirPath, bool includeSignature)
    {
        try
        {
            if (!Directory.Exists(dirPath)) return null;

            var candidates = new List<RepresentativeBinary.Candidate>();
            var paths = new List<string>();
            var examined = 0;

            // 浅层 BFS: 逐层枚举文件 (收 exe/dll), 再下钻子目录, 直到深度/文件数预算耗尽。
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((dirPath, 0));
            while (queue.Count > 0 && examined < SampleMaxFiles)
            {
                var (dir, depth) = queue.Dequeue();

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir); }
                catch { continue; }   // 无权限/被占用目录跳过, 不崩

                foreach (var f in files)
                {
                    if (++examined > SampleMaxFiles) break;
                    var ext = Path.GetExtension(f);
                    var isExe = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase);
                    if (!isExe && !ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                    long size;
                    try { size = new FileInfo(f).Length; } catch { size = 0; }
                    candidates.Add(new RepresentativeBinary.Candidate(Path.GetFileName(f), depth, size, isExe));
                    paths.Add(f);
                }

                if (depth + 1 < SampleMaxDepth && examined < SampleMaxFiles)
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                            queue.Enqueue((sub, depth + 1));
                    }
                    catch { /* 跳过不可枚举子目录 */ }
                }
            }

            var leaf = new DirectoryInfo(dirPath.TrimEnd('\\', '/')).Name;
            var pick = RepresentativeBinary.PickBest(leaf, candidates);
            if (pick is not int idx) return null;

            return ReadBinaryMetadata(paths[idx], includeSignature);
        }
        catch
        {
            return null;   // 任意异常不崩 (DoD)
        }
    }

    // 读代表性二进制的版本信息 (+ 可选签名)。无任何可用归因字段 → null (避免空壳证据)。
    private static FileMetadata? ReadBinaryMetadata(string path, bool includeSignature)
    {
        try
        {
            var fvi = FileVersionInfo.GetVersionInfo(path);
            var product = NullIfBlank(fvi.ProductName);
            var company = NullIfBlank(fvi.CompanyName);
            var version = NullIfBlank(fvi.FileVersion);
            var desc = NullIfBlank(fvi.FileDescription);

            bool? signed = null;
            string? signer = null;
            if (includeSignature)
            {
                (var s, signer) = TryReadEmbeddedSigner(path);
                signed = s;
            }

            if (product is null && company is null && version is null && signer is null)
                return null;   // 该二进制没有可归因信息

            return new FileMetadata(
                FileId: 0,
                Extension: Path.GetExtension(path) is { Length: > 0 } e ? e : null,
                Description: desc,
                ProductName: product,
                CompanyName: company,
                FileVersion: version,
                IsSigned: signed,
                Signer: signer,
                Sha256: null,
                InUse: null,
                OccupyingProcess: null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>IR-4: 解析符号链接/junction 到真实最终路径。失败回退规范化原路径。</summary>
    public string ResolveRealPath(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// 已安装软件列表 (只读注册表 Uninstall): HKLM 64位 + HKLM 32位(WOW6432Node) + HKCU。
    /// 覆盖 Win32 及绝大多数 WinGet 安装项 (后者亦写 Uninstall)。原生 Appx/UWP 枚举需 WinRT, 留作后续。
    /// </summary>
    public IReadOnlyList<InstalledApp> GetInstalledApplications()
    {
        var apps = new List<InstalledApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, apps, seen);
        ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, apps, seen); // WOW6432Node
        ReadUninstall(RegistryHive.CurrentUser, RegistryView.Default, apps, seen);
        return apps;
    }

    private static void ReadUninstall(
        RegistryHive hive, RegistryView view, List<InstalledApp> outp, HashSet<string> seen)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath);
            if (root is null) return;

            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var sub = root.OpenSubKey(subName);
                    if (sub is null) continue;

                    var display = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display)) continue;                 // 无名 → 多为更新/组件
                    if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue; // 系统组件
                    if (sub.GetValue("ParentKeyName") is string pk && pk.Length > 0) continue; // 补丁子项

                    // E2: InstallLocation 常为空 → 从 DisplayIcon 反推安装目录, 提升归属命中率。
                    var location = NullIfBlank(sub.GetValue("InstallLocation") as string)
                        ?? NullIfBlank(RegistryInstallPath.FromDisplayIcon(sub.GetValue("DisplayIcon") as string));
                    if (!seen.Add(display + "|" + (location ?? string.Empty))) continue; // 跨视图去重

                    outp.Add(new InstalledApp(
                        Name: display!,
                        Publisher: NullIfBlank(sub.GetValue("Publisher") as string),
                        InstallLocation: location,
                        Source: "Registry"));
                }
                catch { /* 单项异常跳过, 不影响整体 */ }
            }
        }
        catch { /* 某 hive/view 不可用 → 跳过 */ }
    }

    /// <summary>文件被哪个进程占用 (IR-2 删除前置)。经 Restart Manager; 无占用/失败 → null。仅返回进程名(非路径)。</summary>
    public string? GetOccupyingProcessName(string path) => RestartManager.GetOccupyingProcessName(path);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
