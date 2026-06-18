using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CleanScope.App.Wpf.Common;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.Converters;

/// <summary>风险等级 → 配色 (A 绿 … E 红)。参数 "fg" 取较深前景色。</summary>
public sealed class RiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (bg, fg) = value is RiskLevel r ? r switch
        {
            RiskLevel.A => ("#E6F4EA", "#1E7E34"),
            RiskLevel.B => ("#E8F0FE", "#1A56C4"),
            RiskLevel.C => ("#FEF7E0", "#9A6700"),
            RiskLevel.D => ("#FCE8E6", "#C5221F"),
            RiskLevel.E => ("#3C0E0C", "#FF6B6B"),
            _ => ("#EEEEEE", "#333333"),
        } : ("#EEEEEE", "#333333");
        var hex = string.Equals(parameter as string, "fg", StringComparison.OrdinalIgnoreCase) ? fg : bg;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility。参数 "invert" 反转。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null / 空字符串 / 空集合 → Collapsed, 否则 Visible。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>占比 (0–1) × 参数(最大像素宽) → 大小条宽度 (P1 资源管理器)。</summary>
public sealed class FractionToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var frac = value is double d ? d : 0;
        var max = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 100;
        return Math.Max(0, Math.Min(1, frac)) * max;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>四桶 → 背景色 (容器/可清理/谨慎/勿动)。</summary>
public sealed class BucketToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CleanupBucket b ? Buckets.Brush(b) : Buckets.Brush(CleanupBucket.Keep);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>事实/推测 → 徽标背景色 (事实=蓝, AI 推测=琥珀, 视觉区分, 安全§9)。</summary>
public sealed class FactToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value is true ? "#E8F0FE" : "#FEF3C7";
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
