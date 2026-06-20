using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Safety;

namespace CleanScope.Safety.Tests;

// S-E: 启用删除能力 (deleteEnabled=true) 后的准入 —— 仅"可清理"桶 (A/B)、非黑名单/非容器/未占用项放行,
// 且语义上仅"移入回收站 (可恢复)"; 其余一律拒。红线: 系统关键/容器/C-E/占用必拒。
public sealed class SafetyGuardDeleteEnabledTests
{
    private static ActionRequest Delete(string path) => new(1, path, ActionType.MoveToRecycleBin);
    private static ActionRequest Manual(string path) => new(1, path, ActionType.MoveToRecycleBin, UserOverride: true);

    private static RiskAssessment Risk(RiskLevel level, bool isContainer = false) =>
        new(0, 0, level, 50, new[] { "f" }, new[] { 1L }, level == RiskLevel.A, 0.8, default, isContainer);

    private static SafetyGuard Enabled(FakeWindowsAccess? win = null) =>
        new(win ?? new FakeWindowsAccess(), deleteEnabled: true);

    [Theory] // A/B 可清理项 (非黑名单/非容器/未占用) → 放行 (仅回收站可恢复)
    [InlineData(RiskLevel.A)]
    [InlineData(RiskLevel.B)]
    public void Cleanable_AB_allowed_when_enabled(RiskLevel level)
    {
        var d = Enabled().Evaluate(Delete(@"C:\Users\me\AppData\Local\App\Cache"), null, Risk(level));

        Assert.Equal(GuardOutcome.Allowed, d.Outcome);
        Assert.Contains("回收站", d.Reason);   // 放行语义明确为可恢复
    }

    [Theory] // C/D/E 一律拒 (即便启用)
    [InlineData(RiskLevel.C)]
    [InlineData(RiskLevel.D)]
    [InlineData(RiskLevel.E)]
    public void Non_cleanable_rejected_even_when_enabled(RiskLevel level)
    {
        var d = Enabled().Evaluate(Delete(@"C:\Users\me\AppData\Local\App\data"), null, Risk(level));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains(level.ToString(), d.Reason);
    }

    [Fact] // 风险未知 (null) → 拒 (fail-safe, 不允许删除)
    public void Null_risk_rejected()
    {
        var d = Enabled().Evaluate(Delete(@"C:\Users\me\AppData\Local\App\x"), null, null);
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
    }

    [Fact] // 顶层容器目录 (即便被标 A/B) → 拒, 不可整体删除
    public void Container_rejected_even_if_AB()
    {
        var d = Enabled().Evaluate(Delete(@"C:\Users\me\AppData\Local"), null, Risk(RiskLevel.B, isContainer: true));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("容器", d.Reason);
    }

    [Fact] // 系统关键黑名单 → 拒 (即便风险被标 A)
    public void Blacklist_rejected_even_if_A()
    {
        var d = Enabled().Evaluate(Delete(@"C:\Windows\System32\drivers\x.sys"), null, Risk(RiskLevel.A));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);
    }

    [Fact] // 系统关键规则命中 → 拒 (规则权威, 双保险)
    public void System_critical_rule_rejected()
    {
        var rule = new RuleMatch(0, 0, "sys", "cat", RiskLevel.D, false, true, "禁删", 0.9, 100, true);
        var d = Enabled().Evaluate(Delete(@"C:\Users\me\AppData\Local\App\Cache"), rule, Risk(RiskLevel.A));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);
    }

    [Fact] // 被占用 (即便 A/B) → 拒
    public void Occupied_AB_rejected()
    {
        var win = new FakeWindowsAccess { Occupier = "chrome" };
        var d = Enabled(win).Evaluate(Delete(@"C:\Users\me\AppData\Local\App\Cache"), null, Risk(RiskLevel.B));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("占用", d.Reason);
    }

    [Fact] // symlink 指向 System32 → 解析真实路径后拒 (IR-4), 即便表面是 A 级缓存
    public void Symlink_to_blacklist_resolved_then_rejected()
    {
        var win = new FakeWindowsAccess
        {
            RealPathOf = p => p == @"C:\Users\me\Cache" ? @"C:\Windows\System32\evil" : p,
        };
        var d = Enabled(win).Evaluate(Delete(@"C:\Users\me\Cache"), null, Risk(RiskLevel.A));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);
    }

    [Fact] // 能力位关闭 (默认) → A 级也拒 (零删除回退)
    public void Disabled_rejects_even_A()
    {
        var guard = new SafetyGuard(new FakeWindowsAccess(), deleteEnabled: false);
        var d = guard.Evaluate(Delete(@"C:\Users\me\AppData\Local\App\Cache"), null, Risk(RiskLevel.A));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
    }

    // —— 问题#4: UserOverride 仅放宽风险等级闸门 (C4), 其余红线不动 ——

    [Theory] // 用户强确认 → C/D/E 也放行 (识别不出但属用户自己的数据)
    [InlineData(RiskLevel.C)]
    [InlineData(RiskLevel.D)]
    [InlineData(RiskLevel.E)]
    public void Override_allows_high_risk(RiskLevel level)
    {
        var d = Enabled().Evaluate(Manual(@"D:\我下载的资料"), null, Risk(level));
        Assert.Equal(GuardOutcome.Allowed, d.Outcome);
        Assert.Contains("回收站", d.Reason);
    }

    [Fact] // 强确认也救不了系统关键黑名单 (红线不受 override 影响)
    public void Override_still_rejects_blacklist()
    {
        var d = Enabled().Evaluate(Manual(@"C:\Windows\System32\drivers\x.sys"), null, Risk(RiskLevel.E));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);
    }

    [Fact] // 强确认也不能整体删容器
    public void Override_still_rejects_container()
    {
        var d = Enabled().Evaluate(Manual(@"C:\Users\me\AppData\Local"), null, Risk(RiskLevel.E, isContainer: true));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("容器", d.Reason);
    }

    [Fact] // 强确认也不能删被占用项
    public void Override_still_rejects_occupied()
    {
        var win = new FakeWindowsAccess { Occupier = "chrome" };
        var d = Enabled(win).Evaluate(Manual(@"D:\我下载的资料"), null, Risk(RiskLevel.C));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("占用", d.Reason);
    }

    [Fact] // 能力位关闭时, 强确认仍拒 (零删除回退不被 override 突破)
    public void Override_still_rejected_when_capability_off()
    {
        var guard = new SafetyGuard(new FakeWindowsAccess(), deleteEnabled: false);
        var d = guard.Evaluate(Manual(@"D:\我下载的资料"), null, Risk(RiskLevel.C));
        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
    }
}
