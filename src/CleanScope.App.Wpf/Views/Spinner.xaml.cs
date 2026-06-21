using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CleanScope.App.Wpf.Views;

/// <summary>复用型加载转圈 (问题#2)。Diameter 控制大小, Stroke 控制颜色 (默认强调蓝)。</summary>
public partial class Spinner : UserControl
{
    public Spinner() => InitializeComponent();

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(Spinner), new PropertyMetadata(14.0));

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Spinner),
        new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))));   // Accent

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }
}
