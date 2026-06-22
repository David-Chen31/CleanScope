using System.Windows;
using System.Windows.Controls;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    // P2: 打开"腾出空间向导"(非模态), 传入当前 HomeViewModel (含已扫描会话)。
    private void OpenWizard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is HomeViewModel home && home.Session is not null)
            SpaceWizardWindow.ShowFor(Window.GetWindow(this), home);
    }
}
