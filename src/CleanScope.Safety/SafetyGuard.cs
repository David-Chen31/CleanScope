namespace CleanScope.Safety;

/// <summary>
/// 安全闸门 (架构§5, 唯一可改盘判定源)。删除意图须 <b>全部</b>满足安全§4 的条件才放行 (AND, fail-safe IR-8)。
///
/// 能力位 <paramref name="deleteEnabled"/>=false (默认, 如 Console) ⇒ 一切删除意图恒被拒 (SR-1, 零删除)。
/// =true (S-E, 如桌面端) ⇒ 仅"可清理"桶 (A/B) 且非黑名单/非容器/未占用的项可放行, 且**仅移入回收站 (可恢复)**。
/// 非破坏性辅助操作 (打开目录/复制路径/跳转设置/展示命令/加忽略/导出) 直接放行。
///
/// 条件顺序: 先评估目标相关的安全门 (C6 真实路径 → C2/C3 黑名单 → C3.5 容器 → C4 风险 → C5 占用),
/// 给出最具操作性的拒绝理由; 全部通过后再卡 C1 能力位。永久删除从不在此枚举/代码中 (IR-1/SR-3)。
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

        // C3.5: 顶层容器目录 (盘符根/Users/AppData/Program Files…) 仅供浏览, 绝不整体删除 (防误删多个软件数据)。
        if (risk?.IsContainer == true)
            return Reject(
                "目标是顶层容器目录 (内含多个程序的数据), 不可整体删除。",
                "请展开按子目录分别判断, 不要对容器整体操作。");

        // C4: 仅"可清理"桶 (A/B: 缓存/包/临时, 可重建) 可移入回收站; C/D/E 一律不放行。
        //   问题#4 例外: 用户经强确认手动处置"自己的、识别不出的"高风险项时放宽本条 (仅本条);
        //   黑名单(C2/C3)/容器(C3.5)/占用(C5)/仅回收站等红线在前后仍然生效, 不受 UserOverride 影响。
        if (!request.UserOverride && (risk is null || (risk.Level != RiskLevel.A && risk.Level != RiskLevel.B)))
            return Reject(
                $"风险等级为 {risk?.Level.ToString() ?? "未知"}, 不允许删除 (仅 A/B 可清理项可移入回收站)。",
                ruleMatch?.RecommendedAction ?? "请参考更安全的官方清理方式, 或先备份确认。");

        // C5: 被进程占用 → 拒 (IR-2)。
        var occupier = _windows.GetOccupyingProcessName(realPath);
        if (!string.IsNullOrWhiteSpace(occupier))
            return Reject(
                $"目标正被进程「{occupier}」占用, 不能删除。",
                "请先关闭占用该文件的程序, 再重新评估。");

        // C1 能力位: 未开启删除能力 ⇒ 兜底拒绝 (SR-1)。Console 等场景恒 false (零删除)。
        if (!_deleteEnabled)
            return Reject(
                "当前版本不提供删除功能, 仅作解释与建议 (MVP 零删除)。",
                "请参考建议的官方清理方式, 或在充分了解后自行确认处理。");

        // 全部安全门通过 (非黑名单/非容器/未占用; A/B 或经用户强确认覆盖) + 能力位开启 → 放行。
        // C7 忽略名单 / C8 两步确认由上层保证; C9 仅移入回收站 (可恢复) / C10 先写审计由执行器保证。
        return Allow(request.UserOverride
            ? "准入通过 (用户强确认手动处置): 仅移入回收站 (可恢复); 系统关键/容器/占用红线仍已排除。"
            : "准入通过: 仅移入回收站 (可恢复), 已排除系统关键/容器/占用/高风险。");
    }

    private static GuardDecision Allow(string reason) => new(GuardOutcome.Allowed, reason, null);
    private static GuardDecision Reject(string reason, string? alternative) => new(GuardOutcome.Rejected, reason, alternative);
}
