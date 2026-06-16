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
            .Select(a => new AttributionViewModel(a.AppName, a.Confidence))
            .ToList();

        var risk = analysis.Risk;
        RiskScore = risk.Score;
        RiskFactors = risk.Factors;

        Ai = analysis.Explanation is { Validated: true } ai ? new AiExplanationViewModel(ai) : null;
    }

    public DecisionItem Item { get; }
    public FileAnalysis Analysis { get; }

    // —— 列表列 ——
    public string Path { get; }
    public string Name { get; }
    public long Size { get; }
    public string SizeText => Format.HumanSize(Size);
    public RiskLevel RiskLevel { get; }
    public string RiskText => RiskLevel.ToString();
    public string RiskMeaning => Format.RiskMeaning(RiskLevel);
    public string? OwnerApp { get; }
    public string RecommendedAction { get; }
    public string? Explanation { get; }

    /// <summary>D/E 高风险: 仅提示"不建议删除", 永不渲染删除入口 (安全注意)。</summary>
    public bool IsHighRisk => RiskLevel is RiskLevel.D or RiskLevel.E;

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
}

/// <summary>归因候选展示 (应用名 + 置信度)。非单一答案, 按置信度排序。</summary>
public sealed class AttributionViewModel
{
    public AttributionViewModel(string appName, double confidence)
    {
        AppName = appName;
        Confidence = confidence;
    }

    public string AppName { get; }
    public double Confidence { get; }
    public string ConfidenceText => $"{Confidence:P0}";
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
