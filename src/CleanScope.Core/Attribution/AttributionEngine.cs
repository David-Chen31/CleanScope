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

    // 归因强度 (区别于证据可靠性 weight): 安装目录归属最强, 产品名次之, 签名者(厂商)最弱。
    private const double InstalledAppConfidence = 0.85;
    private const double ProductConfidence = 0.70;
    private const double SignerConfidence = 0.50;

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
            Fuse(acc, name, conf, e.Id);
        }

        // S4: 无任何事实候选时, 从路径模式推断一个低置信候选 (填补"小文件夹无归属"的空白)。
        // 置信度刻意低于 0.8, 不驱动风险判定 (仅供展示); 不与事实候选混淆 (仅在事实为空时启用)。
        if (acc.Count == 0)
        {
            var inferred = PathPatternCandidate(node.RealPath ?? node.Path);
            return inferred is null
                ? Array.Empty<AttributionCandidate>()     // AS-8: 仍未知, 不臆造
                : new[] { new AttributionCandidate(0, node.Id, inferred, 0.5, 1, Array.Empty<long>(), Source: "路径推断") };
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
        EvidenceKind.Metadata when Product(e.Value) is { } p => (p, ProductConfidence),
        _ => (null, 0),
    };

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

    // 从 "product=X; company=Y; version=Z" 取 X (空/缺则 null)。
    private static string? Product(string value)
    {
        var i = value.IndexOf(ProductMarker, StringComparison.Ordinal);
        if (i < 0) return null;
        var start = i + ProductMarker.Length;
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
        var pi = Array.FindIndex(segs, s =>
            s.StartsWith("Program Files", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("ProgramData", StringComparison.OrdinalIgnoreCase));
        if (pi >= 0 && pi + 1 < segs.Length)
            return Friendly(segs[pi + 1]);

        return null;
    }

    private static string? Friendly(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || NoiseSegments.Contains(segment)) return null;
        return VendorNames.TryGetValue(segment, out var v) ? v : segment;
    }

    private sealed record Candidate(string Name, double Confidence, IReadOnlyList<long> EvidenceIds);
}
