using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;
using CleanScope.Safety;

namespace CleanScope.Safety.Tests;

// T4.3: ActionExecutor —— 辅助操作可用; 无删除代码路径; 仅执行已放行的。
public sealed class ActionExecutorTests
{
    private static readonly GuardDecision Allowed = new(GuardOutcome.Allowed, "ok", null);
    private static readonly GuardDecision Rejected = new(GuardOutcome.Rejected, "命中黑名单", "官方方式");

    private static (ActionExecutor exec, FakeShellLauncher shell, FakeAudit audit, FakeIgnore ignore) New()
    {
        var shell = new FakeShellLauncher();
        var audit = new FakeAudit();
        var ignore = new FakeIgnore();
        return (new ActionExecutor(shell, audit, ignore, "test"), shell, audit, ignore);
    }

    [Fact]
    public async Task OpenDir_launches_folder_and_audits_first()
    {
        var (exec, shell, audit, _) = New();
        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\some\dir", ActionType.OpenDir), Allowed);

        Assert.Equal(ActionResult.Success, log.Result);
        Assert.Contains(@"C:\some\dir", shell.OpenedFolders);
        Assert.Single(audit.Added);                       // 先写审计
    }

    [Fact]
    public async Task AddIgnore_persists_entry()
    {
        var (exec, _, _, ignore) = New();
        await exec.ExecuteAsync(new ActionRequest(1, @"C:\x\y", ActionType.AddIgnore), Allowed);
        Assert.Single(ignore.Added);
        Assert.Equal(@"C:\x\y", ignore.Added[0].PathOrPattern);
    }

    [Theory] // 纯展示类无副作用但仍成功 + 审计
    [InlineData(ActionType.CopyPath)]
    [InlineData(ActionType.ShowCommand)]
    [InlineData(ActionType.ExportReport)]
    public async Task Side_effect_free_actions_succeed(ActionType action)
    {
        var (exec, shell, audit, _) = New();
        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\x", action), Allowed);
        Assert.Equal(ActionResult.Success, log.Result);
        Assert.Empty(shell.OpenedFolders);
        Assert.Single(audit.Added);
    }

    [Fact] // 仅执行已放行: 被拒 → 不执行任何操作
    public async Task Rejected_approval_performs_nothing()
    {
        var (exec, shell, _, ignore) = New();
        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\x", ActionType.OpenDir), Rejected);

        Assert.Equal(ActionResult.Rejected, log.Result);
        Assert.Empty(shell.OpenedFolders);                // 未打开
        Assert.Empty(ignore.Added);
        Assert.Equal("命中黑名单", log.RejectReason);
    }

    [Fact] // 删除: 即便被错误放行也无删除代码路径 → Failed, 不触碰文件 (IR-1)
    public async Task MoveToRecycleBin_has_no_delete_path()
    {
        var (exec, shell, _, _) = New();
        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\x\f", ActionType.MoveToRecycleBin), Allowed);

        Assert.Equal(ActionResult.Failed, log.Result);    // 无实现, 安全失败
        Assert.Empty(shell.OpenedFolders);
    }
}
