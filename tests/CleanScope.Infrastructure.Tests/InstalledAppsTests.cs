using CleanScope.Infrastructure.Windows;

namespace CleanScope.Infrastructure.Tests;

// T2.2: GetInstalledApplications —— 只读注册表 Uninstall 的集成测试 (真机)。
public sealed class InstalledAppsTests
{
    [Fact]
    public void Returns_nonempty_well_formed_app_list()
    {
        var apps = new WindowsAccess().GetInstalledApplications();

        // 任何 Windows 机器的 Uninstall 表都至少有若干带 DisplayName 的项。
        Assert.NotEmpty(apps);
        Assert.All(apps, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));   // 过滤掉无名项
            Assert.Equal("Registry", a.Source);
        });
    }

    [Fact]
    public void Deduplicates_by_name_and_location()
    {
        var apps = new WindowsAccess().GetInstalledApplications();
        var keys = apps.Select(a => a.Name + "|" + (a.InstallLocation ?? "")).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Read_does_not_throw_and_is_repeatable()
    {
        var wa = new WindowsAccess();
        var a = wa.GetInstalledApplications();
        var b = wa.GetInstalledApplications();
        Assert.Equal(a.Count, b.Count);   // 只读, 幂等
    }
}
