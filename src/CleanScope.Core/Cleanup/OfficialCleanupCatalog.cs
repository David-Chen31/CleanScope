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
                Note: "需要管理员。关闭后休眠与快速启动将不可用。",
                Reversible: true,
                Undo: "以管理员运行 powercfg /h on 即可重新开启休眠（hiberfil.sys 会重新生成）。",
                Consequence: "删除休眠文件 hiberfil.sys 并停用“休眠/快速启动”功能（不影响关机、睡眠、你的任何数据）。"),

            // 清空回收站: PowerShell 官方 cmdlet, 隐藏执行。注意 powershell.exe 只要有错误记录 (含已被
            // -ErrorAction 抑制的) 进程退出码就是 1——回收站本就为空时 Clear-RecycleBin 会报"回收站为空", 据此
            // 误判失败。空回收站正是期望的结果, 故 try/catch 吞掉并显式 exit 0; 实际释放量由前后可用空间差如实反映。
            new("empty-recyclebin",
                "清空回收站",
                "永久删除回收站中的项以释放空间（此操作不可还原, 请先确认无误删）。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand,
                "powershell -NoProfile -Command \"try { Clear-RecycleBin -Force -ErrorAction Stop } catch { }; exit 0\"",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "清空后无法从回收站还原, 请确认其中没有需要的文件。",
                Reversible: false,
                Undo: "无法撤销。如担心误删, 建议先打开回收站逐项确认后再清空。",
                Consequence: "永久删除回收站里的全部文件（不再可还原）。不影响回收站以外的任何文件。"),

            // WinSxS 组件清理: 黑名单严禁手删, 唯一安全方式是 DISM 官方命令。
            new("dism-component-cleanup",
                "清理组件存储（WinSxS, DISM）",
                "用 Windows 官方 DISM 安全清理 WinSxS 中被取代的旧组件, 切勿手动删除该目录。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand,
                "Dism.exe /Online /Cleanup-Image /StartComponentCleanup",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: true,
                Note: "需要管理员, 过程可能较久。这是清理 WinSxS 的唯一官方安全方式。",
                Reversible: false,
                Undo: "无法逐项撤销, 但只清理“已被更新取代、不再需要”的旧组件, 不影响系统正常运行与现有功能。",
                Consequence: "删除 WinSxS 里被新版本取代的旧组件副本；清理后已安装的更新将无法卸载回退。系统功能不受影响。"),

            // 磁盘清理 cleanmgr: 经典官方工具, 覆盖更新缓存/缩略图/旧系统等。
            new("disk-cleanup",
                "打开磁盘清理（cleanmgr）",
                "Windows 自带磁盘清理, 可勾选更新缓存、缩略图、传递优化文件等安全项。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand, "cleanmgr",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "在弹出的界面中勾选要清理的类别后确认。",
                Reversible: false,
                Undo: "由你在磁盘清理界面勾选要删的类别, 删除的临时/缓存文件无法还原 (但多可由系统重新生成)。",
                Consequence: "仅“打开”Windows 磁盘清理工具，不会自动删除任何东西——删什么由你在它界面里勾选后确认。",
                Surface: CleanupSurface.OpensWindowsUi),

            // 优化驱动器 (碎片整理 / SSD TRIM): 用户常说的"磁盘整理/合并零散空间"。Windows 自带「优化驱动器」
            // 会自动识别盘类型——机械盘做碎片整理(合并空闲块)、固态盘发 TRIM (绝不能对 SSD 强行碎片整理, 否则
            // 徒增写入、折寿)。这是官方且安全的做法; 我们只"拉起"它, 不替它动盘。它不释放空间, 属维护类。
            new("optimize-drives",
                "优化驱动器（碎片整理 / SSD TRIM）",
                "Windows 自带「优化驱动器」：机械硬盘(HDD)做碎片整理、合并零散空闲块；固态硬盘(SSD)发送 TRIM。它会自动识别盘类型、选对方式。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand, "dfrgui",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "不删除文件、不直接释放空间, 只整理布局/性能。SSD 切勿手动碎片整理(只需 TRIM)；Windows 默认每周已自动优化一次, 通常无需手动。",
                Reversible: true,
                Undo: "无需撤销：优化不删除任何数据, 只是重新整理文件布局或对 SSD 发送 TRIM 指令。",
                Consequence: "仅“打开”Windows「优化驱动器」工具：HDD 做碎片整理(合并零散空闲块)、SSD 做 TRIM；不删除任何文件、不直接释放空间。优化哪个盘、是否优化由你在它界面里决定。",
                Surface: CleanupSurface.OpensWindowsUi),

            // 存储感知: 现代设置页, 可一劳永逸自动清理临时/回收站。
            new("storage-sense",
                "打开存储感知设置",
                "开启后 Windows 会自动清理临时文件与回收站, 长期省心。",
                CleanupActionKind.OpenFolder, ActionType.OpenSettings, "ms-settings:storagesense",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "建议开启自动清理, 之后无需手动维护。",
                Reversible: true,
                Undo: "随时可在同一设置页关闭“存储感知”。",
                Consequence: "仅“打开”系统设置页面，不删除任何东西——是否开启自动清理由你决定。",
                Surface: CleanupSurface.OpensWindowsUi),
        };

        // 仅在检测到 Windows.old 时提供 (旧系统备份, 经磁盘清理删除, 不可手删)。
        if (hasWinOld)
            list.Add(new OfficialCleanupAction(
                "remove-windows-old",
                "删除旧系统备份（Windows.old）",
                "升级后遗留的旧系统备份常占数 GB, 经磁盘清理「以前的 Windows 安装」安全删除。",
                CleanupActionKind.RunCommand, ActionType.RunCleanupCommand, "cleanmgr",
                EstimatedBytes: 0, Detected: true, NeedsAdmin: false,
                Note: "在磁盘清理中勾选「以前的 Windows 安装」。删除后无法回退到旧版本。",
                Reversible: false,
                Undo: "删除后无法再回退到升级前的旧 Windows 版本。10 天回退期内若想保留请勿删。",
                Consequence: "删除升级遗留的旧系统备份 Windows.old（常数 GB）。删后无法用它回退到旧版本，但不影响当前系统。",
                Surface: CleanupSurface.OpensWindowsUi));

        // 检测到的、预估收益大的排前 (休眠优先), 其余按目录原序。
        return list
            .OrderByDescending(a => a.Detected)
            .ThenByDescending(a => a.EstimatedBytes)
            .ToList();
    }
}
