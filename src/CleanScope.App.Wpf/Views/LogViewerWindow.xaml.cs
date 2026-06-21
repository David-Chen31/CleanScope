using System.Windows;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

/// <summary>问题#3: 应用内诊断日志查看窗 (非模态)。从 AI 设置「查看日志」打开, 用户不必自己去翻文件。</summary>
public partial class LogViewerWindow : Window
{
    private LogViewerWindow()
    {
        InitializeComponent();
        DataContext = new LogViewerViewModel();
    }

    /// <summary>打开 (或聚焦已打开的) 日志窗。非模态, 便于边看日志边调 AI 设置。</summary>
    public static void ShowFor(Window? owner)
    {
        if (_open is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return;
        }
        var win = new LogViewerWindow { Owner = owner };
        _open = win;
        win.Closed += (_, _) => _open = null;
        win.Show();
    }

    private static LogViewerWindow? _open;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
