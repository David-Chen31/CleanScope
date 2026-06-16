using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Safety;

namespace CleanScope.Safety.Tests;

// T4.2: SafetyGuard 准入框架 —— C2-C6 逐条拒绝并给可操作理由 (T-02~T-06)。
public sealed class SafetyGuardAdmissionTests
{
    private static ActionRequest Delete(string path) => new(1, path, ActionType.MoveToRecycleBin);

    private static RiskAssessment Risk(RiskLevel level) =>
        new(0, 0, level, 50, new[] { "f" }, new[] { 1L }, level == RiskLevel.A, 0.8, default);

    [Fact] // T-02: System32 删除 → Rejected + 黑名单原因
    public void System32_delete_rejected_as_blacklist()
    {
        var guard = new SafetyGuard(new FakeWindowsAccess());
        var d = guard.Evaluate(Delete(@"C:\Windows\System32\drivers\nv.sys"), null, Risk(RiskLevel.A));

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);
        Assert.False(string.IsNullOrWhiteSpace(d.RecommendedAlternative));   // 官方替代
    }

    [Fact] // T-03: Installer\*.msi → Rejected + 官方替代
    public void Installer_msi_delete_rejected_with_official_alternative()
    {
        var guard = new SafetyGuard(new FakeWindowsAccess());
        var d = guard.Evaluate(Delete(@"C:\Windows\Installer\1a2b.msi"), null, Risk(RiskLevel.A));

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);
        Assert.Contains("官方", d.RecommendedAlternative!);
    }

    [Theory] // T-04: 风险 D/E 项 → Rejected (非黑名单路径)
    [InlineData(RiskLevel.D)]
    [InlineData(RiskLevel.E)]
    [InlineData(RiskLevel.C)]
    [InlineData(RiskLevel.B)]
    public void High_risk_delete_rejected(RiskLevel level)
    {
        var guard = new SafetyGuard(new FakeWindowsAccess());
        var d = guard.Evaluate(Delete(@"D:\data\app\big.vhdx"), null, Risk(level));

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains(level.ToString(), d.Reason);   // 提及风险等级
    }

    [Fact] // T-05: 被进程占用 → Rejected
    public void Occupied_file_delete_rejected()
    {
        var win = new FakeWindowsAccess { Occupier = "devenv" };
        var guard = new SafetyGuard(win);
        var d = guard.Evaluate(Delete(@"C:\Users\me\proj\locked.tmp"), null, Risk(RiskLevel.A));

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("devenv", d.Reason);
        Assert.Contains("占用", d.Reason);
    }

    [Fact] // T-06: 经 symlink 指向 System32 → 解析真实路径后 Rejected (IR-4)
    public void Symlink_to_system32_resolved_then_rejected()
    {
        var win = new FakeWindowsAccess
        {
            RealPathOf = p => p == @"C:\Users\me\shortcut" ? @"C:\Windows\System32\evil" : p,
        };
        var guard = new SafetyGuard(win);
        var d = guard.Evaluate(Delete(@"C:\Users\me\shortcut"), null, Risk(RiskLevel.A));

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("黑名单", d.Reason);   // 真实路径命中黑名单
    }

    [Fact] // 即便全部前置安全条件通过 (A级/未占用/非黑名单), MVP 仍在 C1 兜底拒绝
    public void All_safety_conditions_pass_but_mvp_still_rejects_at_C1()
    {
        var guard = new SafetyGuard(new FakeWindowsAccess(), deleteEnabled: false);
        var d = guard.Evaluate(Delete(@"C:\Users\me\AppData\Local\Temp\x.tmp"), null, Risk(RiskLevel.A));

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.Contains("MVP", d.Reason);
    }
}
