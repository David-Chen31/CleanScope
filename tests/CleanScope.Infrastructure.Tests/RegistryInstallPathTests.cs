using CleanScope.Infrastructure.Windows;

namespace CleanScope.Infrastructure.Tests;

// E2: 从 DisplayIcon 反推安装目录的解析。
public sealed class RegistryInstallPathTests
{
    [Theory]
    [InlineData(@"""C:\Program Files\App\app.exe"",0", @"C:\Program Files\App")]
    [InlineData(@"C:\Program Files\App\app.exe,0", @"C:\Program Files\App")]
    [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\App")]
    [InlineData(@"""C:\Users\me\AppData\Local\App\app.exe""", @"C:\Users\me\AppData\Local\App")]
    [InlineData(@"C:\App\icon.ico,-12", @"C:\App")]
    public void Derives_directory_from_display_icon(string displayIcon, string expectedDir)
        => Assert.Equal(expectedDir, RegistryInstallPath.FromDisplayIcon(displayIcon));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_returns_null(string? input)
        => Assert.Null(RegistryInstallPath.FromDisplayIcon(input));
}
