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
        "你是 Windows 磁盘清理顾问, 面向**不懂命令行、也不想动脑的普通用户**——他们只想知道『哪些能放心清、点哪里、能省多少』, 然后磁盘就瘦下来。\n" +
        "本程序已经用确定性规则把每项判好了风险等级: A/B = 可放心清理 (删除只进回收站, 可还原), C = 个人资料/信息不足需你确认, D/E = 系统关键勿动。" +
        "你的计划必须**站在这个已判好的结论上**, 帮用户把『可放心清理』的部分一键清掉, 而不是让他自己纠结。\n" +
        "**第一步永远是**: 去本程序「可清理清单」一键勾选并批量移入回收站那些已判为可放心清理(A/B)的项, 用我给你的『估算可清理』总量作为这一步的 saving (让用户一眼看到能省多少)。\n" +
        "其后再按「省得多 + 风险低 + 操作简单」补充: 本机适用的官方清理手段(我会给名称与预估收益), 以及识别出的重复/冗余(多套同类工具链、重复缓存)。\n" +
        "要**具体**, 不要泛泛: 用我给的真实数据点名实际的大类/大项及其大小(如『Temp 约 3.9 GB』『编译/扩展缓存 约 X』), 直说哪些可清、哪些是个人资料要保留。\n" +
        "只输出**一个 JSON 对象**, 字段如下 (不要输出 JSON 以外的任何字符, 也不要用 ``` 代码块包裹):\n" +
        "  summary(string): 一句话总览, 含预计可省总量, 让人一眼知道值不值得做;\n" +
        "  steps(数组, 3-6 项, 每项一个对象, 第一项即上面的『一键批量回收 A/B』):\n" +
        "    title(string): 一句话的具体动作, 让小白也能看懂(如『去可清理清单一键回收可放心清理项』);\n" +
        "    detail(string): 1-2 句, 说清这是什么、为什么可以清、删了会怎样;\n" +
        "    saving(string): 预计可省, 如『约 1.4 GB』; 不确定就给空字符串;\n" +
        "    difficulty(string): 只能是『简单』『中等』『谨慎』三者之一; 第一步应是『简单』;\n" +
        "    where(string): 在哪做 —— 只能是本程序「可清理清单」, 或「Windows 官方清理」卡片里**与我给的名称完全一致**的按钮名;\n" +
        "  note(string): 末尾一句提醒, 例如『个人资料请自行确认; 本程序删除只进回收站, 可随时还原』。\n" +
        "硬性要求: 所有动作都要能在**本程序内**完成; where 必须具体到上面两种入口之一, 不要写『用官方方式』『用命令』『去设置里』这类笼统话; " +
        "**严禁**让用户自己打开终端/PowerShell/CMD 或第三方软件、手敲命令; **严禁**输出任何删除命令、脚本或路径删除指令; " +
        "只能引用我提供的官方手段名称, 不要自创。若提供了「具体大项」(真实路径/名称), 据此判断哪些可清、哪些是个人资料应保留 (只描述, 不替用户做删除决定)。";

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
            // 优先按结构化 JSON 解析成分步卡片。
            var parsed = TryParsePlan(reply);
            if (parsed is not null) return parsed;

            // 问题#4: JSON 被截断/夹带说明 → 尽力抢救已完整的步骤, 别让用户看到半截 JSON。
            var salvaged = TrySalvageSteps(reply);
            if (salvaged is { Count: > 0 })
            {
                var sum = ExtractSummary(reply) ?? "";
                var nt = "部分内容可能被截断, 仅展示已成型的步骤; 个人资料请自行确认, 删除只进回收站可还原。";
                return new CleanupPlan(sum, salvaged, nt, BuildMarkdown(sum, salvaged, nt));
            }

            // 仍解析不出: 若回复本就是 JSON(只是不完整/异常), 绝不把原始花括号倒给用户; 给一句可重试的提示。
            if (LooksLikeJson(reply))
            {
                Domain.Diagnostics.AppTrace.Log("AI 行动计划: 无法解析为结构化计划 (疑似截断/格式异常)");
                const string msg = "AI 这次返回的清理计划不完整或格式异常（可能被截断）。请再点一次「生成 AI 清理建议」重试。";
                return new CleanupPlan("", Array.Empty<CleanupPlanStep>(), "", msg);
            }

            // 真·纯文本(模型没按 JSON 走) → 原样展示 (仍可读)。
            return new CleanupPlan("", Array.Empty<CleanupPlanStep>(), "", reply.Trim());
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

    // 问题#4: 从被截断/夹带文字的回复里, 逐个抢救出**已完整**的 step 对象 (忽略末尾半截的那个)。
    private static List<CleanupPlanStep>? TrySalvageSteps(string reply)
    {
        var keyIdx = reply.IndexOf("\"steps\"", StringComparison.Ordinal);
        if (keyIdx < 0) return null;
        var arrIdx = reply.IndexOf('[', keyIdx);
        if (arrIdx < 0) return null;

        var steps = new List<CleanupPlanStep>();
        var order = 0;
        var i = arrIdx + 1;
        while (i < reply.Length && steps.Count < 7)
        {
            // 找下一个对象起点; 遇到 ']' 说明数组正常收尾。
            while (i < reply.Length && reply[i] != '{' && reply[i] != ']') i++;
            if (i >= reply.Length || reply[i] == ']') break;

            // 扫描平衡花括号 (容忍字符串内的花括号/转义), 截出一个完整对象。
            var depth = 0; var inStr = false; var esc = false; var objStart = i; var objEnd = -1;
            for (; i < reply.Length; i++)
            {
                var ch = reply[i];
                if (esc) { esc = false; continue; }
                if (ch == '\\') { esc = true; continue; }
                if (ch == '"') inStr = !inStr;
                else if (!inStr && ch == '{') depth++;
                else if (!inStr && ch == '}') { depth--; if (depth == 0) { objEnd = i; i++; break; } }
            }
            if (objEnd < 0) break;   // 半截对象 (被截断) → 丢弃, 停止

            try
            {
                using var doc = JsonDocument.Parse(reply[objStart..(objEnd + 1)]);
                var s = doc.RootElement;
                var title = Str(s, "title");
                if (!string.IsNullOrWhiteSpace(title))
                {
                    order++;
                    steps.Add(new CleanupPlanStep(
                        order, title!.Trim(), Str(s, "detail")?.Trim() ?? "",
                        Str(s, "saving")?.Trim() ?? "", NormalizeDifficulty(Str(s, "difficulty")),
                        Str(s, "where")?.Trim() ?? ""));
                }
            }
            catch { break; }   // 解析不出 → 停
        }
        return steps.Count > 0 ? steps : null;
    }

    // 抢救 summary: 优先取 "summary":"..." 的值 (即便整体 JSON 不完整)。
    private static string? ExtractSummary(string reply)
    {
        var k = reply.IndexOf("\"summary\"", StringComparison.Ordinal);
        if (k < 0) return null;
        var colon = reply.IndexOf(':', k);
        if (colon < 0) return null;
        var q1 = reply.IndexOf('"', colon + 1);
        if (q1 < 0) return null;
        var sb = new StringBuilder();
        for (var i = q1 + 1; i < reply.Length; i++)
        {
            var ch = reply[i];
            if (ch == '\\' && i + 1 < reply.Length) { sb.Append(reply[++i]); continue; }
            if (ch == '"') break;
            sb.Append(ch);
        }
        var s = sb.ToString().Trim();
        return s.Length > 0 ? s : null;
    }

    // 回复看起来是 JSON (而非模型自由发挥的纯文本) —— 用于决定失败时是否该展示原文。
    private static bool LooksLikeJson(string reply)
    {
        var t = reply.TrimStart().TrimStart('`').TrimStart();
        return t.StartsWith('{') || reply.Contains("\"steps\"", StringComparison.Ordinal);
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
