using System.Windows;

namespace CleanScope.App.Wpf.Common;

public enum AppTheme { Light, Dark }

/// <summary>
/// 主题切换 (E): 运行时替换 App 资源里的主题色板 (MergedDictionaries[0]) 即可整体重着色 ——
/// 所有色板消费方都用 DynamicResource, 故切换立即生效、无需重启。选择持久化到 <see cref="UserPrefs"/>。
/// </summary>
public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>主题切换后触发 (自绘视图如 treemap 用 DynamicResource 无法自动重着色, 借此重绘)。</summary>
    public static event Action? ThemeChanged;

    /// <summary>启动时按已保存偏好应用主题 (在主窗口显示前调用)。</summary>
    public static void Initialize()
    {
        Apply(Parse(UserPrefs.Current.Theme), persist: false);
    }

    public static void Toggle() => Apply(Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

    public static void Apply(AppTheme theme, bool persist = true)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        var uri = new Uri($"pack://application:,,,/Themes/{theme}.xaml");
        var dict = new ResourceDictionary { Source = uri };

        // 主题色板恒为 MergedDictionaries[0] (见 App.xaml); 替换它即整体换肤。
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(dict);
        else merged[0] = dict;

        Current = theme;
        if (persist)
        {
            UserPrefs.Current.Theme = theme.ToString();
            UserPrefs.Current.Save();
        }
        ThemeChanged?.Invoke();
    }

    private static AppTheme Parse(string? s) =>
        string.Equals(s, "Dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;
}
