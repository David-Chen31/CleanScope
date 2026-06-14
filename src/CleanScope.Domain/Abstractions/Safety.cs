namespace CleanScope.Domain.Abstractions;

// 安全闸门契约 (架构§5, 唯一可改盘路径)。仅签名 (T0.5)。
// 实现在 CleanScope.Safety; CleanScope.Ai 不引用本程序集 (决议10, 架构测试断言)。

/// <summary>
/// 安全闸门: 评估操作意图是否放行。唯一可触发 IActionExecutor 的判定来源。
/// MVP: 对一切删除意图返回 Rejected (C1=false, SR-1)。
/// 校验黑名单/D-E/占用/路径穿越 (安全§4 的 10 条件)。
/// </summary>
public interface ISafetyGuard
{
    GuardDecision Evaluate(ActionRequest request, RuleMatch? ruleMatch, RiskAssessment? risk);
}

/// <summary>
/// 操作执行器: 仅执行已获闸门放行 (approval.Outcome==Allowed) 的操作。
/// MVP 仅辅助操作 (打开目录/复制路径/跳转设置/展示命令/加忽略); 无永久删除代码路径 (IR-1/SR-3)。
/// 破坏性操作须携带 SafetyGuard 的放行决定。
/// </summary>
public interface IActionExecutor
{
    Task<ActionLog> ExecuteAsync(ActionRequest request, GuardDecision approval, CancellationToken ct = default);
}
