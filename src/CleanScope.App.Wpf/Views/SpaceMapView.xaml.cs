using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.ViewModels;

namespace CleanScope.App.Wpf.Views;

/// <summary>
/// 空间地图视图。treemap 用 <see cref="TreemapLayout"/> 在 Canvas 上自绘 (无第三方控件):
/// 当前层变化或尺寸变化时重排矩形; 点击块委托给 VM (下钻/跳详情)。
/// </summary>
public partial class SpaceMapView : UserControl
{
    private SpaceMapViewModel? _vm;

    public SpaceMapView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.Changed -= Render;
        _vm = DataContext as SpaceMapViewModel;
        if (_vm is not null) _vm.Changed += Render;
        Render();
    }

    private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Render();

    private void Render()
    {
        MapCanvas.Children.Clear();
        var node = _vm?.Current;
        if (node is null) return;

        double w = MapCanvas.ActualWidth, h = MapCanvas.ActualHeight;
        if (w < 8 || h < 8) return;

        var children = node.Children;
        if (children.Count == 0) return;

        const double gap = 2;
        var tiles = TreemapLayout.Squarify(children.Select(c => c.Size).ToList(), w, h);
        foreach (var t in tiles)
        {
            var child = children[t.Index];
            double tw = t.W - gap, th = t.H - gap;
            if (tw < 1 || th < 1) continue;

            // 按 A–E 风险等级着色 (地图本职 = 风险可视化, 五色可分; 个人文件中性化只留在清单徽章)。
            var fill = RiskPalette.Brush(child.Risk);
            // P2: 悬停即解释 —— 名称/大小/风险等级人话 + 是否可清理。
            var nav = child.HasChildren ? "\n(点击下钻)" : child.Row is not null ? "\n(点击查看详情)" : "";
            var border = new Border
            {
                Width = tw,
                Height = th,
                Background = child.IsRemainder ? new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEE)) : fill,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = child is { IsRemainder: false } && (child.HasChildren || child.Row is not null)
                    ? Cursors.Hand : Cursors.Arrow,
                ToolTip = $"{child.Name}\n{child.SizeText}\n风险：{child.Grade.Label}"
                    + (child.IsCleanable ? "\n✓ 可清理（可回收）" : "") + nav,
                Tag = child,
            };

            // 仅在足够大时绘制标签: 等级字母前缀 (与清单/详情同一套徽章) + 名称·大小; 可清理项加 ✓ 角标 (双编码)。
            if (tw > 56 && th > 26)
            {
                var content = new Grid();
                content.Children.Add(new TextBlock
                {
                    Text = (child.Grade.HasLetter ? child.Grade.Letter + "  " : "") + child.Label,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x33)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Top,
                });
                if (child.IsCleanable)
                    content.Children.Add(new TextBlock
                    {
                        Text = "✓",
                        Margin = new Thickness(0, 3, 6, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x7E, 0x34)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                    });
                border.Child = content;
            }

            if (!child.IsRemainder)
                border.MouseLeftButtonUp += OnTileClick;

            Canvas.SetLeft(border, t.X);
            Canvas.SetTop(border, t.Y);
            MapCanvas.Children.Add(border);
        }
    }

    private void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: TreeNodeViewModel node }) _vm?.Activate(node);
    }
}
