using System.Text.RegularExpressions;

namespace CleanScope.Core.Attribution;

/// <summary>
/// 容器目录的"存在解释" (确定性, 非 AI)。顶层容器 (盘根 / Users / AppData / Program Files / ProgramData)
/// 没有单一归属应用, 但有明确角色——它装的是什么。给每个容器一个**简短标签** (列表"来源/归属"列) 与
/// **完整描述** (详情/报告"说明"列), 落实"每个文件夹都知道是什么、干什么"。
/// </summary>
public static class ContainerPurpose
{
    private static readonly Regex UserHome = new(@"^[a-z]:\\users\\[^\\]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UsersRoot = new(@"^[a-z]:\\users$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DriveRoot = new(@"^[a-z]:\\?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>返回 (短标签, 完整描述); 非已知容器返回 null。</summary>
    public static (string Short, string Full)? Describe(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var lower = path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

        // AppData 家族 (先于"用户主目录"判断, 因为更具体)。
        if (lower.EndsWith(@"\appdata\roaming"))
            return ("应用配置·漫游", "用户应用程序的配置与个性化数据 (随账户在域内漫游)");
        if (lower.EndsWith(@"\appdata\local"))
            return ("应用数据·本机", "本机应用程序的数据与缓存 (不随账户漫游)");
        if (lower.EndsWith(@"\appdata\locallow"))
            return ("应用数据·低权限", "低完整性应用数据 (如浏览器沙箱 / 受保护模式)");
        if (lower.EndsWith(@"\appdata"))
            return ("应用数据根", "各软件的配置、数据与缓存的总目录");

        // 用户主目录。
        if (UserHome.IsMatch(lower))
            return ("用户主目录", "你的用户主目录: 文档 / 桌面 / 下载, 及各软件的个人数据");
        if (UsersRoot.IsMatch(lower))
            return ("所有用户", "本机所有用户的主目录");

        // 程序与共享数据。
        if (lower.EndsWith(@"\program files (x86)"))
            return ("程序·32位", "32 位程序的安装目录");
        if (lower.EndsWith(@"\program files"))
            return ("程序·64位", "64 位程序的安装目录");
        if (lower.EndsWith(@"\programdata"))
            return ("共享应用数据", "所有用户共享的应用程序数据");

        // 盘根。
        if (DriveRoot.IsMatch(lower))
            return ("磁盘根目录", "整个磁盘分区的根目录, 内含系统与所有数据");

        return null;
    }
}
