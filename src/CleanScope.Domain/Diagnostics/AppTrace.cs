namespace CleanScope.Domain.Diagnostics;

/// <summary>
/// 跨层诊断日志门面 (零依赖)。Domain 不做 IO —— 由宿主 (WPF/Console) 注入写文件的 sink。
/// 下层 (AI/编排) 在"静默降级"的 catch 里调用 <see cref="Log"/>, 把丢失的失败原因变成可诊断记录,
/// 并经 <see cref="LastError"/> 让 UI 即时显示原因, 不必让用户翻日志文件。
/// </summary>
public static class AppTrace
{
    private static Action<string, Exception?>? _sink;

    /// <summary>宿主在启动时注入实际写文件的 sink (如写 %LocalAppData%\CleanScope\logs\app.log)。</summary>
    public static void UseSink(Action<string, Exception?> sink) => _sink = sink;

    /// <summary>最近一次记录的简短文本 (供 UI 即时显示失败原因)。</summary>
    public static string? LastError { get; private set; }

    /// <summary>记录一条诊断 (失败原因 + 可选异常)。绝不抛出 —— 日志失败不得影响主流程。</summary>
    public static void Log(string context, Exception? ex = null)
    {
        LastError = ex is null ? context : $"{context}：{ex.GetType().Name} {ex.Message}";
        try { _sink?.Invoke(context, ex); } catch { /* 日志失败不得再抛 */ }
    }
}
