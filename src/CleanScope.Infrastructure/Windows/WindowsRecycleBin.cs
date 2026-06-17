using System.Runtime.Versioning;
using CleanScope.Domain.Abstractions;
using Microsoft.VisualBasic.FileIO;

namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// 回收站删除 (<see cref="IRecycleBin"/> 的 Windows 实现)。
///
/// 红线 (IR-1/SR-3): 本文件是整个仓库**唯一**触碰删除 API 的地方, 且**仅用可恢复接口**
/// <see cref="RecycleOption.SendToRecycleBin"/> —— 绝不使用任何永久删除选项或永久删除 API。
/// 删除后内容进 Windows 回收站, 用户可一键还原。
///
/// 静态门禁 <c>NoPermanentDeleteTests</c> 对本文件单独豁免 (因含 VisualBasic 的 Delete* 调用),
/// 并对本文件**正向断言**: 必须使用回收站可恢复选项、绝不出现任何永久删除选项。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBin : IRecycleBin
{
    public void Send(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("回收站删除仅支持 Windows。");

        // 可恢复删除: 移入回收站。出错弹系统对话框; 用户取消 → 抛异常 (绝不静默永久删除)。
        if (Directory.Exists(path))
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        else if (File.Exists(path))
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        else
            throw new FileNotFoundException("目标不存在, 未执行任何删除。", path);
    }
}
