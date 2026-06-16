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
            max_tokens = 600,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        });

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? throw new InvalidOperationException("AI 返回空内容");
    }
}
