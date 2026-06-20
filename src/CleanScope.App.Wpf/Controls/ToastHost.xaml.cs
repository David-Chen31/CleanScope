using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;

namespace CleanScope.App.Wpf.Controls;

/// <summary>
/// 全局 Toast 宿主 (右下角)。订阅 <see cref="Toast.Posted"/>，在 UI 线程把消息入列，
/// 几秒后自动移除；带操作按钮的停留更久 (给用户点"打开回收站"等的时间)。
/// </summary>
public partial class ToastHost : UserControl
{
    private readonly ObservableCollection<ToastItem> _items = new();

    public ToastHost()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        Toast.Posted += OnPosted;
        Unloaded += (_, _) => Toast.Posted -= OnPosted;
    }

    private void OnPosted(ToastMessage m) => Dispatcher.Invoke(() => Add(m));

    private void Add(ToastMessage m)
    {
        var item = new ToastItem(m);
        _items.Add(item);
        if (_items.Count > 4) _items.RemoveAt(0);   // 最多同时显示 4 条

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(m.Action is null ? 3.2 : 6.0) };
        timer.Tick += (_, _) => { timer.Stop(); _items.Remove(item); };
        timer.Start();
        item.OnDismiss = () => { timer.Stop(); _items.Remove(item); };
    }
}

/// <summary>单条 Toast 的展示模型 (色条/图标/可选操作)。</summary>
public sealed class ToastItem
{
    public ToastItem(ToastMessage m)
    {
        Text = m.Text;
        (Icon, AccentBrush) = m.Kind switch
        {
            ToastKind.Success => ("✓", Brush("#157347")),
            ToastKind.Error => ("✗", Brush("#C5221F")),
            _ => ("ℹ", Brush("#2563EB")),
        };
        ActionText = m.ActionText;
        HasAction = !string.IsNullOrWhiteSpace(m.ActionText);
        ActionCommand = new RelayCommand(() => { m.Action?.Invoke(); OnDismiss?.Invoke(); });
    }

    public string Text { get; }
    public string Icon { get; }
    public Brush AccentBrush { get; }
    public string? ActionText { get; }
    public bool HasAction { get; }
    public RelayCommand ActionCommand { get; }
    public Action? OnDismiss { get; set; }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
