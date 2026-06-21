using System.Windows.Media;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 风险等级配色 (treemap 与图例共用)。从当前主题资源解析 (随明暗主题切换), 资源缺失时回退浅色硬编码。
/// 与图例的 DynamicResource RiskA…RiskE 同一套键, 故地图与图例恒一致。
/// </summary>
public static class RiskPalette
{
    private static Brush Res(string key, string fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush
        ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));

    public static Brush Brush(RiskLevel? risk) => risk switch
    {
        RiskLevel.A => Res("RiskA", "#9BD3AE"),
        RiskLevel.B => Res("RiskB", "#A9C7F5"),
        RiskLevel.C => Res("RiskC", "#F4D58A"),
        RiskLevel.D => Res("RiskD", "#F0A8A4"),
        RiskLevel.E => Res("RiskE", "#C98B88"),
        _ => Res("RiskNone", "#C9D2DA"),     // 合成根 / 未细分余量
    };

    /// <summary>余量块底 (中性, 不参与风险分级)。</summary>
    public static Brush Remainder => Res("RiskRemainder", "#E6EAEE");

    /// <summary>瓦片上文字色 (暗色主题转浅, 保证落在压暗瓦片上可读)。</summary>
    public static Brush Ink => Res("RiskInk", "#1F2933");
}
