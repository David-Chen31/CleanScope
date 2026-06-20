using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>
/// 文件详情 (T5.4): 属性 / 证据链(事实 vs AI 推测视觉区分) / 风险 / AI 解释 / 建议。
///
/// 红线: **不渲染任何删除入口** (MVP 零删除); 仅提供安全的辅助操作 (打开目录/复制路径/加忽略)。
/// 页内以只读方式展示"安全闸门判定", 证明即便发起删除意图也会被闸门拒绝 (SR-1)。
/// 辅助操作经 <see cref="IActionExecutor"/> 执行 (先写审计后执行, SR-9)。
/// </summary>
public sealed class FileDetailViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly INavigationHost _host;

    public FileDetailViewModel(AppServices services, INavigationHost host)
    {
        _services = services;
        _host = host;
        BackCommand = new RelayCommand(_host.BackFromDetail);   // 返回进入详情前的那一页 (按软件/空间地图/清单)
        OpenFolderCommand = new AsyncRelayCommand(_ => DoActionAsync(ActionType.OpenDir));
        CopyPathCommand = new AsyncRelayCommand(_ => CopyPathAsync());
        AddIgnoreCommand = new AsyncRelayCommand(_ => DoActionAsync(ActionType.AddIgnore));
        RunCommandCommand = new AsyncRelayCommand(_ => RunCommandAsync());
        CopyCommandCommand = new AsyncRelayCommand(_ => CopyCommandAsync());
        UninstallCommand = new AsyncRelayCommand(_ =>
            DoActionAsync(ActionType.OpenSettings, target: "ms-settings:appsfeatures"));
        MoveToRecycleBinCommand = new AsyncRelayCommand(_ => MoveToRecycleBinAsync());
        ExplainCommand = new AsyncRelayCommand(_ => ExplainAsync(), _ => CanExplain);
    }

    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand CopyPathCommand { get; }
    public AsyncRelayCommand AddIgnoreCommand { get; }
    public AsyncRelayCommand RunCommandCommand { get; }     // S-D: 运行官方清理命令
    public AsyncRelayCommand CopyCommandCommand { get; }    // S-D: 复制官方清理命令
    public AsyncRelayCommand UninstallCommand { get; }      // S-D: 打开卸载程序
    public AsyncRelayCommand MoveToRecycleBinCommand { get; }  // S-E: 移入回收站 (可恢复, 两步确认)
    public AsyncRelayCommand ExplainCommand { get; }           // 按需 AI 解释 (脱敏后请求, 非自动)

    /// <summary>AI 已配置且当前项可解释 (非系统关键、尚未解释) → 显示「AI 解释」按钮。</summary>
    public bool AiEnabled => _services.AiEnabled;
    public bool CanExplain => _services.AiEnabled && _ai is null && !_explaining
        && _row?.Analysis.RuleMatch?.IsSystemCritical != true;
    private bool _explaining;

    private FileRowViewModel? _row;
    public FileRowViewModel? Row { get => _row; private set => SetField(ref _row, value); }

    private string _actionStatus = "";
    public string ActionStatus { get => _actionStatus; private set => SetField(ref _actionStatus, value); }

    // —— AI 解释 (S6: 详情页按需生成, 扫描不再批量串行) ——
    private AiExplanationViewModel? _ai;
    public AiExplanationViewModel? Ai
    {
        get => _ai;
        private set { if (SetField(ref _ai, value)) { OnPropertyChanged(nameof(HasAi)); OnPropertyChanged(nameof(CanExplain)); ExplainCommand.RaiseCanExecuteChanged(); } }
    }
    public bool HasAi => _ai is not null;

    private string _aiStatus = "";
    public string AiStatus { get => _aiStatus; private set => SetField(ref _aiStatus, value); }

    // —— 安全闸门判定 (只读演示, 不执行) ——
    private string _guardVerdict = "";
    public string GuardVerdict { get => _guardVerdict; private set => SetField(ref _guardVerdict, value); }

    public void Show(FileRowViewModel row)
    {
        Row = row;
        ActionStatus = "";
        EvaluateGate(row);

        // AI 解释: 不再自动出云 (省 token); 若该项此前已生成过则直接显示, 否则等用户点「AI 解释」按钮。
        Ai = row.Ai;
        AiStatus = "";
        ExplainCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExplain));
    }

    // 按需: 用户点击「AI 解释」才发一次脱敏请求。
    private async Task ExplainAsync()
    {
        if (Row is null) return;
        _explaining = true;
        ExplainCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExplain));
        AiStatus = "正在生成 AI 解释（脱敏后请求，仅供参考）…";
        try { await LoadAiAsync(Row); }
        finally { _explaining = false; ExplainCommand.RaiseCanExecuteChanged(); OnPropertyChanged(nameof(CanExplain)); }
    }

    private async Task LoadAiAsync(FileRowViewModel row)
    {
        try
        {
            var ex = await _services.Annotator.AnnotateAsync(row.Analysis);
            if (!ReferenceEquals(_row, row)) return;          // 用户已切换到别的项
            if (ex is { Validated: true })
            {
                Ai = new AiExplanationViewModel(ex);
                AiStatus = "";
            }
            else
            {
                AiStatus = "AI 解释不可用或未通过校验（以引擎结论为准）。";
            }
        }
        catch
        {
            if (ReferenceEquals(_row, row))
                AiStatus = "AI 解释获取失败（以引擎结论为准）。";
        }
    }

    // 以"移入回收站"意图询问闸门 (纯判定, 绝不执行)。A/B 可清理项放行 (仅回收站可恢复); 其余拒绝并给理由。
    private void EvaluateGate(FileRowViewModel row)
    {
        var request = new ActionRequest(null, row.Path, ActionType.MoveToRecycleBin);
        var decision = _services.SafetyGuard.Evaluate(request, row.Analysis.RuleMatch, row.Analysis.Risk);
        var head = decision.Outcome == GuardOutcome.Rejected ? "🔒 不可删除" : "✅ 可移入回收站（可恢复）";
        GuardVerdict = string.IsNullOrWhiteSpace(decision.RecommendedAlternative)
            ? $"{head}：{decision.Reason}"
            : $"{head}：{decision.Reason}　建议：{decision.RecommendedAlternative}";
    }

    // S-E: 移入回收站 (可恢复)。两步确认 (C8) → 闸门复核 → 执行器先写审计后移入回收站。绝不永久删除。
    private async Task MoveToRecycleBinAsync()
    {
        if (Row is null) return;

        // 闸门先判: 被拒则直接告知理由, 连确认框都不弹。
        var probe = new ActionRequest(null, Row.Path, ActionType.MoveToRecycleBin);
        var verdict = _services.SafetyGuard.Evaluate(probe, Row.Analysis.RuleMatch, Row.Analysis.Risk);
        if (verdict.Outcome != GuardOutcome.Allowed)
        {
            ActionStatus = $"操作被拒：{verdict.Reason}";
            return;
        }

        // 两步确认 (C8): 明确告知"移入回收站、可还原、非永久删除"。
        var confirm = MessageBox.Show(
            $"确定把以下项移入回收站吗？（可从回收站还原，非永久删除）\n\n{Row.Path}\n\n大小：{Row.SizeText}　归属：{Row.OwnerApp ?? "未知"}",
            "移入回收站 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK)
        {
            ActionStatus = "已取消，未做任何改动。";
            return;
        }

        await DoActionAsync(ActionType.MoveToRecycleBin);
    }

    private async Task CopyPathAsync()
    {
        if (Row is null) return;
        try { Clipboard.SetText(Row.Path); } catch { /* 剪贴板偶发占用, 忽略 */ }
        await DoActionAsync(ActionType.CopyPath, silent: true);
        ActionStatus = "已复制路径到剪贴板。";
    }

    // S-D: 运行官方清理命令 (在可见终端; 删除由厂商工具完成, 我们不碰文件)。
    private async Task RunCommandAsync()
    {
        if (Row?.Command is null) return;
        await DoActionAsync(ActionType.RunCleanupCommand, payload: Row.Command);
        ActionStatus = $"已在终端运行官方清理命令：{Row.Command}";
    }

    private async Task CopyCommandAsync()
    {
        if (Row?.Command is null) return;
        try { Clipboard.SetText(Row.Command); } catch { /* 剪贴板偶发占用 */ }
        await DoActionAsync(ActionType.ShowCommand, silent: true);
        ActionStatus = $"已复制清理命令：{Row.Command}";
    }

    // 辅助操作: 经闸门放行 → 执行器 (先写审计后执行)。这些操作均无破坏性。
    private async Task DoActionAsync(ActionType action, string? target = null, string? payload = null, bool silent = false)
    {
        if (Row is null) return;
        var request = new ActionRequest(null, target ?? Row.Path, action, payload);
        var approval = _services.SafetyGuard.Evaluate(request, Row.Analysis.RuleMatch, Row.Analysis.Risk);
        var log = await _services.ActionExecutor.ExecuteAsync(request, approval);
        if (silent) return;
        ActionStatus = log.Result switch
        {
            ActionResult.Success => Describe(action),
            ActionResult.Rejected => $"操作被拒：{log.RejectReason}",
            _ => $"操作失败：{log.RejectReason}",
        };
    }

    private static string Describe(ActionType action) => action switch
    {
        ActionType.OpenDir => "已在资源管理器中打开所在位置。",
        ActionType.AddIgnore => "已加入忽略名单（后续扫描不再提示）。",
        ActionType.CopyPath => "已复制路径。",
        ActionType.OpenSettings => "已打开「应用和功能」，可在此卸载对应程序。",
        ActionType.RunCleanupCommand => "已在终端运行官方清理命令。",
        ActionType.MoveToRecycleBin => "已移入回收站（可在回收站还原，非永久删除）。",
        _ => "操作已完成。",
    };
}
