using System.Windows;
using System.Windows.Input;
using CleanScope.App.Wpf.Common;

namespace CleanScope.App.Wpf.Views;

/// <summary>D: 首次启动引导。说清"做什么 / 安全 / 三步上手", 看过即记 <see cref="UserPrefs.OnboardingSeen"/> 不再弹。</summary>
public partial class OnboardingWindow : Window
{
    private OnboardingWindow() => InitializeComponent();

    /// <summary>仅在从未看过时弹出 (模态); 关闭后标记已看并持久化。</summary>
    public static void ShowIfFirstRun(Window owner)
    {
        if (UserPrefs.Current.OnboardingSeen) return;
        var win = new OnboardingWindow { Owner = owner };
        win.ShowDialog();
        UserPrefs.Current.OnboardingSeen = true;
        UserPrefs.Current.Save();
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Close();
}
