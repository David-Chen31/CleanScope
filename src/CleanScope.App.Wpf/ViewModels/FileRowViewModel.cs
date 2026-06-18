using CleanScope.App.Wpf.Common;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 列表/详情共用的逐文件行视图模型。包裹精简的 <see cref="DecisionItem"/> (展示用)
/// 与完整的 <see cref="FileAnalysis"/> (证据链/归因/AI 解释), 由两者拼出 UI 字段。
///
/// 红线: 本类不提供任何删除入口 (MVP 零删除); D/E 高风险仅作"不建议删除"提示。
/// </summary>
public sealed class FileRowViewModel
{
    public FileRowViewModel(DecisionItem item, FileAnalysis analysis)
    {
        Item = item;
        Analysis = analysis;

        var node = analysis.Node;
        var meta = analysis.Evidence.Metadata;

        Path = item.Path;
        Name = node.Name;
        Size = item.Size;
        ExclusiveSize = item.ExclusiveSize;
        RiskLevel = item.RiskLevel;
        OwnerApp = item.OwnerApp;
        RecommendedAction = item.RecommendedAction;
        Explanation = item.Explanation;

        IsDirectory = node.IsDirectory;
        IsReparsePoint = node.IsReparsePoint;
        RealPath = node.RealPath;
        NodeType = node.NodeType;
        Extension = meta?.Extension;
        IsSigned = meta?.IsSigned;
        Signer = meta?.Signer;
        ProductName = meta?.ProductName;
        CompanyName = meta?.CompanyName;
        Mtime = node.Mtime;
        IsOccupied = meta?.InUse == true;
        OccupyingProcess = meta?.OccupyingProcess;

        Evidences = analysis.Evidence.Evidences.Select(e => new EvidenceItemViewModel(e)).ToList();
        Attributions = analysis.Attributions
            .OrderByDescending(a => a.Confidence)
            .Select(a => new AttributionViewModel(a.AppName, a.Confidence, a.Source))
            .ToList();

        var risk = analysis.Risk;
        RiskScore = risk.Score;
        RiskFactors = risk.Factors;

        Ai = analysis.Explanation is { Validated: true } ai ? new AiExplanationViewModel(ai) : null;
        AiInvestigation = item.AiInvestigation;
    }

    public DecisionItem Item { get; }
    public FileAnalysis Analysis { get; }

    // —— 列表列 ——
    public string Path { get; }
    public string Name { get; }
    public long Size { get; }
    public long ExclusiveSize { get; }
    public string SizeText => Format.HumanSize(Size);
    public RiskLevel RiskLevel { get; }
    public string RiskText => RiskLevel.ToString();
    public string RiskMeaning => Format.RiskMeaning(RiskLevel);
    public string? OwnerApp { get; }
    /// <summary>统一"来源/归属"短标签 (列表列): 应用 ▸ 系统来源 ▸ 容器角色 ▸ 未知。保证非空。</summary>
    public string Origin => Item.Origin ?? OwnerApp ?? "未知来源";
    public string RecommendedAction { get; }
    public string? Explanation { get; }

    /// <summary>D/E 高风险: 仅提示"不建议删除", 永不渲染删除入口 (安全注意)。</summary>
    public bool IsHighRisk => RiskLevel is RiskLevel.D or RiskLevel.E;

    /// <summary>顶层容器目录 (仅浏览, 非删除对象)。</summary>
    public bool IsContainer => Item.IsContainer;

    // —— 四桶 (D6: 比 A–E 更直观) ——
    public CleanupBucket Bucket => Buckets.Of(Item);
    public string BucketLabel => Buckets.Label(Bucket);

    // —— S-D 推荐动作 ——
    public CleanupActionKind ActionKind => Item.ActionKind;
    public string? Command => Item.Command;
    public bool HasRunCommand => ActionKind == CleanupActionKind.RunCommand && !string.IsNullOrWhiteSpace(Command);
    public bool HasUninstall => ActionKind == CleanupActionKind.Uninstall;

