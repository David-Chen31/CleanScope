using System.Windows.Media;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 用户友好的"四桶"分类 (S-D/D6): 比抽象 A–E 更直观地回答"能不能删"。
/// 枚举顺序即"可操作性"排序: 可清理 → 谨慎 → 勿动 → 容器。
/// </summary>
public enum CleanupBucket { Cleanable, Caution, Keep, Container }

/// <summary>四桶映射与配色 (容器/可清理/谨慎/勿动)。</summary>
public static class Buckets
{
    public static CleanupBucket Of(bool isContainer, RiskLevel risk) => isContainer
        ? CleanupBucket.Container
        : risk switch
        {
            RiskLevel.A or RiskLevel.B => CleanupBucket.Cleanable,
            RiskLevel.C => CleanupBucket.Caution,
            _ => CleanupBucket.Keep,
        };

    public static CleanupBucket Of(DecisionItem i) => Of(i.IsContainer, i.RiskLevel);

    public static string Label(CleanupBucket b) => b switch
    {
        CleanupBucket.Container => "🗂 容器",
        CleanupBucket.Cleanable => "✅ 可清理",
        CleanupBucket.Caution => "⚠ 谨慎",
        _ => "🛑 勿动",
    };

    public static Color Fill(CleanupBucket b) => (Color)ColorConverter.ConvertFromString(b switch
    {
        CleanupBucket.Container => "#E2E8F0",
        CleanupBucket.Cleanable => "#D7F0DD",
        CleanupBucket.Caution => "#FCEFC7",
        _ => "#F6D4D1",
    });

    public static Brush Brush(CleanupBucket b) => new SolidColorBrush(Fill(b));
}
