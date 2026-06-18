using System.Windows.Controls;

namespace CleanScope.App.Wpf.Views;

/// <summary>资源管理器树视图 (P1)。纯 XAML 绑定 + TreeView 虚拟化/惰性展开, 无代码逻辑。</summary>
public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();
}
