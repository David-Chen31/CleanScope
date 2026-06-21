using System.Runtime.Versioning;
using CleanScope.Domain.Abstractions;

namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// 回收站还原 (<see cref="IRecycleRestore"/> 的 Windows 实现, H)。经 Shell (Shell.Application COM)
/// 在回收站里按"原始位置"匹配目标项, 调用其"还原"动词把文件移回原位。**纯还原, 不触碰任何删除 API**。
/// 找不到匹配 / 动词不可用 / 非 Windows → 返回 false, 由上层回退到"打开回收站手动还原"。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleRestore : IRecycleRestore
{
    private const int RecycleBinFolder = 10;   // ssfBITBUCKET

    // 还原动词的常见本地化名 (去掉 & 加速键后比较)。覆盖 zh-CN / zh-TW / en 等。
    private static readonly string[] RestoreVerbs = { "还原", "恢复", "復原", "還原", "restore", "undelete" };

    public bool TryRestore(string originalPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(originalPath)) return false;
        var target = Normalize(originalPath);

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return false;

        object? shellObj = null;
        try
        {
            shellObj = Activator.CreateInstance(shellType);
            dynamic shell = shellObj!;
            dynamic bin = shell.NameSpace(RecycleBinFolder);
            if (bin is null) return false;

            foreach (dynamic item in bin.Items())
            {
                string? from = OriginalLocation(bin, item);
                if (from is null) continue;
                var name = (string)item.Name;
                var full = Normalize(System.IO.Path.Combine(from, name));
                // 名称可能因"隐藏已知扩展名"略去后缀 → 同时允许"父目录一致 + 文件名(去后缀)一致"。
                if (full == target ||
                    (Normalize(from) == ParentOf(target) && StemMatches(name, LeafOf(originalPath))))
                {
                    if (InvokeRestore(item)) return true;
                }
            }
            return false;
        }
        catch { return false; }
        finally
        {
            if (shellObj is not null)
                try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shellObj); } catch { }
        }
    }

    // 原始位置: 优先用与区域无关的扩展属性; 取不到再扫详情列里形如 X:\... 的值。
    private static string? OriginalLocation(dynamic bin, dynamic item)
    {
        try { string s = item.ExtendedProperty("System.Recycle.DeletedFromPath"); if (!string.IsNullOrEmpty(s)) return s; }
        catch { /* 某些系统不支持该属性 */ }
        for (var col = 0; col < 6; col++)
        {
            try { string s = bin.GetDetailsOf(item, col); if (!string.IsNullOrEmpty(s) && s.Contains(":\\")) return s; }
            catch { /* 列不可读 */ }
        }
        return null;
    }

    private static bool InvokeRestore(dynamic item)
    {
        try
        {
            foreach (dynamic verb in item.Verbs())
            {
                var n = ((string)verb.Name).Replace("&", "").Trim();
                foreach (var r in RestoreVerbs)
                    if (n.Equals(r, StringComparison.OrdinalIgnoreCase) || n.Contains(r, StringComparison.OrdinalIgnoreCase))
                    {
                        verb.DoIt();
                        return true;
                    }
            }
        }
        catch { /* 动词调用失败 → 交由上层回退 */ }
        return false;
    }

    private static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    private static string ParentOf(string normPath) { var i = normPath.LastIndexOf('\\'); return i > 0 ? normPath[..i] : normPath; }
    private static string LeafOf(string p) { var s = p.Replace('/', '\\').TrimEnd('\\'); var i = s.LastIndexOf('\\'); return i >= 0 ? s[(i + 1)..] : s; }

    private static bool StemMatches(string a, string b)
    {
        static string Stem(string x) { var d = x.LastIndexOf('.'); return (d > 0 ? x[..d] : x).ToLowerInvariant(); }
        return Stem(a) == Stem(b) || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
