using CleanScope.Core.Cleanup;
using CleanScope.Domain.Enums;

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
}
