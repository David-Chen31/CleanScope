using System.Globalization;
using System.Text;
using CleanScope.Ai.Chat;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Models;

namespace CleanScope.Ai.Advice;

/// <summary>
/// 整盘清理参谋 (S-H, 实现 <see cref="ICleanupAdvisor"/>)。把脱敏聚合 (软件/类别/容量) 拼成提示,
/// 请 AI 做**跨项**推理: 冗余工具链、重复缓存、优先清理顺序。
///
/// 安全:
///  - 输入是 <see cref="CleanupSummary"/> —— 只有软件名/类别名/容量/计数, **无路径、无文件内容** (PR-1)。
///  - 输出仅作展示文本, 调用方绝不解析/执行其中任何内容 (AS-6/IR-6); 系统提示明令"不要输出删除命令"。
///  - AI 不可用/失败 → 返回 null (降级), 核心功能不依赖在线 AI (决议5)。
/// </summary>
public sealed class CleanupAdvisor : ICleanupAdvisor
{
    private const string SystemPrompt =
        "你是 Windows 磁盘清理顾问。基于用户的占用汇总与本机适用的官方清理手段, 给出简洁中文的**可执行行动计划**: " +
        "1) 按「省得多 + 风险低 + 操作简单」排出**优先顺序**; 2) 识别重复/冗余 (如多套同类工具链、重复缓存); " +
        "3) 每条尽量对应一个明确动作 (在本程序点哪个按钮 / 用哪个官方手段)。" +
        "若我额外提供了「具体大项」(含真实路径/名称), 请针对它们给出有依据的个性化建议 (指出哪些可清、哪些是个人资料应保留), " +
        "但**只描述、不要输出删除命令或脚本**。" +
        "只可引用我提供的官方手段, **不要自创命令**。" +
        "要求: markdown 有序列表, 至多 7 条, 每条一句话且尽量带预估收益; 不要逐文件复述; " +
        "**绝对不要输出任何删除命令、脚本**; 末尾一句提醒删除前确认、优先用官方方式。";

    private readonly IAiChat _chat;

    public CleanupAdvisor(IAiChat chat) => _chat = chat;

    public bool Enabled => _chat.Enabled;

    public async Task<string?> AdviseAsync(
        CleanupSummary summary,
        IReadOnlyList<OfficialCleanupAction>? officialActions = null,
        IReadOnlyList<string>? concreteItems = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (!Enabled) return null;

        try
        {
            var reply = await _chat.CompleteAsync(SystemPrompt, BuildUserPrompt(summary, officialActions, concreteItems), ct);
            if (!string.IsNullOrWhiteSpace(reply)) return reply.Trim();
            Domain.Diagnostics.AppTrace.Log("AI 行动计划: 返回空内容");
            return null;
        }
        catch (Exception ex)
        {
            Domain.Diagnostics.AppTrace.Log("AI 行动计划生成失败", ex);   // 问题#1: 记录真实原因, 不再静默
            return null;   // 降级: 失败不影响核心流程
        }
    }

    private static string BuildUserPrompt(CleanupSummary s, IReadOnlyList<OfficialCleanupAction>? official,
        IReadOnlyList<string>? concreteItems)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"总占用 {Size(s.TotalSize)}, 其中估算可清理 {Size(s.ReclaimableSize)}。").AppendLine();

        if (s.Software.Count > 0)
        {
            sb.AppendLine("按软件占用 (软件 | 占用 | 其中可清理):");
            foreach (var u in s.Software.Take(15))
                sb.AppendLine($"- {u.Name} | {Size(u.TotalSize)} | {Size(u.CleanableSize)}");
            sb.AppendLine();
        }

        if (s.Categories.Count > 0)
        {
            sb.AppendLine("可清理类别 (类别 | 项数 | 可回收 | 官方清理方式):");
            foreach (var c in s.Categories.Take(15))
                sb.AppendLine($"- {c.Name} | {c.ItemCount} | {Size(c.ReclaimableSize)} | {c.RecommendedAction}");
            sb.AppendLine();
        }

        // P1: 把本机适用的系统级官方手段 (含预估收益) 作为 grounding —— AI 只能在这些手段里挑选与排序,
        // 既把"网上常见手段"接进来, 又防止它臆造命令。这些是本程序里可一键执行的动作。
        var applicable = official?.Where(a => a.Detected).ToList();
        if (applicable is { Count: > 0 })
        {
            sb.AppendLine("本机可用的官方清理手段 (手段 | 预估收益 | 需管理员 | 本程序内可一键执行):");
            foreach (var a in applicable)
                sb.AppendLine($"- {a.Title} | {(a.EstimatedBytes > 0 ? Size(a.EstimatedBytes) : "未知")} | {(a.NeedsAdmin ? "是" : "否")} | 是");
            sb.AppendLine();
        }

        // 关闭/放宽脱敏时由宿主附上的"具体大项"(真实路径/名称) —— 让 AI 能给个性化、有依据的建议。
        if (concreteItems is { Count: > 0 })
        {
            sb.AppendLine("具体大项 (路径/名称 | 大小 | 风险等级) —— 请据此给出针对性建议, 区分可清理与个人资料:");
            foreach (var line in concreteItems.Take(15))
                sb.AppendLine($"- {line}");
        }
        return sb.ToString();
    }

    private static string Size(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        double s = bytes;
        int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return i == 0
            ? $"{bytes} {u[i]}"
            : string.Create(CultureInfo.InvariantCulture, $"{s:0.##} {u[i]}");
    }
}
