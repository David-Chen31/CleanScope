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
        "你是 Windows 磁盘清理顾问, 面向**不懂命令行的普通用户**。基于用户的占用汇总与本机适用的官方清理手段, " +
        "给出简洁中文的**可执行行动计划**, 按「省得多 + 风险低 + 操作简单」排优先顺序, 并识别重复/冗余 (多套同类工具链、重复缓存)。\n" +
        "每一步都必须**具体、可照做**, 写清三件事: ①做什么 ②在哪做(怎么点) ③预计省多少。\n" +
        "「在哪做」只能是下面两种之一, 且要让用户一眼知道去哪点, 不要含糊:\n" +
        "  - 本程序「Windows 官方清理」卡片里**与我给的名称完全一致**的按钮 —— 注明它是『应用内一键执行(无需命令行)』还是『会打开 Windows 自带界面』;\n" +
        "  - 或本程序「可清理清单」里勾选对应项后点『移入回收站』。\n" +
        "**严禁**写「用官方方式清理」「用相关命令清理」这类笼统话; **严禁**让用户自己打开终端/PowerShell/CMD 或手敲命令; " +
        "**严禁**输出任何删除命令、脚本、路径删除指令; 只能引用我提供的官方手段名称, 不要自创。\n" +
        "若我额外提供了「具体大项」(真实路径/名称), 针对它们给个性化判断: 哪些可清、哪些是个人资料应保留 (只描述, 不给命令)。\n" +
        "格式: markdown 有序列表, 至多 7 条, 每条一句话且带预估收益; 不逐文件复述; 末尾一句提醒『删除前再确认、本程序删除只进回收站可还原』。";

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
            sb.AppendLine("本机可用的官方清理手段 —— 这些在本程序「Windows 官方清理」卡片里都有同名按钮, 引用时请用『确切名称』并注明执行方式:");
            sb.AppendLine("(名称 | 预估收益 | 执行方式 | 需管理员)");
            foreach (var a in applicable)
            {
                var surface = a.Surface == Domain.Enums.CleanupSurface.OpensWindowsUi ? "会打开 Windows 自带界面" : "应用内一键执行(无需命令行)";
                sb.AppendLine($"- {a.Title} | {(a.EstimatedBytes > 0 ? Size(a.EstimatedBytes) : "未知")} | {surface} | {(a.NeedsAdmin ? "是" : "否")}");
            }
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
