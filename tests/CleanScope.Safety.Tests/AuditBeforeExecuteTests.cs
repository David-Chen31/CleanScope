using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Safety;

namespace CleanScope.Safety.Tests;

// T4.4 / T-20: 先写审计后执行 (SR-9) —— 审计落库失败则中止操作。
public sealed class AuditBeforeExecuteTests
{
    private static readonly GuardDecision Allowed = new(GuardOutcome.Allowed, "ok", null);

    [Fact] // T-20: 操作前写审计失败 → 中止 (不执行任何副作用)
    public void Audit_write_failure_aborts_the_action()
    {
        var shell = new FakeShellLauncher();
        var audit = new FakeAudit { ThrowOnAdd = true };
        var exec = new ActionExecutor(shell, audit, null, "test");

        var log = exec.ExecuteAsync(new ActionRequest(1, @"C:\some\dir", ActionType.OpenDir), Allowed)
            .GetAwaiter().GetResult();

        Assert.Equal(ActionResult.Failed, log.Result);
        Assert.Contains("审计", log.RejectReason);
        Assert.Empty(shell.OpenedFolders);    // 操作未执行 (先日志后执行)
        Assert.Empty(audit.Added);            // 审计未成功落库
    }

    [Fact] // 审计成功 → 才执行
    public void Audit_success_allows_execution()
    {
        var shell = new FakeShellLauncher();
        var audit = new FakeAudit();
        var exec = new ActionExecutor(shell, audit, null, "test");

        var log = exec.ExecuteAsync(new ActionRequest(1, @"C:\d", ActionType.OpenDir), Allowed)
            .GetAwaiter().GetResult();

        Assert.Equal(ActionResult.Success, log.Result);
        Assert.Single(audit.Added);
        Assert.Single(shell.OpenedFolders);
    }
}
