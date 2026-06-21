using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Diagnostics;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 问题#3: 应用内诊断日志查看 (不再让用户自己去开文件)。读取本地日志尾部展示, 支持刷新/复制/清空/打开目录。
/// 面向小白: 顶部用一句话点出"最近一次问题", 并提示可截图发开发者。日志仅本地, 从不上传。
/// </summary>
public sealed class LogViewerViewModel : ViewModelBase
{
    public LogViewerViewModel()
    {
        RefreshCommand = new RelayCommand(Refresh);
        CopyCommand = new RelayCommand(Copy, () => !IsEmpty);
        ClearCommand = new RelayCommand(Clear);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        Refresh();
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public string LogPath => AppLog.LogPath;

    private string _content = "";
    public string Content
    {
        get => _content;
        private set { if (SetField(ref _content, value)) { OnPropertyChanged(nameof(IsEmpty)); CopyCommand.RaiseCanExecuteChanged(); } }
    }
    public bool IsEmpty => string.IsNullOrWhiteSpace(_content);

    // 顶部"最近一次问题"横幅 (来自 AppTrace.LastError) —— 让小白一眼看到症结, 而非自己读全篇日志。
    private string _lastIssue = "";
    public string LastIssue { get => _lastIssue; private set { if (SetField(ref _lastIssue, value)) OnPropertyChanged(nameof(HasLastIssue)); } }
    public bool HasLastIssue => !string.IsNullOrWhiteSpace(_lastIssue);

    private string _status = "";
    public string Status { get => _status; private set => SetField(ref _status, value); }

    private void Refresh()
    {
        Content = AppLog.ReadRecent();
        LastIssue = AppTrace.LastError ?? "";
        Status = IsEmpty
            ? "暂无日志记录。出现 AI 失败（鉴权/模型名/网络/截断等）后，这里会自动记录原因。"
            : $"已加载日志：{LogPath}";
    }

    private void Copy()
    {
        try { Clipboard.SetText(_content); Status = "已复制全部日志到剪贴板（可粘贴发给开发者）。"; }
        catch { Status = "复制失败（剪贴板被占用），请稍后重试。"; }
    }

    private void Clear()
    {
        AppLog.Clear();
        Refresh();
        Status = "已清空日志。复现问题后再来这里查看新记录即可。";
    }

    private void OpenFolder()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(LogPath)!;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            Status = $"已打开日志所在文件夹：{dir}";
        }
        catch (System.Exception ex) { Status = $"打开文件夹失败：{ex.Message}"; }
    }
}
