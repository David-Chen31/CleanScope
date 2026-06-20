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

    // S-D: 运行官方清理命令 → 隐藏执行 (无黑框, Payload) + 先写审计; 我们不删文件。
    [Fact]
    public async Task RunCleanupCommand_runs_managed_and_audits_first()
    {
        var (exec, shell, audit, _) = New();
        var req = new ActionRequest(1, "", ActionType.RunCleanupCommand,
            Payload: "dotnet nuget locals all --clear");
        var log = await exec.ExecuteAsync(req, Allowed);

        Assert.Equal(ActionResult.Success, log.Result);
        Assert.Contains("dotnet nuget locals all --clear", shell.RanCommands);
        Assert.Single(audit.Added);                       // 先写审计 (SR-9)
    }

    [Fact] // 提权标志透传给执行器 (powercfg/DISM 需 UAC)
    public async Task RunCleanupCommand_passes_elevate_flag()
    {
        var (exec, shell, _, _) = New();
        await exec.ExecuteAsync(new ActionRequest(1, "", ActionType.RunCleanupCommand, "powercfg /h off", Elevate: true), Allowed);
        Assert.Contains(true, shell.RanElevations);
    }

    [Fact] // 命令非 0 退出 (失败/UAC 取消) → 记 Failed, 不谎报成功
    public async Task RunCleanupCommand_nonzero_exit_is_failure()
    {
        var (exec, shell, _, _) = New();
        shell.ExitCode = -1;   // 模拟 UAC 取消 / 启动失败
        var log = await exec.ExecuteAsync(new ActionRequest(1, "", ActionType.RunCleanupCommand, "powercfg /h off", Elevate: true), Allowed);
        Assert.Equal(ActionResult.Failed, log.Result);
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

    [Fact] // 未装配回收站端口: 即便放行也无删除发生 → 安全 Failed, 不触碰文件 (IR-1)
    public async Task MoveToRecycleBin_without_recycler_is_safe_failed()
    {
        var (exec, shell, _, _) = New();   // New() 不装配 IRecycleBin
        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\x\f", ActionType.MoveToRecycleBin), Allowed);

        Assert.Equal(ActionResult.Failed, log.Result);
        Assert.Empty(shell.OpenedFolders);
    }

    [Fact] // S-E: 已放行 + 装配回收站端口 → 移入回收站 + 先写审计 (SR-9); 审计标记可恢复+回收站定位。
    public async Task MoveToRecycleBin_recycles_and_audits_first()
    {
        var shell = new FakeShellLauncher();
        var audit = new FakeAudit();
        var bin = new FakeRecycleBin();
        var exec = new ActionExecutor(shell, audit, null, "test", bin);

        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\Users\me\AppData\Local\App\Cache",
            ActionType.MoveToRecycleBin), Allowed);

        Assert.Equal(ActionResult.Success, log.Result);
        Assert.Contains(@"C:\Users\me\AppData\Local\App\Cache", bin.Sent);   // 已移入回收站
        Assert.Single(audit.Added);                                          // 先写审计 (SR-9)
        Assert.True(audit.Added[0].Recoverable);
        Assert.False(string.IsNullOrWhiteSpace(audit.Added[0].RecycleBinLocation));
    }

    [Fact] // 被拒 → 绝不触碰回收站
    public async Task Rejected_recycle_does_not_touch_recycle_bin()
    {
        var bin = new FakeRecycleBin();
        var exec = new ActionExecutor(new FakeShellLauncher(), new FakeAudit(), null, "test", bin);

        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\x\f", ActionType.MoveToRecycleBin), Rejected);

        Assert.Equal(ActionResult.Rejected, log.Result);
        Assert.Empty(bin.Sent);
    }

    [Fact] // 回收站操作本身失败 (如目标消失) → Failed, 不抛出, 上层可读原因
    public async Task Recycle_bin_failure_is_reported_as_failed()
    {
        var bin = new FakeRecycleBin { ThrowOnSend = true };
        var exec = new ActionExecutor(new FakeShellLauncher(), new FakeAudit(), null, "test", bin);

        var log = await exec.ExecuteAsync(new ActionRequest(1, @"C:\x\f", ActionType.MoveToRecycleBin), Allowed);

        Assert.Equal(ActionResult.Failed, log.Result);
        Assert.Empty(bin.Sent);
    }
}
