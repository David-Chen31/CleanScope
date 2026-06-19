using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using CleanScope.Domain.Abstractions;

namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// 目录联接创建 (<see cref="IJunctionCreator"/> 的 Windows 实现)。用 <c>mklink /J</c> ——
/// 目录联接 (junction) 不需管理员权限, 对应用透明 (程序仍按原路径访问, 实际落在目标盘)。
/// 本类只创建重解析点, **绝无任何删除调用**。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsJunctionCreator : IJunctionCreator
{
    public void Create(string linkPath, string targetDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);
        if (Directory.Exists(linkPath) || File.Exists(linkPath))
            throw new IOException($"联接位置已存在, 未创建: {linkPath}");

        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new IOException("无法启动 mklink。");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();

        // 双重确认: 退出码 0 且确实成为重解析点。
        var created = Directory.Exists(linkPath)
            && File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint);
        if (p.ExitCode != 0 || !created)
            throw new IOException($"创建目录联接失败 (exit {p.ExitCode}): {err}");
    }
}
