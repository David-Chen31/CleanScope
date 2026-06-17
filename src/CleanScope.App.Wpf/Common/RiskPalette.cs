using System.Windows.Media;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.Common;

/// <summary>风险等级配色 (treemap 与图例共用)。与 RiskToBrushConverter 同一套色。</summary>
public static class RiskPalette
{
    public static Color Fill(RiskLevel? risk) => (Color)ColorConverter.ConvertFromString(risk switch
    {
        RiskLevel.A => "#9BD3AE",
        RiskLevel.B => "#A9C7F5",
        RiskLevel.C => "#F4D58A",
        RiskLevel.D => "#F0A8A4",
        RiskLevel.E => "#C98B88",
        _ => "#C9D2DA",     // 合成根 / 未细分余量
    });

    public static Brush Brush(RiskLevel? risk) => new SolidColorBrush(Fill(risk));
}
