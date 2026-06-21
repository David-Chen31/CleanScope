using System.Globalization;
using System.Text;
using System.Text.Json;
using CleanScope.Ai.Chat;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Models;

namespace CleanScope.Ai.Advice;

/// <summary>
/// 整盘清理参谋 (S-H, 实现 <see cref="ICleanupAdvisor"/>)。把脱敏聚合 (软件/类别/容量) 拼成提示,
/// 请 AI 做**跨项**推理, 并以 **JSON 结构化** 返回分步计划 (标题/收益/难度/在哪做/为什么),
/// 供宿主渲染成卡片 (小白可扫读, 进阶可展开)。解析失败时退化为纯文本 (markdown) 回退展示。
///
/// 安全:
///  - 输入是 <see cref="CleanupSummary"/> —— 只有软件名/类别名/容量/计数, **无路径、无文件内容** (PR-1)。
///  - 输出仅作展示文本, 调用方绝不解析/执行其中任何内容 (AS-6/IR-6); 系统提示明令"严禁输出删除命令"。
///  - AI 不可用/失败 → 返回 null (降级), 核心功能不依赖在线 AI (决议5)。
/// </summary>
public sealed class CleanupAdvisor : ICleanupAdvisor
{
    private const string SystemPrompt =
        "你是 Windows 磁盘清理顾问, 面向**不懂命令行的普通用户**。基于用户的占用汇总与本机适用的官方清理手段, " +
        "给出一份分步的清理行动计划, 按「省得多 + 风险低 + 操作简单」排优先顺序, 并识别重复/冗余 (多套同类工具链、重复缓存)。\n" +
        "只输出**一个 JSON 对象**, 字段如下 (不要输出 JSON 以外的任何字符):\n" +
        "  summary(string): 一句话总览, 包含预计可省的总量, 让人一眼知道值不值得做;\n" +
        "  steps(数组, 至多 7 项, 每项一个对象):\n" +
        "    title(string): 一句话的具体动作, 让小白也能看懂(如『清理可重建的编译缓存』);\n" +
        "    detail(string): 1-2 句, 说清这是什么、为什么可以清、删了会怎样 (给想了解的人看);\n" +
        "    saving(string): 预计可省, 如『约 1.4 GB』; 不确定就给空字符串;\n" +
        "    difficulty(string): 只能是『简单』『中等』『谨慎』三者之一;\n" +
        "    where(string): 在哪做 —— 只能是本程序「可清理清单」, 或「Windows 官方清理」卡片里**与我给的名称完全一致**的按钮名;\n" +
        "  note(string): 末尾一句提醒, 例如『删除前再确认; 本程序删除只进回收站, 可随时还原』。\n" +
        "硬性要求: where 必须具体到上面两种入口之一, 不要写『用官方方式』『用命令』这类笼统话; " +
        "**严禁**让用户自己打开终端/PowerShell/CMD 或手敲命令; **严禁**输出任何删除命令、脚本或路径删除指令; " +
        "只能引用我提供的官方手段名称, 不要自创。若提供了「具体大项」(真实路径/名称), 针对它们判断哪些可清、哪些是个人资料应保留 (只描述)。";

    private readonly IAiChat _chat;

    public CleanupAdvisor(IAiChat chat) => _chat = chat;

    public bool Enabled => _chat.Enabled;

    public async Task<CleanupPlan?> AdviseAsync(
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
            if (string.IsNullOrWhiteSpace(reply))
            {
                Domain.Diagnostics.AppTrace.Log("AI 行动计划: 返回空内容");
                return null;
            }
            // 优先按结构化 JSON 解析成分步卡片; 失败则退化为纯文本展示 (仍可用)。
            return TryParsePlan(reply) ?? new CleanupPlan("", Array.Empty<CleanupPlanStep>(), "", reply.Trim());
        }
        catch (Exception ex)
        {
            Domain.Diagnostics.AppTrace.Log("AI 行动计划生成失败", ex);   // 问题#1: 记录真实原因, 不再静默
            return null;   // 降级: 失败不影响核心流程
        }
    }

    // 解析 JSON 计划; 任何异常/无步骤 → 返回 null 让调用方回退纯文本。
    private static CleanupPlan? TryParsePlan(string reply)
    {
        var json = ExtractJsonObject(reply);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("steps", out var stepsEl) || stepsEl.ValueKind != JsonValueKind.Array)
                return null;

            var steps = new List<CleanupPlanStep>();
            var order = 0;
            foreach (var s in stepsEl.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                var title = Str(s, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;
                order++;
                steps.Add(new CleanupPlanStep(
                    order, title!.Trim(), Str(s, "detail")?.Trim() ?? "",
                    Str(s, "saving")?.Trim() ?? "", NormalizeDifficulty(Str(s, "difficulty")),
                    Str(s, "where")?.Trim() ?? ""));
                if (steps.Count >= 7) break;
            }
            if (steps.Count == 0) return null;

            var summary = Str(root, "summary")?.Trim() ?? "";
            var note = Str(root, "note")?.Trim() ?? "";
            return new CleanupPlan(summary, steps, note, BuildMarkdown(summary, steps, note));
        }
        catch
        {
            return null;
        }
    }

    // 由结构化计划合成可读 markdown (供报告导出 / Console 展示)。
    private static string BuildMarkdown(string summary, IReadOnlyList<CleanupPlanStep> steps, string note)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(summary)) sb.AppendLine(summary).AppendLine();
        foreach (var s in steps)
        {
            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(s.Saving)) meta.Add($"省 {s.Saving}");
            if (!string.IsNullOrWhiteSpace(s.Difficulty)) meta.Add($"难度 {s.Difficulty}");
            if (!string.IsNullOrWhiteSpace(s.Where)) meta.Add($"在哪做：{s.Where}");
            var metaLine = meta.Count > 0 ? $"（{string.Join(" ｜ ", meta)}）" : "";
            sb.AppendLine($"{s.Order}. **{s.Title}** {metaLine}".TrimEnd());
            if (!string.IsNullOrWhiteSpace(s.Detail)) sb.AppendLine($"   {s.Detail}");
        }
        if (!string.IsNullOrWhiteSpace(note)) sb.AppendLine().AppendLine(note);
        return sb.ToString().Trim();
    }

    private static string NormalizeDifficulty(string? d) => d?.Trim() switch
    {
        "简单" or "容易" or "low" or "Low" => "简单",
        "中等" or "medium" or "Medium" => "中等",
        "谨慎" or "高" or "high" or "High" => "谨慎",
        _ => string.IsNullOrWhiteSpace(d) ? "" : d!.Trim(),
    };

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

    private static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static string? Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

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
