using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CleanScope.App.Wpf.Common;

/// <summary>
/// 极简 Markdown 行内渲染 (附加属性): 处理 **加粗** 与换行, 把 AI 文本里的标记渲染成真正的样式。
/// 用于 AI 清理行动计划等 —— 以前 TextBlock 直接显示 ** 字面量, 现在渲染为加粗 (报告导出仍保留原始 markdown)。
/// </summary>
public static class MarkdownInline
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(MarkdownInline),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);
    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not string text || text.Length == 0) return;
        foreach (var inline in Parse(text)) tb.Inlines.Add(inline);
    }

    // 按 ** 切分: 偶数段普通、奇数段加粗; 段内换行转 LineBreak。AI 输出格式良好, 不处理嵌套/转义。
    private static IEnumerable<Inline> Parse(string text)
    {
        var parts = text.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            var seg = parts[i];
            if (seg.Length == 0) continue;
            var bold = i % 2 == 1;
            var lines = seg.Split('\n');
            for (var j = 0; j < lines.Length; j++)
            {
                if (j > 0) yield return new LineBreak();
                var run = new Run(lines[j].TrimEnd('\r'));
                if (bold) run.FontWeight = FontWeights.SemiBold;
                yield return run;
            }
        }
    }
}
