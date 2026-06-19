using System.Windows.Controls;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

public partial class AiSettingsView : UserControl
{
    public AiSettingsView()
    {
        InitializeComponent();
        // 进入页面时把已保存的 Key 回填到 PasswordBox (PasswordBox 不支持直接绑定)。
        DataContextChanged += (_, _) =>
        {
            if (DataContext is AiSettingsViewModel vm && KeyBox.Password != vm.ApiKey)
                KeyBox.Password = vm.ApiKey;
        };
    }

    // PasswordBox 出于安全不支持绑定 Password; 用代码把变更推给 VM。
    private void KeyBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is AiSettingsViewModel vm) vm.ApiKey = KeyBox.Password;
    }
}
