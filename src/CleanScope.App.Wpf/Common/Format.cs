using System.Globalization;

namespace CleanScope.App.Wpf.Common;

/// <summary>展示格式化 (人类可读大小 / 风险中文标签)。纯 UI 关注点。</summary>
public static class Format
{
    public static string HumanSize(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        double s = bytes;
        var i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return i == 0
            ? $"{bytes} {u[i]}"
            : string.Create(CultureInfo.InvariantCulture, $"{s:0.##} {u[i]}");
    }

    /// <summary>风险等级的中文释义 (与风险分级细则一致)。</summary>
    public static string RiskMeaning(Domain.Enums.RiskLevel level) => level switch
    {
        Domain.Enums.RiskLevel.A => "A · 可安全清理",
        Domain.Enums.RiskLevel.B => "B · 建议用官方方式清理",
        Domain.Enums.RiskLevel.C => "C · 需确认后处理",
        Domain.Enums.RiskLevel.D => "D · 不建议删除",
        Domain.Enums.RiskLevel.E => "E · 无法判断，不建议删除",
        _ => level.ToString(),
    };
}
