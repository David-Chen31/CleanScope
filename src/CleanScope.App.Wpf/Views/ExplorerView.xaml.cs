using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

/// <summary>资源管理器树视图 (P1)。绑定为主; 仅两处轻量交互: 选中→详情面板 (C2)、右键先选中该行 (C3)。</summary>
public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();

    // C2: 选中节点同步到 VM, 供底部详情面板展示完整信息。
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ExplorerViewModel vm) vm.SelectedNode = e.NewValue as ExplorerNodeViewModel;
    }

    // C3: 右键不会默认选中行, 先把右键命中的项选中, 让上下文菜单明确作用于它。
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is TreeViewItem item) { item.IsSelected = true; item.Focus(); break; }
    }
}
