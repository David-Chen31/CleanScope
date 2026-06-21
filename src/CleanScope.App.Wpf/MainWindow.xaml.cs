using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CleanScope.App.Wpf.Common;

namespace CleanScope.App.Wpf;

/// <summary>
/// 主窗口 (自绘外壳)。WindowChrome 保留系统的缩放/贴靠/任务栏行为, 我们只接管标题栏视觉,
/// 让标题栏/侧栏/内容是同一块材质 (去"原生 Windows 窗口"感)。
/// </summary>
public partial class MainWindow : Window
{
    // Segoe MDL2 字形: 最大化 E922 / 还原 E923 (用码点构造, 避免源文件含私用区字符)。
    private static readonly string GlyphMaximize = char.ConvertFromUtf32(0xE922);
    private static readonly string GlyphRestore = char.ConvertFromUtf32(0xE923);

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnStateChanged;
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
        RestoreWindowGeometry();
        Closing += OnClosing;
        UpdateThemeIcon();
    }

    // C: 恢复上次窗口尺寸/位置/最大化 (位置越界则不还原, 回退居中)。
    private void RestoreWindowGeometry()
    {
        var p = UserPrefs.Current;
        if (p.HasWindowSize) { Width = p.WinWidth; Height = p.WinHeight; }
        if (p.HasWindowPos && IsOnScreen(p.WinLeft, p.WinTop, p.WinWidth, p.WinHeight))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = p.WinLeft;
            Top = p.WinTop;
        }
        if (p.WinMaximized) WindowState = WindowState.Maximized;
    }

    // C: 关闭时保存窗口几何 (最大化时存其还原后的尺寸, 以便下次还原态正常)。
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var p = UserPrefs.Current;
        var b = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        if (b.Width >= 320 && b.Height >= 320)
        {
            p.WinLeft = b.Left; p.WinTop = b.Top; p.WinWidth = b.Width; p.WinHeight = b.Height;
        }
        p.WinMaximized = WindowState == WindowState.Maximized;
        p.Save();
    }

    private static bool IsOnScreen(double left, double top, double w, double h)
    {
        double vx = SystemParameters.VirtualScreenLeft, vy = SystemParameters.VirtualScreenTop,
               vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        // 要求标题栏中点落在虚拟屏内, 且顶边不被任务栏/屏外吞掉 (容差 20px)。
        var cx = left + w * 0.5;
        return cx >= vx && cx <= vx + vw && top >= vy - 1 && top <= vy + vh - 20;
    }

    // B: Ctrl+F → 切到可清理清单并聚焦搜索框 (聚焦是视图职责, 故在此处理而非命令)。
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && DataContext is ViewModels.ShellViewModel shell && shell.GoExplorerCommand.CanExecute(null))
        {
            shell.GoExplorerCommand.Execute(null);
            Dispatcher.BeginInvoke(new Action(FocusSearchBox), System.Windows.Threading.DispatcherPriority.Background);
            e.Handled = true;
        }
    }

    private void FocusSearchBox()
    {
        if (FindDescendant<TextBox>(this, "SearchBox") is { } box) { box.Focus(); box.SelectAll(); }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T fe && fe.Name == name) return fe;
            if (FindDescendant<T>(child, name) is { } found) return found;
        }
        return null;
    }

    // E: 切换主题 + 更新图标 (浅色显示月亮=点击转深, 深色显示太阳=点击转浅)。
    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        var key = ThemeManager.Current == AppTheme.Light ? "IconMoon" : "IconSun";
        ThemeIcon.Data = (Geometry)FindResource(key);
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // 最大化时把内容内缩一圈, 避免越过屏幕边缘被裁切; 同时切换最大化/还原字形。
    private void OnStateChanged(object? sender, EventArgs e)
    {
        var max = WindowState == WindowState.Maximized;
        RootBorder.Padding = max ? new Thickness(7) : new Thickness(0);
        MaxBtn.Content = max ? GlyphRestore : GlyphMaximize;
    }

    // Win11 圆角窗口 (DWM); 旧系统调用失败则忽略 (无害)。
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int pref = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
        }
        catch { /* 非 Win11 / 无该属性: 忽略 */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