    /// <summary>S-E: 是否提供「移入回收站」入口。仅"可清理"桶 (A/B)、非容器、未被占用;
    /// 安全闸门仍会独立复核 (黑名单/容器/占用/风险), 此处只决定是否渲染按钮。</summary>
    public bool CanRecycle => Bucket == CleanupBucket.Cleanable && !IsContainer && !IsOccupied;

    // —— 详情属性 ——
    public bool IsDirectory { get; }
    public bool IsReparsePoint { get; }
    public string? RealPath { get; }
    public NodeType? NodeType { get; }
    public string? Extension { get; }
    public bool? IsSigned { get; }
    public string? Signer { get; }
    public string? ProductName { get; }
    public string? CompanyName { get; }
    public DateTime? Mtime { get; }
    public bool IsOccupied { get; }
    public string? OccupyingProcess { get; }

    public int RiskScore { get; }
    public IReadOnlyList<string> RiskFactors { get; }

    public IReadOnlyList<EvidenceItemViewModel> Evidences { get; }
    public IReadOnlyList<EvidenceItemViewModel> FactEvidences =>
        Evidences.Where(e => e.IsFact).ToList();
    public IReadOnlyList<EvidenceItemViewModel> InferenceEvidences =>
        Evidences.Where(e => !e.IsFact).ToList();
    public bool HasInferenceEvidence => InferenceEvidences.Count > 0;

    public IReadOnlyList<AttributionViewModel> Attributions { get; }
    public bool HasAttributions => Attributions.Count > 0;

    /// <summary>仅当 AI 解释通过校验 (Validated) 才有值; 未校验一律不展示 (架构§5)。</summary>
    public AiExplanationViewModel? Ai { get; }
    public bool HasAi => Ai is not null;

    /// <summary>S-C: AI 对未知项的调查推测 (已校验, 仅供参考, 不改判风险)。批量调查或详情按需均可填充。</summary>
    public string? AiInvestigation { get; }
    public bool HasAiInvestigation => !string.IsNullOrWhiteSpace(AiInvestigation);
}

/// <summary>归因候选展示 (应用名 + 置信度 + 来源)。非单一答案, 按置信度排序。</summary>
public sealed class AttributionViewModel
{
    public AttributionViewModel(string appName, double confidence, string? source = null)
    {
        AppName = appName;
        Confidence = confidence;
        Source = source;
    }

    public string AppName { get; }
    public double Confidence { get; }
    public string ConfidenceText => $"{Confidence:P0}";

    /// <summary>来源: null=事实证据(权威); "路径推断"/"AI 推测"=低置信猜测 (S-G, 诚实标注)。</summary>
    public string? Source { get; }
    public bool IsInferred => !string.IsNullOrWhiteSpace(Source);
    /// <summary>展示标签: 事实归属不加缀; 推断/AI 加"(来源)"后缀, 让用户知道这是猜测。</summary>
    public string DisplayText => IsInferred ? $"{AppName}（{Source}）" : AppName;
}

/// <summary>已校验 AI 解释展示。来源标注 (云端/本地规则) 供隐私审计可见。</summary>
public sealed class AiExplanationViewModel
{
    public AiExplanationViewModel(AiExplanation e)
    {
        WhatIsIt = e.WhatIsIt;
        UserFriendlyExplanation = e.UserFriendlyExplanation;
        Reasoning = e.Reasoning;
        Confidence = e.Confidence;
        ModelUsed = e.ModelUsed;
        IsCloud = e.IsCloud;
    }

    public string? WhatIsIt { get; }
    public string? UserFriendlyExplanation { get; }
    public IReadOnlyList<string> Reasoning { get; }
    public double? Confidence { get; }
    public string? ModelUsed { get; }
    public bool IsCloud { get; }

    public string SourceLabel => IsCloud ? $"AI 云端解释 ({ModelUsed}) · 已校验" : $"本地规则解释 ({ModelUsed})";
}
