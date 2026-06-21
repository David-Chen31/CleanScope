namespace CleanScope.Ai.Chat;

/// <summary>
/// AI 对话调用失败 (HTTP 非 2xx / 空内容等)。携带状态码与可读原因 (含服务端 error.message 片段),
/// 供上层记日志与向用户显示**具体原因** (问题#1), 而非被吞成笼统的"未能生成"。
/// </summary>
public sealed class AiChatException : Exception
{
    public int StatusCode { get; }

    public AiChatException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}
