using System.Diagnostics;

namespace CleanScope.Safety;

/// <summary>外壳交互端口 (辅助操作的副作用边界, 便于无头测试)。仅"打开/跳转", 绝无删除。</summary>
public interface IShellLauncher
{
    void OpenFolder(string path);   // 资源管理器打开/定位目录
    void OpenUri(string uri);       // 跳转系统设置 (ms-settings:) 或 URL
}

/// <summary>默认实现: 经 Shell 打开 (Windows)。只读取/跳转, 不修改任何文件。</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
