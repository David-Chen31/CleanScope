using System.Diagnostics;

namespace CleanScope.Safety;

/// <summary>
/// 外壳交互端口 (辅助操作的副作用边界, 便于无头测试)。仅"打开/跳转/运行官方命令", **本身绝无删除代码**。
/// 运行清理命令 = 启动厂商自己的清理工具 (如 conda clean), 删除由该工具完成, 命令对用户可见。
/// </summary>
public interface IShellLauncher
{
    void OpenFolder(string path);          // 资源管理器打开/定位目录
    void OpenUri(string uri);              // 跳转系统设置 (ms-settings:) 或 URL
    void RunInTerminal(string command);    // 在可见终端运行官方清理命令 (用户可见、可中断)
}

/// <summary>默认实现: 经 Shell 打开 (Windows)。只读取/跳转/启动官方工具, 不含任何删除调用。</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    // /k: 运行后保留窗口, 让用户看到结果并可关闭; 命令来自规则包 (受控), 非用户/AI 任意输入。
    public void RunInTerminal(string command) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k {command}") { UseShellExecute = true });
}
