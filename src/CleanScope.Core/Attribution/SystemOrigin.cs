namespace CleanScope.Core.Attribution;

/// <summary>
/// 系统/共享路径的来源与用途解析 (确定性, 非 AI)。
///
/// 目标 (本项目核心): 让每个目录都"知道从何而来、有什么用" —— 没有第三方应用归属的系统文件,
/// 不该落进"未归类", 而应标为「Windows 系统」「共享组件」「临时文件」等**来源**, 并给出**用途**
/// (如 pagefile=虚拟内存、WinSxS=组件存储、Temp=临时文件)。
///
/// 这些都是公认的固定路径, 故用确定性表覆盖, 优先于路径段猜测。返回 (来源, 用途) 或 null。
/// </summary>
public static class SystemOrigin
{
    public const string WindowsOwner = "Windows 系统";

    // \Windows\<片段> → 用途 (来源统一为 Windows 系统)。按特异性从具体到笼统排列, 取首个命中。
    private static readonly (string Fragment, string Purpose)[] WindowsSubdirs =
    {
        (@"\winsxs", "组件存储 (WinSxS, 仅用 DISM 清理)"),
        (@"\installer", "MSI 安装缓存 (修复/卸载所需, 勿直删)"),
        (@"\softwaredistribution", "Windows 更新下载缓存"),
        (@"\servicing", "Windows 更新与维护数据"),
        (@"\assembly", ".NET 本机映像缓存"),
        (@"\microsoft.net", ".NET 运行时"),
        (@"\system32\driverstore", "驱动程序仓库 (用设备管理器/pnputil 管理)"),
        (@"\system32\config", "注册表与系统配置"),
        (@"\system32", "系统核心组件"),
        (@"\syswow64", "32 位系统组件"),
        (@"\systemapps", "系统内置应用"),
        (@"\fonts", "系统字体 (经字体设置管理)"),
        (@"\temp", "系统临时文件 (通常可清理)"),
        (@"\logs", "系统日志"),
        (@"\panther", "系统安装/升级日志"),
        (@"\boot", "启动文件"),
    };

    /// <summary>解析路径的系统来源与用途; 非系统/共享路径返回 null (交回普通归因)。</summary>
    public static (string Owner, string Purpose)? Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var p = path.Replace('/', '\\').TrimEnd('\\');
        var lower = p.ToLowerInvariant();
        var name = lower[(lower.LastIndexOf('\\') + 1)..];

        // 1) 整盘特殊文件。
        switch (name)
        {
            case "pagefile.sys": return (WindowsOwner, "虚拟内存页面文件 (经 系统>高级>虚拟内存 调整)");
            case "hiberfil.sys": return (WindowsOwner, "休眠文件 (关闭休眠可移除)");
            case "swapfile.sys": return (WindowsOwner, "Windows 交换文件");
        }

        // 2) 顶层特殊目录。
        if (lower.Contains(@"\$recycle.bin")) return ("回收站", "已删除文件的回收站 (可清空)");
        if (lower.Contains(@"\system volume information")) return (WindowsOwner, "系统还原点 / 卷影副本");

        // 3) 系统 Windows 目录 → 具体用途; 否则笼统 Windows 系统文件。
        //    必须锚定盘符根下的 \Windows (如 C:\Windows), 即 ":\windows"。否则 "\windows" 会误命中
        //    用户目录里的同名子目录 (如 ...\AppData\Local\Microsoft\Windows), 把用户数据错标成系统文件。
        const string winMarker = @":\windows";
        int winIdx = lower.IndexOf(winMarker, StringComparison.Ordinal);
        if (winIdx >= 0)
        {
            var afterRoot = lower[(winIdx + winMarker.Length)..];   // \Windows 之后 (含前导 '\'), 整盘根则为 ""
            if (afterRoot.Length == 0 || afterRoot[0] == '\\')      // 必须是 \Windows 这一段本身, 排除 windows.old/windowsapps 等
            {
                foreach (var (frag, purpose) in WindowsSubdirs)
                    if (afterRoot.StartsWith(frag, StringComparison.Ordinal)) return (WindowsOwner, purpose);
                return (WindowsOwner, "Windows 系统文件");
            }
        }

        // 4) 安装器/更新的包缓存。
        if (lower.Contains(@"\package cache")) return (WindowsOwner, "安装包缓存 (经官方安装器管理)");

        // 5) 共享组件 (多程序共用)。仅当 Common Files 为末段时 (更深的 X 交回普通归因, 如 Adobe)。
        if (lower.EndsWith(@"\common files")) return ("共享组件 (多程序)", "多个程序共用的库/运行时");

        // 6) 用户目录下的通用容器 —— 仅匹配容器目录"本身"(EndsWith), 其更深子目录交回普通归因
        //    (如 Packages\<家族>、Temp\<App> 由路径推断/AI 识别具体应用, 不被笼统标签淹没)。
        if (lower.EndsWith(@"\appdata\local\packages")) return ("Windows 应用商店应用", "UWP/商店应用的本地数据与缓存");
        if (lower.EndsWith(@"\appdata\local\programs")) return ("用户级安装的程序", "免管理员安装到用户目录的程序");
        if (lower.EndsWith(@"\appdata\local\temp"))
            return ("临时文件", "各程序运行时的临时文件 (通常可安全清理)");

        return null;
    }
}
