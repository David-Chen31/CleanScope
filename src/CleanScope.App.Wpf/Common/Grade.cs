using System.Windows.Media;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 风险分级徽章 —— CleanScope 的签名视觉 (品牌资产)。A–E 字母等级 + 人话标签 + 固定配色,
/// 在清单 / 详情 / 空间地图三处长得完全一致, 让"风险/解释"成为视觉主角 (而非通用红黄绿点)。
/// 个人文件 / 容器 / 余量不参与 A–E 评级 —— 用"无字母"圆点徽章, 语义上与风险项区分开。
/// </summary>
public sealed record GradeBadge(string Letter, string Label, Brush Chip, bool HasLetter)
{
    // 非评级态 (无字母, 中性色): 你的资料 / 结构容器 / 未细分余量。
    public static readonly GradeBadge Personal = new("", "个人文件 · 珍贵≠危险", Solid("#7E93AC"), false);
    public static readonly GradeBadge Container = new("", "容器 · 结构而非垃圾", Solid("#94A3B2"), false);
    public static readonly GradeBadge Other = new("", "本目录其它文件", Solid("#AAB4C0"), false);

    /// <summary>A–E 风险等级 → 签名字母徽章 (A 安全 → E 系统关键)。</summary>
    public static GradeBadge Of(RiskLevel risk) => risk switch
    {
        RiskLevel.A => new("A", "可放心清理", Solid("#2E9E5B"), true),
        RiskLevel.B => new("B", "基本安全", Solid("#3B82C4"), true),
        RiskLevel.C => new("C", "谨慎 · 删前确认", Solid("#C2871B"), true),
        RiskLevel.D => new("D", "高风险 · 不建议删", Solid("#D24A45"), true),
        RiskLevel.E => new("E", "系统关键 · 勿动", Solid("#8B2E2A"), true),
        _ => new("?", "未分级", Solid("#94A3B2"), true),
    };

    private static Brush Solid(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
