using System.IO;

namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// 从卸载表的 DisplayIcon 反推安装目录 (E2)。很多应用不填 InstallLocation, 但 DisplayIcon 指向其主 exe,
/// 据此可补出安装目录, 提升"已安装应用归属"的命中率。纯字符串处理, 便于单测。
/// </summary>
public static class RegistryInstallPath
{
    /// <summary>从 DisplayIcon 值 (可能形如 "C:\App\a.exe",0 / C:\App\a.exe,0 / "C:\App\a.exe") 取其所在目录; 无法解析返回 null。</summary>
    public static string? FromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var s = displayIcon.Trim();

        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            s = end > 1 ? s[1..end] : s.Trim('"');
        }
        else
        {
            // 去掉尾部图标索引 ",0" / ",-12"。
            var comma = s.LastIndexOf(',');
            if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out _)) s = s[..comma];
        }

        s = s.Trim().Trim('"');
        if (s.Length == 0) return null;

        try
        {
            var dir = Path.GetDirectoryName(s);
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
        catch
        {
            return null;
        }
    }
}
