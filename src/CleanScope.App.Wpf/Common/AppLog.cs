using System.IO;

namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 轻量本地日志 (仅本地, 不上云)。把未处理异常写到 %LocalAppData%\CleanScope\logs\app.log,
/// 让"闪退"变成可诊断的记录 + 提示, 而非静默退出。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanScope", "logs", "app.log");

    public static void Error(string context, Exception ex) => Write(context, ex);

    /// <summary>读取日志尾部 (问题#3: 应用内查看, 无需用户自己开文件)。返回最近 <paramref name="maxChars"/> 个字符。</summary>
    public static string ReadRecent(int maxChars = 24000)
    {
        try
        {
            if (!File.Exists(LogPath)) return "";
            lock (Gate)
            {
                var text = File.ReadAllText(LogPath);
                return text.Length > maxChars ? "…（仅显示最近部分）\n" + text[^maxChars..] : text;
            }
        }
        catch (Exception ex) { return $"(读取日志失败：{ex.Message})"; }
    }

    /// <summary>清空日志 (问题#3: 应用内一键清空, 便于复现后重新采集)。</summary>
    public static void Clear()
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.WriteAllText(LogPath, "");
            }
        }
        catch { /* 清空失败不抛 */ }
    }

    /// <summary>统一写入口 (供 <see cref="CleanScope.Domain.Diagnostics.AppTrace"/> sink 注入)。
    /// ex 为 null 时写一条信息行; 非 null 时附完整异常 (含堆栈)。</summary>
    public static void Write(string context, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var line = ex is null
                    ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n"
                    : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n{ex}\n\n";
                File.AppendAllText(LogPath, line);
            }
        }
        catch { /* 日志失败不得再抛 */ }
    }
}
