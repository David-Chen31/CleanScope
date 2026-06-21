using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CleanScope.Ai.Chat;

/// <summary>
/// OpenAI 兼容对话客户端 (POST {baseUrl}/chat/completions)。用于经中转访问 deepseek 等模型。
/// 仅发送已脱敏载荷 (调用方保证); 本类不做脱敏, 也不接触文件内容。
/// </summary>
public sealed class OpenAiChatClient : IAiChat
{
    private readonly HttpClient _http;
    private readonly AiOptions _options;

    public OpenAiChatClient(HttpClient http, AiOptions options)
    {
        _http = http;
        _options = options;
    }

    public bool Enabled => _options.IsUsable;

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var url = _options.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        req.Content = JsonContent.Create(new
        {
            model = _options.Model,
            temperature = 0.2,
            max_tokens = 1500,          // 600 偏小, JSON 解释易被截断导致解析失败 → 误报"未能生成"
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        });

        using var resp = await _http.SendAsync(req, ct);
        // 非 2xx: 读取响应体并抛出**含状态码与原因**的异常 (问题#1: 让"模型名错/鉴权失败/限流"等可诊断,
        // 而非被上层吞成笼统的"AI 未能生成建议")。
        if (!resp.IsSuccessStatusCode)
        {
            var body = await SafeReadBodyAsync(resp, ct);
            throw new AiChatException((int)resp.StatusCode,
                $"AI 服务返回 {(int)resp.StatusCode} {resp.ReasonPhrase}{(body.Length > 0 ? "：" + body : "")}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? throw new AiChatException(0, "AI 返回了空内容 (model 可能不支持该请求)");
    }

    // 读取错误响应体的前若干字符 (含服务端的 error.message), 失败不抛。
    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var s = await resp.Content.ReadAsStringAsync(ct);
            s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return s.Length > 300 ? s[..300] + "…" : s;
        }
        catch { return string.Empty; }
    }
}
