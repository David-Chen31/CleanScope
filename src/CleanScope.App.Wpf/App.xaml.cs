using System.Windows;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf;

/// <summary>
/// 应用入口 (T5.1 组合根挂载)。启动时手写装配 <see cref="AppServices"/> → 构造 ShellViewModel →
/// 显示主窗口。装配失败 (如规则缺失) 弹窗提示, 不静默崩溃。
/// </summary>
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
            MessageBox.Show($"启动失败：{ex.GetType().Name}\n{ex.Message}", "CleanScope",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
