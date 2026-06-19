using System.Net.Http;

namespace CleanScope.Ai.Chat;

/// <summary>
/// 可热替换的对话客户端 (D 运行时重组)。内部包裹一个 <see cref="OpenAiChatClient"/> (或在未配置时为空)。
/// 用户在设置页保存新 baseUrl/key/model 后调用 <see cref="Reconfigure"/> 即可即时生效, 无需重启 ——
/// 因为 ExplanationService / CleanupAdvisor 持有的是本包装器, 行为随内部客户端变化。
/// </summary>
public sealed class MutableAiChat : IAiChat
{
    private readonly HttpClient _http;
    private volatile OpenAiChatClient? _inner;

    public MutableAiChat(HttpClient http, AiOptions initial)
    {
        _http = http;
        Reconfigure(initial);
    }

    /// <summary>配置变化时触发, 供 UI 刷新"AI 已启用"徽章/菜单可见性。</summary>
    public event Action? EnabledChanged;

    public bool Enabled => _inner is { Enabled: true };

    /// <summary>用新配置重建内部客户端 (未启用/不完整 → 置空, 即停用云端, 走本地降级)。</summary>
    public void Reconfigure(AiOptions options)
    {
        _inner = options.IsUsable ? new OpenAiChatClient(_http, options) : null;
        EnabledChanged?.Invoke();
    }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var inner = _inner ?? throw new InvalidOperationException("AI 未配置或未启用。");
        return inner.CompleteAsync(systemPrompt, userPrompt, ct);
    }
}
