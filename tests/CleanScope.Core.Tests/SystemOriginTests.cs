using CleanScope.Core.Attribution;

namespace CleanScope.Core.Tests;

// 核心目标: 系统/共享文件"知道从何而来、有什么用" —— 不落进"未归类"。
public sealed class SystemOriginTests
{
    [Theory]
    [InlineData(@"C:\pagefile.sys", "Windows 系统", "虚拟内存")]
    [InlineData(@"C:\hiberfil.sys", "Windows 系统", "休眠")]
    [InlineData(@"C:\Windows\WinSxS", "Windows 系统", "组件存储")]
    [InlineData(@"C:\Windows\Installer", "Windows 系统", "安装缓存")]
    [InlineData(@"C:\Windows\System32", "Windows 系统", "系统核心")]
    [InlineData(@"C:\Windows\System32\DriverStore\FileRepository\x.inf", "Windows 系统", "驱动")]
    [InlineData(@"C:\Windows\Temp", "Windows 系统", "临时")]
    [InlineData(@"C:\Windows", "Windows 系统", "Windows 系统文件")]
    [InlineData(@"C:\Program Files\Common Files", "共享组件 (多程序)", "共用")]
    [InlineData(@"C:\Users\me\AppData\Local\Packages", "Windows 应用商店应用", "商店应用")]
    [InlineData(@"C:\Users\me\AppData\Local\Programs", "用户级安装的程序", "免管理员")]
    [InlineData(@"C:\Users\me\AppData\Local\Temp", "临时文件", "临时文件")]
    public void Resolves_owner_and_purpose(string path, string owner, string purposeFragment)
    {
        var r = SystemOrigin.Resolve(path);
        Assert.NotNull(r);
        Assert.Equal(owner, r!.Value.Owner);
        Assert.Contains(purposeFragment, r.Value.Purpose);
    }

    [Theory] // 普通第三方路径 / 根 → 不命中 (交回普通归因)
    [InlineData(@"C:\Users\me\AppData\Roaming\Tencent\QQ")]
    [InlineData(@"C:\some\random\file.dat")]
    [InlineData(@"C:\")]
    public void Non_system_paths_return_null(string path)
        => Assert.Null(SystemOrigin.Resolve(path));
}
