namespace CleanScope.App.Wpf.Common;

/// <summary>Toast 类型 (决定左侧色条与图标)。</summary>
public enum ToastKind { Success, Info, Error }

/// <summary>一条 Toast 消息。<paramref name="ActionText"/>/<paramref name="Action"/> 可选 (如"打开回收站")。</summary>
public sealed record ToastMessage(string Text, ToastKind Kind, string? ActionText = null, Action? Action = null);

/// <summary>
/// 全局轻量反馈出口 (右下角自动消失)。任意 ViewModel 调 <see cref="Show"/> 即可，
/// 由 <c>ToastHost</c> 订阅并在 UI 线程呈现。无 UI 依赖，便于测试/复用。
/// </summary>
public static class Toast
{
    public static event Action<ToastMessage>? Posted;

    public static void Show(string text, ToastKind kind = ToastKind.Info, string? actionText = null, Action? action = null)
        => Posted?.Invoke(new ToastMessage(text, kind, actionText, action));

    public static void Success(string text) => Show(text, ToastKind.Success);
    public static void Error(string text) => Show(text, ToastKind.Error);
}
