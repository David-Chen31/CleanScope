using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Safety;

namespace CleanScope.Safety.Tests;

// T4.1: SafetyGuard MVP —— 一切删除意图 Rejected (T-01, SR-1); 辅助操作放行。
public sealed class SafetyGuardMvpTests
{
    private static SafetyGuard Mvp() => new(new FakeWindowsAccess(), deleteEnabled: false);

    private static ActionRequest Delete(string path) => new(1, path, ActionType.MoveToRecycleBin);

    [Fact] // T-01: MVP 阶段对任意文件提交删除意图 → Rejected (C1=false)
    public void Any_delete_intent_is_rejected_with_reason_and_alternative()
    {
        var d = Mvp().Evaluate(Delete(@"C:\Users\me\AppData\Local\Temp\x.tmp"), null, null);

        Assert.Equal(GuardOutcome.Rejected, d.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
        Assert.False(string.IsNullOrWhiteSpace(d.RecommendedAlternative));   // 必给官方/替代方式
    }

    [Theory] // 即便是看起来"最安全"的 A 级临时文件, MVP 也拒
    [InlineData(@"C:\Users\me\AppData\Local\Temp\a.tmp")]
    [InlineData(@"C:\some\random\file.bin")]
    [InlineData(@"D:\downloads\setup.exe")]
    public void Even_safe_looking_files_are_rejected_in_mvp(string path)
    {
        var risk = new RiskAssessment(0, 0, RiskLevel.A, 10, new[] { "临时" }, new[] { 1L }, true, 0.9, default);
        Assert.Equal(GuardOutcome.Rejected, Mvp().Evaluate(Delete(path), null, risk).Outcome);
    }

    [Theory] // 非破坏性辅助操作放行
    [InlineData(ActionType.OpenDir)]
    [InlineData(ActionType.CopyPath)]
    [InlineData(ActionType.OpenSettings)]
    [InlineData(ActionType.ShowCommand)]
    [InlineData(ActionType.AddIgnore)]
    [InlineData(ActionType.ExportReport)]
    public void Auxiliary_actions_are_allowed(ActionType action)
    {
        var d = Mvp().Evaluate(new ActionRequest(1, @"C:\x", action), null, null);
        Assert.Equal(GuardOutcome.Allowed, d.Outcome);
    }
}
