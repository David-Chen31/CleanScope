using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CleanScope.App.Wpf.Views;

/// <summary>
/// 问题#4 手动处置高风险项的"强确认"对话框: 比普通移入回收站更重的确认 —— 必须勾选"我确认这是我自己的数据、
/// 自行承担风险"复选框, 确定按钮才可用。纯代码构建 (无 XAML), 自包含。
///
/// 红线提示: 仍然只是移入回收站 (可恢复), 绝非永久删除; 系统关键/容器/占用项即便勾选也会被安全闸门拦下。
/// </summary>
public static class ManualDeleteDialog
{
    private static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush SubInk = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    /// <summary>显示强确认; 用户勾选并点"确定移入回收站"返回 true, 否则 false。</summary>
    public static bool Confirm(Window? owner, string name, string path, string sizeText, string origin, string riskHint)
    {
        var ok = new Button
        {
            Content = "确定移入回收站",
            Padding = new Thickness(14, 6, 14, 6),
            IsEnabled = false,
            Background = Danger,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        var cancel = new Button
        {
            Content = "取消",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true,
        };

        var check = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "我确认这是我自己的数据，了解风险并自行承担（仍可从回收站还原）。",
                TextWrapping = TextWrapping.Wrap,
            },
            Foreground = Ink,
        };

        var title = new TextBlock
        {
            Text = "手动移入回收站（高风险项）",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Danger,
        };
        var warn = new TextBlock
        {
            Text = $"此项未被识别为可安全清理（{riskHint}）。仅在你确认这是你自己的数据（如自己下载的文件夹）时才继续。"
                   + "本操作只把它移入回收站，可随时还原；但请自行确认其用途。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Ink,
        };
        var detail = new TextBlock
        {
            Text = $"名称：{name}\n路径：{path}\n大小：{sizeText}　来源：{origin}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = SubInk,
        };

        check.Checked += (_, _) => ok.IsEnabled = true;
        check.Unchecked += (_, _) => ok.IsEnabled = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(title);
        panel.Children.Add(warn);
        panel.Children.Add(detail);
        panel.Children.Add(check);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "手动处置 — CleanScope",
            Content = panel,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = owner,
        };

        var result = false;
        ok.Click += (_, _) => { result = true; dialog.DialogResult = true; };
        return dialog.ShowDialog() == true && result;
    }
}
