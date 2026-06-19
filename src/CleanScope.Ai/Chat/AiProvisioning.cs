using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CleanScope.Ai.Chat;

/// <summary>
/// AI 配置自助 (D): 后端凭 baseUrl + key 检索可用模型 (GET /models) 与连通性测试 (一条极小补全),
/// 供桌面设置页"检索模型 → 列表选择 → 测试"。纯网络探测, 不接触文件, 不做脱敏 (此处无文件内容)。
/// </summary>
public static class AiProvisioning
{
    /// <summary>GET {baseUrl}/models, 解析 OpenAI 兼容的 data[].id 列表。失败抛异常 (含可读信息)。</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(
        HttpClient http, string baseUrl, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("请先填写 Base URL。");
        var url = baseUrl.TrimEnd('/') + "/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"检索模型失败 (HTTP {(int)resp.StatusCode})。请检查 Base URL / Key 是否正确、端点是否兼容 OpenAI。");

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<string>();
        var root = doc.RootElement;
        // 标准: { "data": [ { "id": "..." } ] }; 兼容直接数组 [ {id} ] 或 [ "id" ]。
        var arr = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("data", out var d) ? d : default;
        if (arr.ValueKind == JsonValueKind.Array)
            foreach (var m in arr.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String) { list.Add(m.GetString()!); continue; }
                if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    list.Add(id.GetString()!);
            }
        return list.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>用给定配置发一条极小补全验证 baseUrl+key+model 是否可用。返回 (是否成功, 提示信息)。</summary>
    public static async Task<(bool Ok, string Message)> TestAsync(
        HttpClient http, AiOptions options, CancellationToken ct = default)
    {
        if (!options.IsUsable) return (false, "配置不完整 (需 Base URL、Key、模型, 且已启用)。");
        try
        {
            var chat = new OpenAiChatClient(http, options);
            var reply = await chat.CompleteAsync("你是连通性测试助手。", "只回复两个字: 成功", ct);
            var trimmed = reply.Trim();
            return (true, $"连接成功，模型已响应：{(trimmed.Length > 30 ? trimmed[..30] + "…" : trimmed)}");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
    }
}
