namespace CleanScope.Safety;

/// <summary>
/// 安全闸门 (架构§5, 唯一可改盘判定源)。删除意图须 <b>全部</b>满足安全§4 的 10 条件才放行 (AND, fail-safe IR-8)。
///
/// MVP: 删除能力未开放 (C1=false) ⇒ 一切删除意图恒被拒 (SR-1)。
/// 非破坏性辅助操作 (打开目录/复制路径/跳转设置/展示命令/加忽略/导出) 直接放行。
///
/// 条件顺序: 先评估目标相关的安全门 (C2 黑名单 / C4 风险 / C5 占用 / C6 穿越),
/// 给出最具操作性的拒绝理由; 全部通过后再卡 C1 能力位 (MVP 在此兜底拒绝)。
/// 详细 C2–C10 在 T4.2 落地; 本类 (T4.1) 先保证"任何删除 → Rejected"。
/// </summary>
public sealed class SafetyGuard : ISafetyGuard
{
    private readonly IWindowsAccess _windows;
    private readonly bool _deleteEnabled;   // MVP 恒 false

    public SafetyGuard(IWindowsAccess windows, bool deleteEnabled = false)
    {
        _windows = windows;
        _deleteEnabled = deleteEnabled;
    }

    public GuardDecision Evaluate(ActionRequest request, RuleMatch? ruleMatch, RiskAssessment? risk)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsDestructive(request.Action))
            return Allow("辅助操作: 只读 / 无破坏性");

        return EvaluateDelete(request, ruleMatch, risk);
    }

    // MoveToRecycleBin 是唯一的破坏性意图 (且仅 Beta 起存在); 永久删除从不在此枚举/代码中 (IR-1/SR-3)。
    private static bool IsDestructive(ActionType action) => action == ActionType.MoveToRecycleBin;

    private GuardDecision EvaluateDelete(ActionRequest request, RuleMatch? ruleMatch, RiskAssessment? risk)
    {
        // C6 先行 (IR-4): 解析真实路径, 防 symlink/junction 绕过后续黑名单判定。
        var realPath = _windows.ResolveRealPath(request.TargetPath);

        // C2 + C3: 命中系统关键黑名单 / 禁删类型 → 拒 (规则权威 + 路径黑名单双保险, IR-5)。
        if (ruleMatch?.IsSystemCritical == true || SystemCriticalPaths.IsBlacklisted(realPath))
            return Reject(
                "目标命中系统关键黑名单, 严禁删除。",
                "请经官方工具/设置处理 (如 DISM 清理、设备管理器、磁盘清理), 切勿手动删除。");

        // C4: 仅 A 级可考虑直删 (自 Beta 起); B/C/D/E 不放行。
        if (risk is not null && risk.Level != RiskLevel.A)
            return Reject(
                $"风险等级为 {risk.Level}, 不允许直接删除 (仅 A 级在 Beta 起可考虑)。",
                ruleMatch?.RecommendedAction ?? "请参考更安全的官方清理方式, 或先备份确认。");

        // C5: 被进程占用 → 拒 (IR-2)。
        var occupier = _windows.GetOccupyingProcessName(realPath);
        if (!string.IsNullOrWhiteSpace(occupier))
            return Reject(
                $"目标正被进程「{occupier}」占用, 不能删除。",
                "请先关闭占用该文件的程序, 再重新评估。");

        // C7 忽略名单 / C8 两步确认 / C9 回收站 / C10 审计: 由上层与执行器在 Beta 起保证。
        // C1 能力位: MVP 恒 false ⇒ 兜底拒绝 (SR-1, MVP 零删除)。
        if (!_deleteEnabled)
            return Reject(
                "当前版本不提供删除功能, 仅作解释与建议 (MVP 零删除)。",
                "请参考建议的官方清理方式, 或在充分了解后自行确认处理。");

        // (Beta 起) 走到此处方可进入两步确认 + 审计 + 移入回收站。
        return Reject("删除准入未完全通过。", "请参考官方清理方式。");
    }

    private static GuardDecision Allow(string reason) => new(GuardOutcome.Allowed, reason, null);
    private static GuardDecision Reject(string reason, string? alternative) => new(GuardOutcome.Rejected, reason, alternative);
}
