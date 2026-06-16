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

        if (acc.Count == 0)
            return Array.Empty<AttributionCandidate>();   // AS-8: 未知, 不臆造

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

    private sealed record Candidate(string Name, double Confidence, IReadOnlyList<long> EvidenceIds);
}
