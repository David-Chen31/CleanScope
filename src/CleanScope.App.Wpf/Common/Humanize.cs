namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 把引擎内部输出翻成"人话"(展示层, 不动裁决逻辑)。
/// 最关键: 真正"认不出归属"的项, 对用户其实就是"个人文件", 不该露出
/// "未知来源 / 无规则/无归因/无缓存特征, 无法判断"这类开发者术语 —— 那让页面像数据库表、像故障。
/// </summary>
public static class Humanize
{
    public const string Personal = "个人文件";

    private static readonly string[] UnknownOrigins =
    {
        "未知来源", "未归类 / 未知来源", "未归类/未知来源",
    };

    /// <summary>来源/归属: 认不出的一律说成"个人文件"; 已识别的软件原样保留。</summary>
    public static string Origin(string? origin)
        => string.IsNullOrWhiteSpace(origin) || Array.IndexOf(UnknownOrigins, origin!.Trim()) >= 0
            ? Personal
            : origin!;

    /// <summary>用途/说明: 当来源被归为"个人文件", 给一句安心的人话, 替掉三无术语。</summary>
    public static string Purpose(string? purpose, string displayedOrigin)
    {
        if (displayedOrigin == Personal) return "你的文件，建议自行判断（非软件产生）";
        if (string.IsNullOrWhiteSpace(purpose) || LooksTechnical(purpose!)) return "";
        return purpose!;
    }

    private static bool LooksTechnical(string s) =>
        s.Contains("无规则") || s.Contains("无归因") || s.Contains("无缓存特征") || s.Contains("无法判断");
}
