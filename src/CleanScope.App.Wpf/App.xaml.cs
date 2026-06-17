using System.Windows;
using System.Windows.Threading;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf;

/// <summary>
/// 应用入口 (T5.1 组合根挂载)。启动时手写装配 <see cref="AppServices"/> → 构造 ShellViewModel →
/// 显示主窗口。装配失败 (如规则缺失) 弹窗提示, 不静默崩溃。
/// 全局兜底未处理异常: 记日志 + 提示, 不让 UI 线程异常导致静默闪退。
/// </summary>
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // UI 线程未处理异常 → 记录并提示, 不闪退 (可恢复的尽量保活)。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            if (ev.ExceptionObject is Exception ex) AppLog.Error("AppDomain.UnhandledException", ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            AppLog.Error("UnobservedTaskException", ev.Exception);
            ev.SetObserved();
        };

        try
        {
            var services = await CompositionRoot.BuildAsync();
            var shell = new ShellViewModel(services);
            var window = new MainWindow { DataContext = shell };
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            AppLog.Error("OnStartup", ex);
            MessageBox.Show($"启动失败：{ex.GetType().Name}\n{ex.Message}\n\n详情见日志：{AppLog.LogPath}", "CleanScope",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            $"发生了一个错误，但应用会尽量继续运行。\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n详情见日志：{AppLog.LogPath}",
            "CleanScope", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;   // 防止 UI 线程异常导致静默闪退
    }
}
