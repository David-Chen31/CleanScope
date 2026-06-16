namespace CleanScope.Ai.Chat;

/// <summary>原始对话补全抽象 (Ai 内部)。隔离 HTTP 细节, 便于 ExplanationService 单测降级路径。</summary>
public interface IAiChat
{
    /// <summary>云端是否可用 (已配置且开启)。false → ExplanationService 走本地规则解释。</summary>
    bool Enabled { get; }

    /// <summary>发起一次对话补全, 返回助手文本。失败抛异常 (由上层降级处理)。</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
