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
        // C1 能力位: MVP 恒 false ⇒ 直接拒绝 (SR-1, MVP 零删除)。
        if (!_deleteEnabled)
            return Reject(
                "当前版本不提供删除功能, 仅作解释与建议 (MVP 零删除)。",
                "请参考建议的官方清理方式, 或在充分了解后自行确认处理。");

        // Beta 起: 此处继续 C2–C10 (T4.2)。当前不可达。
        return Reject("删除准入未通过。", "请参考官方清理方式。");
    }

    private static GuardDecision Allow(string reason) => new(GuardOutcome.Allowed, reason, null);
    private static GuardDecision Reject(string reason, string? alternative) => new(GuardOutcome.Rejected, reason, alternative);
}
