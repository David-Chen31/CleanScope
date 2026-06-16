using CleanScope.Domain.Entities;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 证据条目展示 (安全§9: 事实 vs 推测必须视觉可分)。
/// <see cref="IsFact"/>=true 为事实证据 (可驱动权威结论); false 为 AI 推测 (仅供解释参考)。
/// </summary>
public sealed class EvidenceItemViewModel
{
    public EvidenceItemViewModel(Evidence e)
    {
        Kind = e.Kind.ToString();
        Value = e.Value;
        Source = e.Source;
        IsFact = e.IsFact;
    }

    public string Kind { get; }
    public string Value { get; }
    public string? Source { get; }
    public bool IsFact { get; }

    /// <summary>展示用徽标文字: 事实证据 / AI 推测。</summary>
    public string Badge => IsFact ? "事实" : "AI 推测";
}
