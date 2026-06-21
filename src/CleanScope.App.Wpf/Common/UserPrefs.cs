using System.IO;
using System.Text.Json;

namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 本地用户偏好 (仅本地 JSON, %LocalAppData%\CleanScope\prefs.json)。
/// 承载: 主题 (E)、窗口尺寸/位置 (C)、上次扫描目标/管理员模式 (C)、首启引导是否看过 (D)。
/// 读写容错: 损坏/缺失静默回退默认, 写入失败不抛 (偏好丢失无关紧要)。
/// </summary>
public sealed class UserPrefs
{
    // —— E: 主题 ——
    public string Theme { get; set; } = "Light";   // Light / Dark

    // —— C: 窗口几何 ——
    public double WinWidth { get; set; }
    public double WinHeight { get; set; }
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public bool WinMaximized { get; set; }

    // —— C: 上次扫描 ——
    public string? LastScanPath { get; set; }
    public bool AdminMode { get; set; }

    // —— D: 首启引导 ——
    public bool OnboardingSeen { get; set; }

    // —— H: 最近扫描历史 (最多 8 条, 最近在前, 按目标去重) ——
    public List<ScanHistoryEntry> RecentScans { get; set; } = new();

    /// <summary>记一次扫描 (去重置顶, 截断到 8 条) 并保存。</summary>
    public void AddScan(string target, long totalSize, long fileCount)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        RecentScans.RemoveAll(s => string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase));
        RecentScans.Insert(0, new ScanHistoryEntry
        {
            Target = target, TotalSize = totalSize, FileCount = fileCount, WhenUtc = DateTime.UtcNow,
        });
        if (RecentScans.Count > 8) RecentScans.RemoveRange(8, RecentScans.Count - 8);
        Save();
    }

    public bool HasWindowSize => WinWidth >= 320 && WinHeight >= 320;
    public bool HasWindowPos => !double.IsNaN(WinLeft) && !double.IsNaN(WinTop);

    private static readonly string PathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanScope", "prefs.json");

    private static UserPrefs? _current;
    public static UserPrefs Current => _current ??= Load();

    private static UserPrefs Load()
    {
        try
        {
            if (File.Exists(PathFile))
                return JsonSerializer.Deserialize<UserPrefs>(File.ReadAllText(PathFile)) ?? new UserPrefs();
        }
        catch { /* 损坏/不可读 → 默认 */ }
        return new UserPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 写入失败不抛 */ }
    }
}

/// <summary>H: 一条扫描历史 (目标 / 总占用 / 项数 / 时间)。</summary>
public sealed class ScanHistoryEntry
{
    public string Target { get; set; } = "";
    public long TotalSize { get; set; }
    public long FileCount { get; set; }
    public DateTime WhenUtc { get; set; }
}
