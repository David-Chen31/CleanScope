namespace CleanScope.Core.Attribution;

/// <summary>
/// 知名软件特征库 (① 的本地化形态): 随应用内置、可经 git 持续扩充的数据驱动归因增强 ——
/// 对标 CCleaner/360 的"云端规则库", 但**纯本地、零联网、零上报**, 把"靠云"换成"靠内置可更新特征包"。
///
/// 两类条目, 互补 T3 (读二进制) 这条 ground-truth 主线:
///  ① <see cref="VendorAlias"/>: 把二进制里原始的公司/签名者字符串归一为友好名 (Valve→Steam、Tencent→腾讯)。
///  ② <see cref="DirectoryAlias"/>: 对**没有可执行文件的纯数据目录** (微信接收目录、数据集…) 按目录名给归属/用途,
///     这是 T3 也读不到时的诚实兜底 (展示为"依据目录名/特征库", 与事实区分, 守安全§9)。
///
/// 匹配纯字符串、确定性、可单测。空库 (<see cref="Empty"/>) 时全部返回 null —— 增强缺失绝不影响主流程。
/// </summary>
public sealed class KnownSoftwareCatalog
{
    public static KnownSoftwareCatalog Empty { get; } =
        new(Array.Empty<VendorAlias>(), Array.Empty<DirectoryAlias>());

    private readonly IReadOnlyList<VendorAlias> _vendors;
    private readonly IReadOnlyList<DirectoryAlias> _directories;

    public KnownSoftwareCatalog(KnownSoftwareData data)
        : this(data?.Vendors ?? Array.Empty<VendorAlias>(), data?.Directories ?? Array.Empty<DirectoryAlias>()) { }

    public KnownSoftwareCatalog(IEnumerable<VendorAlias> vendors, IEnumerable<DirectoryAlias> directories)
    {
        _vendors = vendors?.Where(v => !string.IsNullOrWhiteSpace(v.Contains) && !string.IsNullOrWhiteSpace(v.Name))
            .ToList() ?? new List<VendorAlias>();
        _directories = directories?.Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.App))
            .ToList() ?? new List<DirectoryAlias>();
    }

    public int VendorCount => _vendors.Count;
    public int DirectoryCount => _directories.Count;

    /// <summary>把原始公司/签名者字符串归一为友好名 (子串匹配)。无匹配返回 null (调用方保留原值)。</summary>
    public string? FriendlyVendor(string? companyOrSigner)
    {
        if (string.IsNullOrWhiteSpace(companyOrSigner)) return null;
        foreach (var v in _vendors)
            if (companyOrSigner.Contains(v.Contains, StringComparison.OrdinalIgnoreCase))
                return v.Name;
        return null;
    }

    /// <summary>按目录叶子名匹配已知软件 (归一化后等于/前缀/包含)。无匹配返回 null。</summary>
    public DirectoryHint? MatchDirectory(string? leafName)
    {
        var seg = Normalize(leafName);
        if (seg.Length < 2) return null;

        DirectoryAlias? best = null;
        var bestScore = 0;
        foreach (var d in _directories)
        {
            var score = NameMatchScore(seg, Normalize(d.Name));
            if (score > bestScore) { bestScore = score; best = d; }
        }
        return best is { } b ? new DirectoryHint(b.App, string.IsNullOrWhiteSpace(b.Purpose) ? null : b.Purpose) : null;
    }

    private static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty
        : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // 相等最高; 一方为另一方前缀 (≥4) 次之; 含子串 (≥5) 再次。0=不匹配 (避免短词误配)。
    private static int NameMatchScore(string seg, string name)
    {
        if (name.Length < 2) return 0;
        if (seg == name) return 3;
        if (seg.Length >= 4 && name.Length >= 4 &&
            (name.StartsWith(seg, StringComparison.Ordinal) || seg.StartsWith(name, StringComparison.Ordinal)))
            return 2;
        if (seg.Length >= 5 && name.Length >= 5 &&
            (name.Contains(seg, StringComparison.Ordinal) || seg.Contains(name, StringComparison.Ordinal)))
            return 1;
        return 0;
    }
}

/// <summary>目录名匹配结果 (VendorAlias / DirectoryAlias 数据类型在 Domain.Models)。</summary>
public readonly record struct DirectoryHint(string App, string? Purpose);
