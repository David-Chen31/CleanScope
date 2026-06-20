using System.Text.Json;
using CleanScope.Ai.Chat;

namespace CleanScope.Ai.Explanation;

/// <summary>
/// AI 解释服务 (实现 <see cref="IExplanationService"/>)。把已脱敏 <see cref="AiInput"/> 转自然语言解释。
///
/// 硬要求 (决议5, T-14): **支持降级** —— 云端不可用/超时/异常/未配置时, 回退基于规则的本地解释,
/// 核心功能不依赖 AI 在线。输出 <c>Validated=false</c>, 须经 <see cref="IAiOutputValidator"/> 校验后方可展示。
/// 仅发送脱敏载荷 (PR-1), 不接触文件内容。
/// </summary>
public sealed class ExplanationService : IExplanationService
{
    private const string SystemPrompt =
        "你是 Windows 磁盘清理助手的解释模块。任务: 说清这个文件/目录是什么、属于哪个软件、为何是该风险等级, 并给更安全的处理建议。\n" +
        "请遵守两条不同的准则:\n" +
        "(1) 识别与介绍 —— 鼓励运用常识: 若从路径、文件夹名 (pathPattern) 或 relatedApps 能认出是某个具体软件" +
        "(例如 Steam=游戏平台、Zed/VS Code=代码编辑器、有道=翻译软件、微信=即时通讯), 请大方说明它是什么、" +
        "典型用途、该目录通常存放什么, 帮用户看懂。不要因为'只是文件夹名'就拒绝介绍已知软件。\n" +
        "(2) 删除安全 —— 必须保守、只依据给定事实: 关于'能否删除/风险等级', 不得断言'一定可删'; " +
        "系统关键项必须建议不要删除; 不确定就明说不确定, 优先推荐软件自带或系统官方的清理方式。\n" +
        "注意: pathPattern 中的 %USER%/%FILE% 是为保护隐私做的占位符 (分别代表用户名/被隐去的名称), 不是真实名字。\n" +
        "只输出一个 JSON 对象, 字段: whatIsIt(string, 点明是什么软件及其用途), ownerApp(string|null), " +
        "riskLevel(A|B|C|D|E), canDeleteDirectly(bool), recommendedAction(string), reasoning(string[]), " +
        "confidence(0-1 number), userFriendlyExplanation(string, 面向普通用户的一段话: 先说清是什么软件、再给处理建议)。" +
        "不要输出 JSON 以外的任何内容。";

    private readonly IAiChat? _chat;

    public ExplanationService(IAiChat? chat) => _chat = chat;

    public bool IsCloudEnabled => _chat is { Enabled: true };

    public async Task<AiExplanation> ExplainAsync(AiInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (IsCloudEnabled)
        {
            try
            {
                var content = await _chat!.CompleteAsync(SystemPrompt, BuildUserPrompt(input), ct);
                if (TryParseCloud(content, input, out var explanation))
                    return explanation;
            }
            catch
            {
                // 离线/超时/异常 → 降级 (T-14)。
            }
        }
        return BuildLocal(input);
    }

    private static string BuildUserPrompt(AiInput i) => JsonSerializer.Serialize(new
    {
        pathPattern = i.PathPattern,
        extension = i.Extension,
        sizeBytes = i.Size,
        nodeType = i.NodeType?.ToString(),
        ruleCategory = i.MatchedRuleCategory,
        ruleRiskLevel = i.RuleRiskLevel?.ToString(),
        isSystemCritical = i.IsSystemCritical,
        facts = i.Facts,
        relatedApps = i.RelatedApps.Select(a => new { a.AppName, a.Confidence }),
        engineConfidence = i.Confidence,
    });

    private bool TryParseCloud(string content, AiInput input, out AiExplanation explanation)
    {
        explanation = default!;
        var json = ExtractJsonObject(content);
        if (json is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            explanation = new AiExplanation(
                Id: 0,
                FileId: 0,
                WhatIsIt: Str(r, "whatIsIt"),
                OwnerApp: Str(r, "ownerApp"),
                RiskLevel: ParseRisk(Str(r, "riskLevel")),
                CanDeleteDirectly: Bool(r, "canDeleteDirectly"),
                RecommendedAction: Str(r, "recommendedAction"),
                Reasoning: StrArray(r, "reasoning"),
                Confidence: Num(r, "confidence"),
                UserFriendlyExplanation: Str(r, "userFriendlyExplanation"),
                Validated: false,                 // 须经校验器
                ModelUsed: "cloud",
                IsCloud: true,
                CreatedAt: DateTime.UtcNow);
            return true;
        }
        catch
        {
            return false;   // 解析失败 → 调用方降级到本地
        }
    }

    // 本地规则解释 (无 AI): 用脱敏输入里的事实拼出可展示解释。保守、不开删除绿灯。
    private static AiExplanation BuildLocal(AiInput i)
    {
        var category = string.IsNullOrWhiteSpace(i.MatchedRuleCategory) ? "未明确类别" : i.MatchedRuleCategory!;
        var owner = i.RelatedApps.OrderByDescending(a => a.Confidence).FirstOrDefault()?.AppName;
        var risk = i.RuleRiskLevel;

        return new AiExplanation(
            Id: 0,
            FileId: 0,
            WhatIsIt: $"可能是「{category}」相关的文件/目录",
            OwnerApp: owner,
            RiskLevel: risk,
            CanDeleteDirectly: false,             // 本地降级保守
            RecommendedAction: DefaultAction(risk),
            Reasoning: i.Facts.Count > 0 ? i.Facts : new[] { "依据规则与路径特征" },
            Confidence: i.Confidence,
            UserFriendlyExplanation: owner is null
                ? $"该项归类为「{category}」。"
                : $"该项可能属于「{owner}」, 归类为「{category}」。",
            Validated: false,
            ModelUsed: "rule-based",
            IsCloud: false,
            CreatedAt: DateTime.UtcNow);
    }

    private static string DefaultAction(RiskLevel? risk) => risk switch
    {
        RiskLevel.A => "通常可清理, 仍建议确认",
        RiskLevel.B => "建议用官方方式清理 (命令/设置)",
        RiskLevel.C => "谨慎处理: 建议先备份或确认用途",
        RiskLevel.D => "不建议删除: 高风险, 见官方替代方案",
        RiskLevel.E => "无法判断, 不建议删除",
        _ => "建议确认后再处理",
    };

    private static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static string? Str(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static double? Num(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static IReadOnlyList<string> StrArray(JsonElement e, string n)
    {
        if (!e.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!).ToList();
    }

    private static RiskLevel? ParseRisk(string? s) =>
        Enum.TryParse<RiskLevel>(s, ignoreCase: true, out var r) && Enum.IsDefined(r) ? r : null;
}
