namespace CleanScope.Core.Attribution;

/// <summary>
/// 归因引擎 (架构§3)。多证据融合 → **候选归属列表 + 置信度** (非单一答案):
/// 同名候选按概率 OR 融合 (独立证据互相增强), 合并支撑证据 id, 按置信度降序排名。
///
/// 安全 (AS-8): **不臆造归属** —— 无可归属证据时返回空列表 (即"未知"), 绝不编造。
/// 候选来源仅限事实证据: 已安装应用归属 / 元数据产品名 / 数字签名者。
/// </summary>
public sealed class AttributionEngine : IAttributionEngine
{
    private const string InstalledAppPrefix = "under installed app: ";
    private const string SignaturePrefix = "signed by ";
    private const string ProductMarker = "product=";
    private const string CompanyMarker = "company=";

    // 归因强度 (区别于证据可靠性 weight): 安装目录归属最强, 产品名次之, 公司/签名者(厂商)最弱。
    private const double InstalledAppConfidence = 0.85;
    private const double ProductConfidence = 0.70;
    private const double CompanyConfidence = 0.55;
    private const double SignerConfidence = 0.50;

    private readonly KnownSoftwareCatalog _catalog;

    /// <param name="catalog">知名软件特征库 (① 本地化): 归一厂商名 + 纯数据目录兜底。null=空库 (不增强)。</param>
    public AttributionEngine(KnownSoftwareCatalog? catalog = null) => _catalog = catalog ?? KnownSoftwareCatalog.Empty;

    public IReadOnlyList<AttributionCandidate> Attribute(
        FileNode node, EvidenceBundle evidence, RuleMatch? ruleMatch)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(evidence);

