using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CleanScope.App.Wpf.Mvvm;

namespace CleanScope.App.Wpf.Views;

/// <summary>
/// 问题#2 统一确认弹窗: 自绘、贴合项目视觉系统 (取代 Windows 自带 MessageBox)。
/// 任意风险等级的"移入回收站"、批量删除、官方清理手段执行前都经此再确认;
/// 高风险项 (<see cref="ConfirmDialogModel.IsHighRisk"/>) 红色强调 + 必须勾选确认框才放行。
///
/// 红线不变: 这里只是 UI 确认层; 真正放行与否仍由安全闸门裁决, 且永远只移入回收站 (可还原)。
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(ConfirmDialogModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    /// <summary>显示确认弹窗; 用户点确定 (高风险须先勾选) 返回 true, 取消/关闭返回 false。</summary>
    public static bool Show(Window? owner, ConfirmDialogModel model)
    {
        var dlg = new ConfirmDialog(model)
        {
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };
        return dlg.ShowDialog() == true;
    }

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}

/// <summary>确认弹窗的内容模型 (一项/批量/官方手段共用)。</summary>
public sealed class ConfirmDialogModel : ViewModelBase
{
    public string Title { get; init; } = "请确认";
    public string Intro { get; init; } = "";
    public string WarningText { get; init; } = "";
    public string ConfirmText { get; init; } = "确定";
    public string CancelText { get; init; } = "取消";

    /// <summary>高风险: 顶部红带 + 红色确认按钮 + 强制勾选确认框。</summary>
    public bool IsHighRisk { get; init; }

    public IReadOnlyList<DetailRow> Details { get; init; } = System.Array.Empty<DetailRow>();
    public bool HasDetails => Details.Count > 0;
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningText);

    /// <summary>勾选确认文案; 非空则显示确认框, 且必须勾选才能点"确定"。</summary>
    public string CheckText { get; init; } = "";
    public bool HasCheck => !string.IsNullOrWhiteSpace(CheckText);

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set { if (SetField(ref _isChecked, value)) OnPropertyChanged(nameof(CanConfirm)); }
    }

    /// <summary>无需勾选时恒可确定; 需勾选时须先勾选。</summary>
    public bool CanConfirm => !HasCheck || _isChecked;

    /// <summary>明细行 (标签 | 值)。</summary>
    public static DetailRow Row(string label, string value) => new(label, value);

    public static IReadOnlyList<DetailRow> Rows(params (string label, string value)[] rows) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(r.value))
            .Select(r => new DetailRow(r.label, r.value)).ToList();
}

public sealed record DetailRow(string Label, string Value);
