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

        // E: 启动即应用已保存的主题 (浅/深), 在任何窗口显示前生效, 避免闪一下浅色。
        Common.ThemeManager.Initialize();

        // 跨层诊断: 让 AI/编排等下层的"静默降级"也落到同一日志文件 (问题#1)。
        CleanScope.Domain.Diagnostics.AppTrace.UseSink(AppLog.Write);

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
            ShowError("启动失败", $"{ex.GetType().Name}：{ex.Message}\n\n详情见日志：{AppLog.LogPath}");
            Shutdown(1);
        }
    }

    private bool _dialogOpen;
    private DateTime _lastDialog = DateTime.MinValue;

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("DispatcherUnhandledException", e.Exception);
        e.Handled = true;   // 防止 UI 线程异常导致静默闪退

        // 异常风暴 (如逐项绑定失败) 时只记日志, 不刷屏: 一次只弹一个, 且 5s 冷却。
        if (_dialogOpen || (DateTime.Now - _lastDialog).TotalSeconds < 5) return;
        _dialogOpen = true;
        try
        {
            ShowError("发生了一个错误", $"应用会尽量继续运行。\n\n{e.Exception.GetType().Name}：{e.Exception.Message}\n\n详情见日志：{AppLog.LogPath}");
        }
        finally
        {
            _lastDialog = DateTime.Now;
            _dialogOpen = false;
        }
    }

    // G: 主题化错误弹窗 (取代原生 MessageBox); 若自绘弹窗本身异常则回退原生, 保证错误总能被看到。
    private static void ShowError(string title, string message)
    {
        try { Views.ConfirmDialog.ShowMessage(Current?.MainWindow, title, message, error: true); }
        catch { MessageBox.Show($"{title}\n\n{message}", "CleanScope", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
