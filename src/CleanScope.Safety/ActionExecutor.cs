namespace CleanScope.Safety;

/// <summary>
/// 操作执行器 (实现 <see cref="IActionExecutor"/>)。仅执行闸门已放行的操作。
///
/// 红线:
///  - **无任何永久删除代码路径** (IR-1/SR-3): 本类不引用 File.Delete/Directory.Delete; 无批量删除 (IR-3)。
///    删除唯一出口是 <see cref="IRecycleBin"/> (可恢复, 移入回收站); 永久删除 API 从不出现。
///  - 先写审计后执行 (SR-9): 审计落库失败 → 中止操作, 绝不执行。
///  - 辅助操作 (打开目录/复制路径/跳转设置/展示命令/加忽略/导出) 均无破坏性。
///  - <see cref="ActionType.MoveToRecycleBin"/> 仅在闸门放行 + 已装配回收站端口时执行; 未装配 → Failed, 不触碰文件。
/// </summary>
public sealed class ActionExecutor : IActionExecutor
{
    private readonly IShellLauncher _shell;
    private readonly IAuditLogRepository _audit;
    private readonly IIgnoreRepository? _ignore;
    private readonly IRecycleBin? _recycleBin;
    private readonly string _appVersion;

    public ActionExecutor(
        IShellLauncher shell, IAuditLogRepository audit,
        IIgnoreRepository? ignore = null, string appVersion = "0.1.0",
        IRecycleBin? recycleBin = null)
    {
        _shell = shell;
        _audit = audit;
        _ignore = ignore;
        _recycleBin = recycleBin;
        _appVersion = appVersion;
    }

    public async Task<ActionLog> ExecuteAsync(ActionRequest request, GuardDecision approval, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(approval);

        // 仅执行闸门放行的操作。被拒 → 记录审计 (best-effort) 并返回, 不执行。
        if (approval.Outcome != GuardOutcome.Allowed)
        {
            var rejected = Log(request, ActionResult.Rejected, approval.Reason);
            try { await _audit.AddAsync(rejected, ct); } catch { /* 拒绝本就不执行 */ }
            return rejected;
        }

        // SR-9: 先写审计后执行。审计失败 → 中止 (不执行任何操作)。
        // 移入回收站为可恢复操作 → 记录回收站定位, 供审计与还原参考。
        var recycleLocation = request.Action == ActionType.MoveToRecycleBin ? "Windows 回收站 (可恢复)" : null;
        var planned = Log(request, ActionResult.Success, rejectReason: null, recycleBinLocation: recycleLocation);
        try
        {
            await _audit.AddAsync(planned, ct);
        }
        catch
        {
            return Log(request, ActionResult.Failed, "审计写入失败, 操作已中止 (SR-9 先日志后执行)");
        }

        // 审计已落库 → 执行辅助操作。
        try
        {
            await PerformAsync(request, ct);
            return planned;
        }
        catch (Exception ex)
        {
            return Log(request, ActionResult.Failed, ex.Message);
        }
    }

    private async Task PerformAsync(ActionRequest request, CancellationToken ct)
    {
        switch (request.Action)
        {
            case ActionType.OpenDir:
                _shell.OpenFolder(request.TargetPath);
                break;
            case ActionType.OpenSettings:
                _shell.OpenUri(request.TargetPath);   // TargetPath 携带 ms-settings: URI
                break;
            case ActionType.RunCleanupCommand:
                // S-D: 运行官方清理命令 (Payload)。我们不删文件, 仅启动厂商工具; 已先写审计 (SR-9)。
                if (!string.IsNullOrWhiteSpace(request.Payload))
                    _shell.RunInTerminal(request.Payload);
                break;
            case ActionType.AddIgnore:
                if (_ignore is not null)
                    await _ignore.AddAsync(
                        new IgnoreEntry(0, request.TargetPath, MatchType.Exact, "用户忽略", DateTime.UtcNow), ct);
                break;

            // 纯展示/无副作用: 剪贴板与命令文本由 UI 层呈现, 报告由 Reporting 负责。
            case ActionType.CopyPath:
            case ActionType.ShowCommand:
            case ActionType.ExportReport:
                break;

            // 删除: 仅"移入回收站"(可恢复)。无永久删除调用 (IR-1/SR-3)。已经闸门放行 + 先写审计 (SR-9)。
            // 未装配回收站端口 → 安全失败 (不触碰任何文件)。
            case ActionType.MoveToRecycleBin:
                if (_recycleBin is null)
                    throw new NotSupportedException("回收站端口未装配, 操作中止 (不触碰任何文件)。");
                _recycleBin.Send(request.TargetPath);
                break;

            default:
                throw new NotSupportedException($"未知操作: {request.Action}");
        }
    }

    private ActionLog Log(ActionRequest request, ActionResult result, string? rejectReason,
        string? recycleBinLocation = null) => new(
        Id: 0,
        FileId: request.FileId,
        TargetPath: request.TargetPath,
        Action: request.Action,
        BeforeState: null,
        RecycleBinLocation: recycleBinLocation,
        Recoverable: true,             // 辅助操作无破坏性; 回收站删除可还原 → 均可恢复
        Operator: Operator.User,
        Result: result,
        RejectReason: rejectReason,
        AppVersion: _appVersion,
        Timestamp: DateTime.UtcNow);
}
