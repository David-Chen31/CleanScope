using System.Windows;
using System.Windows.Input;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

/// <summary>
/// P2: 引导式"腾出空间"向导窗口。非模态打开 (Show) —— 这样第 1 步"一键清理"弹出的确认框
/// (属主为主窗口) 不会被本窗口挡住或因属主被禁用而卡死。
/// </summary>
public partial class SpaceWizardWindow : Window
{
    private SpaceWizardWindow(SpaceWizardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += Close;
    }

    /// <summary>对当前扫描结果打开向导 (需已有 Session; 调用方保证)。</summary>
    public static void ShowFor(Window? owner, HomeViewModel home)
    {
        var win = new SpaceWizardWindow(new SpaceWizardViewModel(home)) { Owner = owner };
        win.Show();
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
