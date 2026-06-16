using System.Text.Json;

namespace CleanScope.Ai.Chat;

/// <summary>AI 中转配置。来源优先级: 环境变量 &gt; 本地配置文件 (均 gitignore, 绝不入库)。</summary>
public sealed record AiOptions(string BaseUrl, string ApiKey, string Model, bool CloudEnabled)
{
    public static AiOptions Disabled { get; } = new(string.Empty, string.Empty, string.Empty, false);

    public bool IsUsable => CloudEnabled
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model);

    /// <summary>从 (可选) JSON 文件加载, 再用环境变量覆盖。任何缺失/失败 → 返回 Disabled。</summary>
    public static AiOptions Load(string? jsonFilePath)
    {
        string baseUrl = string.Empty, apiKey = string.Empty, model = "deepseek-chat";
        bool cloud = false;

        try
        {
            if (jsonFilePath is not null && File.Exists(jsonFilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonFilePath));
                var r = doc.RootElement;
                baseUrl = Str(r, "baseUrl") ?? baseUrl;
                apiKey = Str(r, "apiKey") ?? apiKey;
                model = Str(r, "model") ?? model;
                cloud = r.TryGetProperty("cloudEnabled", out var c) && c.ValueKind == JsonValueKind.True;
            }
        }
        catch { /* 配置损坏 → 视为未配置, 走本地降级 */ }

        baseUrl = Env("CLEANSCOPE_AI_BASEURL") ?? baseUrl;
        apiKey = Env("CLEANSCOPE_AI_KEY") ?? apiKey;
        model = Env("CLEANSCOPE_AI_MODEL") ?? model;
        if (Env("CLEANSCOPE_AI_CLOUD") is { } e) cloud = e is "1" or "true" or "True";

        return new AiOptions(baseUrl, apiKey, model, cloud);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
