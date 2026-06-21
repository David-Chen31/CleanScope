using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

/// <summary>资源管理器树视图 (P1)。绑定为主; 仅两处轻量交互: 选中→详情面板 (C2)、右键先选中该行 (C3)。</summary>
public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
        // 选中行高亮用 SystemColors 静态资源 (无法 DynamicResource), 改由主题驱动 → 暗色不再是刺眼亮蓝。
        Loaded += (_, _) => { ApplySelectionBrushes(); ThemeManager.ThemeChanged += ApplySelectionBrushes; };
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= ApplySelectionBrushes;
    }

    private void ApplySelectionBrushes()
    {
        Brush Res(string key) => System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
        var sel = Res("AccentSoft");
        var ink = Res("Ink");
        var inactive = Res("Hover");
        Tree.Resources[SystemColors.HighlightBrushKey] = sel;
        Tree.Resources[SystemColors.HighlightTextBrushKey] = ink;
        Tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = inactive;
        Tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = ink;
    }

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
