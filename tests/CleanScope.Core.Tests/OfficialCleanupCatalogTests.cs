using CleanScope.Core.Cleanup;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Core.Tests;

// P0: 系统级官方清理手段目录 —— 确定性检测 + 受控官方命令; AI 不参与。
public sealed class OfficialCleanupCatalogTests
{
    private static OfficialCleanupCatalog.Probe Probe(long hiberSize, bool hasWinOld) => new(
        path => path.EndsWith("hiberfil.sys", StringComparison.OrdinalIgnoreCase) ? hiberSize : 0,
        path => hasWinOld && path.EndsWith("Windows.old", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Hibernation_savings_come_from_detected_hiberfil_size()
    {
        var list = OfficialCleanupCatalog.Build(@"C:\", Probe(hiberSize: 8L * 1024 * 1024 * 1024, hasWinOld: false));
        var hiber = list.Single(a => a.Id == "disable-hibernation");

        Assert.True(hiber.Detected);
        Assert.Equal(8L * 1024 * 1024 * 1024, hiber.EstimatedBytes);
        Assert.Equal("powercfg /h off", hiber.Payload);
        Assert.Equal(ActionType.RunCleanupCommand, hiber.ExecAction);
        Assert.True(hiber.NeedsAdmin);
        // 检测到的大头排在最前。
        Assert.Equal("disable-hibernation", list[0].Id);
    }

    [Fact]
    public void Hibernation_not_detected_when_no_hiberfil()
    {
        var list = OfficialCleanupCatalog.Build(@"C:\", Probe(hiberSize: 0, hasWinOld: false));
        Assert.False(list.Single(a => a.Id == "disable-hibernation").Detected);
    }

    [Fact]
    public void Windows_old_action_only_present_when_detected()
    {
        Assert.DoesNotContain(OfficialCleanupCatalog.Build(@"C:\", Probe(0, hasWinOld: false)),
            a => a.Id == "remove-windows-old");
        Assert.Contains(OfficialCleanupCatalog.Build(@"C:\", Probe(0, hasWinOld: true)),
            a => a.Id == "remove-windows-old");
    }

    [Fact]
    public void Always_offers_recyclebin_dism_cleanmgr_storagesense()
    {
        var ids = OfficialCleanupCatalog.Build(@"C:\", Probe(0, false)).Select(a => a.Id).ToHashSet();
        Assert.Contains("empty-recyclebin", ids);
        Assert.Contains("dism-component-cleanup", ids);
        Assert.Contains("disk-cleanup", ids);
        Assert.Contains("storage-sense", ids);
    }

    [Fact] // 优化驱动器: 拉起 Windows「优化驱动器」(dfrgui), 自动 SSD TRIM / HDD 碎片整理; 维护类、不删文件、可逆。
    public void Optimize_drives_opens_windows_tool_and_is_reversible()
    {
        var opt = OfficialCleanupCatalog.Build(@"C:\", Probe(0, false)).Single(a => a.Id == "optimize-drives");
        Assert.Equal("dfrgui", opt.Payload);
        Assert.Equal(CleanupSurface.OpensWindowsUi, opt.Surface);
        Assert.True(opt.Reversible);
        Assert.Equal(0, opt.EstimatedBytes);   // 优化不释放空间 (维护类)
    }

    [Fact]
    public void Storage_sense_is_a_settings_jump_not_a_command()
    {
        var ss = OfficialCleanupCatalog.Build(@"C:\", Probe(0, false)).Single(a => a.Id == "storage-sense");
        Assert.Equal(ActionType.OpenSettings, ss.ExecAction);
        Assert.StartsWith("ms-settings:", ss.Payload);
    }

    [Fact] // 全部 payload 都是固定字面量 (白名单), 不含占位/拼接痕迹。
    public void All_payloads_are_nonempty_literals()
        => Assert.All(OfficialCleanupCatalog.Build(@"C:\", Probe(0, true)),
            a => Assert.False(string.IsNullOrWhiteSpace(a.Payload)));

    [Fact] // 问题#3: 每条都标清"后果"且不可逆项给出说明 (让用户点之前心里有数)。
    public void Every_action_explains_consequence_and_irreversible_ones_warn()
    {
        var list = OfficialCleanupCatalog.Build(@"C:\", Probe(8L * 1024 * 1024 * 1024, hasWinOld: true));
        Assert.All(list, a => Assert.False(string.IsNullOrWhiteSpace(a.Consequence)));
        Assert.All(list, a => Assert.False(string.IsNullOrWhiteSpace(a.Undo)));

        // 关闭休眠可逆且给出 powercfg /h on 恢复方式; 清空回收站不可逆。
        var hiber = list.Single(a => a.Id == "disable-hibernation");
        Assert.True(hiber.Reversible);
        Assert.Contains("powercfg /h on", hiber.Undo);

        Assert.False(list.Single(a => a.Id == "empty-recyclebin").Reversible);
    }

    [Fact] // 回归: 空回收站会让 powershell 退出码=1 (即便 -ErrorAction 抑制了报错); 清空命令须吞错并 exit 0,
           // 否则回收站本就为空时被误判为"命令未成功执行(退出码1)"。
    public void Empty_recyclebin_command_swallows_empty_bin_error_and_exits_zero()
    {
        var payload = OfficialCleanupCatalog.Build(@"C:\", Probe(0, false))
            .Single(a => a.Id == "empty-recyclebin").Payload;

        Assert.Contains("Clear-RecycleBin", payload);
        Assert.Contains("catch", payload);     // 吞掉"回收站为空"等错误
        Assert.Contains("exit 0", payload);    // 显式成功退出码, 不再被退出码 1 误判
        Assert.DoesNotContain("SilentlyContinue", payload); // 抑制报错并不能避免退出码 1
    }

    [Fact] // 执行表面: GUI 工具拉起 Windows 界面; 静默命令应用内执行 (避免 cmd 黑框)。
    public void Execution_surface_is_assigned_correctly()
    {
        var list = OfficialCleanupCatalog.Build(@"C:\", Probe(8L * 1024 * 1024 * 1024, hasWinOld: true));
        OfficialCleanupAction A(string id) => list.Single(a => a.Id == id);

        // 自带 GUI / 设置页 → 打开 Windows 界面
        Assert.Equal(CleanupSurface.OpensWindowsUi, A("disk-cleanup").Surface);
        Assert.Equal(CleanupSurface.OpensWindowsUi, A("storage-sense").Surface);
        Assert.Equal(CleanupSurface.OpensWindowsUi, A("remove-windows-old").Surface);

        // 无界面命令 → 应用内隐藏执行
        Assert.Equal(CleanupSurface.ManagedRun, A("disable-hibernation").Surface);
        Assert.Equal(CleanupSurface.ManagedRun, A("empty-recyclebin").Surface);
        Assert.Equal(CleanupSurface.ManagedRun, A("dism-component-cleanup").Surface);
    }
}