        // key(归一化名) → (显示名, 置信度, 支撑证据 id)
        var acc = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in evidence.Evidences)
        {
            if (!e.IsFact) continue;   // 只采信事实 (§9)
            var (name, conf) = Extract(e);
            if (name is null) continue;
            // ① 特征库: 把原始公司/签名者归一为友好名 (Valve→Steam 等); 产品名一般已友好, 命不中保留原值。
            Fuse(acc, _catalog.FriendlyVendor(name) ?? name, conf, e.Id);
        }

        // 无任何事实候选时: 先按路径段推断应用 (S4), 再特征库目录名兜底, 最后按系统/共享路径表给来源。
        // 目标: 让系统文件与纯数据目录也有"来源", 不落进"未归类"。均为低置信展示, 不驱动风险。
        if (acc.Count == 0)
        {
            var path = node.RealPath ?? node.Path;
            var inferred = PathPatternCandidate(path);
            if (inferred is not null)
                return new[] { new AttributionCandidate(0, node.Id, inferred, 0.5, 1, Array.Empty<long>(), Source: "路径推断") };

            // ① 特征库目录名兜底 (T3 也读不到二进制的纯数据目录, 如微信接收目录、数据集)。
            if (node.IsDirectory && _catalog.MatchDirectory(LeafName(path)) is { } hint)
                return new[] { new AttributionCandidate(0, node.Id, hint.App, 0.5, 1, Array.Empty<long>(), Source: "特征库") };

            if (SystemOrigin.Resolve(path) is { } origin)
                return new[] { new AttributionCandidate(0, node.Id, origin.Owner, 0.85, 1, Array.Empty<long>(), Source: "系统目录") };

            return Array.Empty<AttributionCandidate>();   // AS-8: 仍未知, 不臆造
        }

        // 规则类别可佐证 (非新增候选): 若类别提及某候选名, 略增其置信度。
        if (ruleMatch?.Category is { Length: > 0 } category)
            foreach (var key in acc.Keys.ToList())
                if (category.Contains(acc[key].Name, StringComparison.OrdinalIgnoreCase))
                    acc[key] = acc[key] with { Confidence = Or(acc[key].Confidence, 0.3) };

        return acc.Values
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select((c, i) => new AttributionCandidate(
                Id: 0,
                FileId: node.Id,
                AppName: c.Name,
                Confidence: Math.Round(c.Confidence, 4),
                Rank: i + 1,
                SupportingEvidenceIds: c.EvidenceIds))
            .ToList();
    }

    private static (string? Name, double Confidence) Extract(Evidence e) => e.Kind switch
    {
        EvidenceKind.InstalledApp when After(e.Value, InstalledAppPrefix) is { } n => (n, InstalledAppConfidence),
        EvidenceKind.Signature when After(e.Value, SignaturePrefix) is { } s => (s, SignerConfidence),
        // 产品名优先 (更友好); 缺产品名时退而用公司名 (T3 采样的 dll 常只有 company)。
        EvidenceKind.Metadata when Field(e.Value, ProductMarker) is { } p => (p, ProductConfidence),
        EvidenceKind.Metadata when Field(e.Value, CompanyMarker) is { } c => (c, CompanyConfidence),
        _ => (null, 0),
    };

    private static string LeafName(string path)
    {
        var s = path.Replace('/', '\\').TrimEnd('\\');
        var i = s.LastIndexOf('\\');
        return i >= 0 && i + 1 < s.Length ? s[(i + 1)..] : s;
    }

    private static void Fuse(Dictionary<string, Candidate> acc, string name, double conf, long evidenceId)
    {
        var key = name.Trim();
        if (acc.TryGetValue(key, out var existing))
        {
            var ids = existing.EvidenceIds.Contains(evidenceId)
                ? existing.EvidenceIds
                : existing.EvidenceIds.Append(evidenceId).ToList();
            acc[key] = existing with { Confidence = Or(existing.Confidence, conf), EvidenceIds = ids };
        }
        else
        {
            acc[key] = new Candidate(key, conf, new List<long> { evidenceId });
        }
    }

    // 概率 OR: 独立证据互相增强, 上限 0.99。
    private static double Or(double a, double b) => Math.Min(0.99, 1 - (1 - a) * (1 - b));

    private static string? After(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) && value.Length > prefix.Length
            ? value[prefix.Length..].Trim()
            : null;

    // 从 "product=X; company=Y; version=Z" 取某字段值 (空/缺则 null)。
    private static string? Field(string value, string marker)
    {
        var i = value.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var start = i + marker.Length;
        var end = value.IndexOf(';', start);
        var p = (end < 0 ? value[start..] : value[start..end]).Trim();
        return p.Length == 0 ? null : p;
    }

    // 路径模式归因 (S4): 从安装/应用数据/工具链路径段推断归属。返回 null 表示无法推断。
    // 已知厂商目录映射到友好名; 否则取目录段原名。仅作低置信展示, 不作权威。
    private static readonly Dictionary<string, string> VendorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tencent"] = "腾讯系列 (QQ/微信等)",
        ["WeChat"] = "微信 (WeChat)",
        ["xwechat"] = "微信 (WeChat)",
        ["LarkShell"] = "飞书 (Lark)",
        ["Notion"] = "Notion",
        ["JetBrains"] = "JetBrains 系列",
        ["Code"] = "Visual Studio Code",
        ["vscode-remote-wsl"] = "VS Code Remote (WSL)",
        [".lingma"] = "通义灵码 (Lingma)",
        [".vscode"] = "Visual Studio Code",
        [".cargo"] = "Rust / Cargo",
        [".rustup"] = "Rust / rustup",
        [".nuget"] = "NuGet (.NET)",
        [".gradle"] = "Gradle",
        [".m2"] = "Maven",
        [".npm"] = "npm (Node.js)",
        [".conda"] = "conda (Python)",
        ["Miniconda3"] = "Miniconda (Python)",
        ["miniconda3"] = "Miniconda (Python)",
        ["anaconda3"] = "Anaconda (Python)",
        ["NVIDIA Corporation"] = "NVIDIA",
        ["NVIDIA"] = "NVIDIA",
        ["Lenovo"] = "联想 (Lenovo)",
        ["Docker"] = "Docker",
        ["WSL"] = "适用于 Linux 的 Windows 子系统 (WSL)",
        ["Adobe"] = "Adobe",
        ["Google"] = "Google",
        ["Mozilla"] = "Mozilla",
        ["Oracle"] = "Oracle",
        ["MySQL"] = "MySQL",
        ["Thunder Network"] = "迅雷 (Thunder)",
        ["Steam"] = "Steam",
        ["Microsoft Office"] = "Microsoft Office",
        ["Microsoft Visual Studio"] = "Visual Studio",
    };

    // 明确的工具链/应用专属目录名: 出现在路径任意层即可归属 (不依赖 AppData/Program Files 上下文)。
    private static readonly HashSet<string> KnownAppDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cargo", ".rustup", ".nuget", ".gradle", ".m2", ".npm", ".conda", ".lingma", ".vscode",
        "Miniconda3", "miniconda3", "anaconda3", "vscode-remote-wsl",
    };

    private static readonly HashSet<string> NoiseSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "AppData", "Local", "LocalLow", "Roaming", "Packages", "Programs", "Temp", "Common Files",
    };

    private static string? PathPatternCandidate(string path)
    {
        var segs = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);

        // 1) 明确的工具链/应用专属目录 (任意层命中即归属)。
        foreach (var s in segs)
            if (KnownAppDirs.Contains(s))
                return Friendly(s);

        // 2) AppData\{Local,Roaming}\<X> (或 Local\Packages\<家族>_xxx)。
        var ai = Array.FindIndex(segs, s => s.Equals("AppData", StringComparison.OrdinalIgnoreCase));
        if (ai >= 0 && ai + 2 < segs.Length)
        {
            var after = segs[ai + 2];
            if (after.Equals("Packages", StringComparison.OrdinalIgnoreCase) && ai + 3 < segs.Length)
                return Friendly(segs[ai + 3].Split('_')[0]);    // 包家族名 (去掉发布者哈希后缀)
            return Friendly(after);
        }

        // 3) Program Files / Program Files (x86) / ProgramData 下第一段 = 厂商/应用。
        //    若第一段是共享/噪声段 (如 Common Files), 取下一段 (如 Common Files\Adobe → Adobe)。
        var pi = Array.FindIndex(segs, s =>
            s.StartsWith("Program Files", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("ProgramData", StringComparison.OrdinalIgnoreCase));
        if (pi >= 0 && pi + 1 < segs.Length)
        {
            var first = Friendly(segs[pi + 1]);
            if (first is not null) return first;
            if (pi + 2 < segs.Length) return Friendly(segs[pi + 2]);   // 跳过 Common Files 等共享段
        }

        return null;
    }

    private static string? Friendly(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || NoiseSegments.Contains(segment)) return null;
        return VendorNames.TryGetValue(segment, out var v) ? v : segment;
    }

    private sealed record Candidate(string Name, double Confidence, IReadOnlyList<long> EvidenceIds);
}
