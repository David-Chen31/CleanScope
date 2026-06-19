using System.IO;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Cleanup;

/// <summary>
/// 系统级官方清理手段目录 (P0, 确定性·非 AI)。回答"网上常见的 C 盘清理手段, 本机能做哪些、各能省多少"——
/// 关闭休眠、清空回收站、组件清理 (DISM)、删除 Windows.old、磁盘清理 (cleanmgr)、存储感知。
///
/// 这些不是逐文件规则命中, 而是机器层面的机会。本目录只做两件事: (1) 确定性**检测**本机是否适用 + 预估收益;
/// (2) 给出对应的**官方命令/设置跳转** (受控白名单字面量, 绝不拼接用户/AI 输入)。执行仍走安全闸门 + 审计,
/// 且全部是"启动 Windows 自带工具"而非我们替它删文件 (兑现竞品 FAQ 那句"优先用系统自带")。
/// </summary>
public static class OfficialCleanupCatalog
{
    /// <summary>检测探针 (可注入, 便于无文件系统的单测)。FileSize: 不存在→0; DirExists: 目录是否存在。</summary>
    public sealed record Probe(Func<string, long> FileSize, Func<string, bool> DirExists);

    public static Probe RealProbe { get; } = new(
        path => { try { var fi = new FileInfo(path); return fi.Exists ? fi.Length : 0; } catch { return 0; } },
        path => { try { return Directory.Exists(path); } catch { return false; } });

    /// <summary>系统盘根 (如 C:\)。无法判定时回退 C:\。</summary>
    public static string SystemDrive()
        => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    /// <summary>用真实文件系统探针构建本机适用的清理手段 (检测项在前、预估大的在前)。</summary>
    public static IReadOnlyList<OfficialCleanupAction> BuildForThisMachine()
        => Build(SystemDrive(), RealProbe);

    /// <summary>构建目录。<paramref name="systemDrive"/> 形如 "C:\"; <paramref name="probe"/> 缺省用真实文件系统。</summary>
    public static IReadOnlyList<OfficialCleanupAction> Build(string systemDrive, Probe? probe = null)
    {
        probe ??= RealProbe;
        if (string.IsNullOrWhiteSpace(systemDrive)) systemDrive = @"C:\";

        var hiberSize = probe.FileSize(Path.Combine(systemDrive, "hiberfil.sys"));
        var hasWinOld = probe.DirExists(Path.Combine(systemDrive, "Windows.old"));

        var list = new List<OfficialCleanupAction>
        {
            // 关闭休眠: 唯一能给精确数值的"大头"——hiberfil.sys ≈ 物理内存大小, 常 4–16GB, 一条命令立省。
            new("disable-hibernation",
                "关闭休眠（移除 hiberfil.sys）",
                "休眠文件约等于物理内存大小, 常占数 GB。关闭后立即释放。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand, "powercfg /h off",
                EstimatedBytes: hiberSize, Detected: hiberSize > 0, NeedsAdmin: true,
                Note: "需要管理员。关闭后休眠与快速启动将不可用; 需要时可用 powercfg /h on 恢复。"),

            // 清空回收站: 用 PowerShell 官方 cmdlet, 终端可见; 仍是"移走"到永久删除由系统完成, 用户主动确认。
            new("empty-recyclebin",
                "清空回收站",
                "永久删除回收站中的项以释放空间（此操作不可还原, 请先确认无误删）。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand,
                "powershell -NoProfile -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "清空后无法从回收站还原, 请确认其中没有需要的文件。"),

            // WinSxS 组件清理: 黑名单严禁手删, 唯一安全方式是 DISM 官方命令。
            new("dism-component-cleanup",
                "清理组件存储（WinSxS, DISM）",
                "用 Windows 官方 DISM 安全清理 WinSxS 中被取代的旧组件, 切勿手动删除该目录。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand,
                "Dism.exe /Online /Cleanup-Image /StartComponentCleanup",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: true,
                Note: "需要管理员, 过程可能较久。这是清理 WinSxS 的唯一官方安全方式。"),

            // 磁盘清理 cleanmgr: 经典官方工具, 覆盖更新缓存/缩略图/旧系统等。
            new("disk-cleanup",
                "打开磁盘清理（cleanmgr）",
                "Windows 自带磁盘清理, 可勾选更新缓存、缩略图、传递优化文件等安全项。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand, "cleanmgr",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "在弹出的界面中勾选要清理的类别后确认。"),

            // 存储感知: 现代设置页, 可一劳永逸自动清理临时/回收站。
            new("storage-sense",
                "打开存储感知设置",
                "开启后 Windows 会自动清理临时文件与回收站, 长期省心。",
                CleanupActionKind.OpenFolder, ActionType.OpenSettings, "ms-settings:storagesense",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "建议开启自动清理, 之后无需手动维护。"),
        };

        // 仅在检测到 Windows.old 时提供 (旧系统备份, 经磁盘清理删除, 不可手删)。
        if (hasWinOld)
            list.Add(new OfficialCleanupAction(
                "remove-windows-old",
                "删除旧系统备份（Windows.old）",
                "升级后遗留的旧系统备份常占数 GB, 经磁盘清理「以前的 Windows 安装」安全删除。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand, "cleanmgr",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "在磁盘清理中勾选「以前的 Windows 安装」。删除后无法回退到旧版本。"));

        // 检测到的、预估收益大的排前 (休眠优先), 其余按目录原序。
        return list
            .OrderByDescending(a => a.Detected)
            .ThenByDescending(a => a.EstimatedBytes)
            .ToList();
    }
}
