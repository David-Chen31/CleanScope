using CleanScope.Core.Attribution;

namespace CleanScope.Core.Tests;

// 核心目标: 每个容器目录都"知道是什么、干什么" —— 短标签(列) + 完整描述(说明)。
public sealed class ContainerPurposeTests
{
    [Theory]
    [InlineData(@"C:\Users\28170\AppData\Roaming", "应用配置·漫游", "漫游")]
    [InlineData(@"C:\Users\28170\AppData\Local", "应用数据·本机", "不随账户漫游")]
    [InlineData(@"C:\Users\28170\AppData\LocalLow", "应用数据·低权限", "低完整性")]
    [InlineData(@"C:\Users\28170\AppData", "应用数据根", "配置")]
    [InlineData(@"C:\Users\28170", "用户主目录", "用户主目录")]
    [InlineData(@"C:\Users", "所有用户", "所有用户")]
    [InlineData(@"C:\Program Files", "程序·64位", "64 位")]
    [InlineData(@"C:\Program Files (x86)", "程序·32位", "32 位")]
    [InlineData(@"C:\ProgramData", "共享应用数据", "所有用户共享")]
    [InlineData(@"C:\", "磁盘根目录", "根目录")]
    public void Describes_each_container(string path, string shortLabel, string fullFragment)
    {
        var r = ContainerPurpose.Describe(path);
        Assert.NotNull(r);
        Assert.Equal(shortLabel, r!.Value.Short);
        Assert.Contains(fullFragment, r.Value.Full);
    }

    [Theory] // 非容器 (具体子目录/文件) → null, 交回普通归因/系统来源
    [InlineData(@"C:\Users\28170\AppData\Roaming\Code")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Users\28170\Documents")]
    public void Non_containers_return_null(string path)
        => Assert.Null(ContainerPurpose.Describe(path));
}
