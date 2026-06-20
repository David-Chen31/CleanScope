using System.ComponentModel;
using System.Diagnostics;

namespace CleanScope.Safety;

/// <summary>
/// 外壳交互端口 (辅助操作的副作用边界, 便于无头测试)。仅"打开/跳转/运行官方命令", **本身绝无删除代码**。
/// 运行清理命令 = 启动厂商/系统自己的清理工具 (如 conda clean、powercfg、DISM), 删除/变更由该工具完成。
/// </summary>
public interface IShellLauncher
{
    void OpenFolder(string path);          // 资源管理器打开/定位目录
    void OpenUri(string uri);              // 拉起系统设置 (ms-settings:)、URL 或自带 GUI 工具 (cleanmgr) —— 友好界面, 无控制台

    /// <summary>
    /// 隐藏执行一条官方清理命令 (受控白名单字面量), 等待结束并返回退出码 (0=成功; 非 0/无法启动/UAC 取消 → 非 0 或 -1)。
    /// 不弹原始 cmd 黑框; <paramref name="elevate"/>=true 时经标准 UAC 提权 (powercfg/DISM 需要)。
    /// </summary>
    int RunManaged(string command, bool elevate);
}

/// <summary>默认实现: 经 Shell 打开 (Windows)。只读取/跳转/启动官方工具, 不含任何删除调用。</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    // 隐藏执行 (无黑框); 需要管理员时经 ShellExecute "runas" 触发标准 UAC, 窗口隐藏。命令为受控字面量。
    public int RunManaged(string command, bool elevate)
    {
        var (file, args) = SplitCommand(command);
        if (string.IsNullOrEmpty(file)) return -1;

        var psi = new ProcessStartInfo(file, args);
        if (elevate)
        {
            psi.UseShellExecute = true;          // runas 需要 ShellExecute
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
        }
        else
        {
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;           // 静默执行, 不弹控制台
        }

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception)
        {
            return -1;   // 用户在 UAC 点了"否", 或无法启动 —— 视为未执行 (调用方报失败并提示重试)
        }
        catch
        {
            return -1;
        }
    }

    // 取首个 token 作为可执行文件名, 其余为参数 (官方命令的可执行名均无空格: powercfg/Dism.exe/powershell/cleanmgr)。
    private static (string File, string Args) SplitCommand(string command)
    {
        var s = (command ?? string.Empty).Trim();
        if (s.Length == 0) return (string.Empty, string.Empty);
        var i = s.IndexOf(' ');
        return i < 0 ? (s, string.Empty) : (s[..i], s[(i + 1)..]);
    }
}
