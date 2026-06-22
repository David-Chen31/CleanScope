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
/// 删除红线: **唯一删除入口 = 移入回收站 (可还原), 绝不永久删除**; 与「目录浏览」一致, 单一入口覆盖全部风险等级
/// (A/B 普通确认, C-E 经闸门 override 走高风险强确认), 命中系统关键/容器/占用者闸门一律拒绝。
/// 页内以只读方式展示"安全闸门判定"。辅助操作经 <see cref="IActionExecutor"/> 执行 (先写审计后执行, SR-9)。
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
    /// <summary>问题#2: AI 解释生成中 → 显示转圈, 避免用户以为卡住。</summary>
    public bool IsExplaining
    {
        get => _explaining;
        private set { if (SetField(ref _explaining, value)) { OnPropertyChanged(nameof(CanExplain)); ExplainCommand.RaiseCanExecuteChanged(); } }
    }

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
        IsExplaining = true;
        AiStatus = "正在生成 AI 解释（脱敏后请求，仅供参考）…";
        try { await LoadAiAsync(Row); }
        finally { IsExplaining = false; }
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

    // 以"移入回收站"意图询问闸门 (纯判定, 绝不执行)。与「目录浏览」一致的两级判定:
    //   常规放行 (A/B) → ✅; 常规被拒但 override 可放行 (C-E, 仅风险高非红线) → ⚠ 需强确认;
    //   连 override 都拒 (黑名单/容器/占用红线) → 🔒 不可删除。
    private void EvaluateGate(FileRowViewModel row)
    {
        var normal = _services.SafetyGuard.Evaluate(
            new ActionRequest(null, row.Path, ActionType.MoveToRecycleBin), row.Analysis.RuleMatch, row.Analysis.Risk);
        if (normal.Outcome == GuardOutcome.Allowed)
        {
            GuardVerdict = $"✅ 可移入回收站（可还原）：{normal.Reason}";
            return;
        }
        var manual = _services.SafetyGuard.Evaluate(
            new ActionRequest(null, row.Path, ActionType.MoveToRecycleBin, UserOverride: true), row.Analysis.RuleMatch, row.Analysis.Risk);
        if (manual.Outcome == GuardOutcome.Allowed)
        {
            GuardVerdict = $"⚠ 风险偏高：删除需二次强确认，移入回收站仍可还原。（{normal.Reason}）";
            return;
        }
        GuardVerdict = string.IsNullOrWhiteSpace(manual.RecommendedAlternative)
            ? $"🔒 不可删除：{manual.Reason}"
            : $"🔒 不可删除：{manual.Reason}　建议：{manual.RecommendedAlternative}";
    }

    // S-E: 移入回收站 (可恢复)。与「目录浏览」一致的单一入口覆盖全部风险等级:
    //   ① 常规过闸门 (A/B) → 放行 → 普通确认。
    //   ② 常规被拒 (风险高) → 以 UserOverride 复判; 放行 → 高风险强确认 (红 + 必须勾选)。
    //   ③ 连 override 都拒 → 命中系统关键/容器/占用红线, 不可删除, 只如实告知理由。
    // 闸门复核 → 执行器先写审计后移入回收站; 绝不永久删除; 删后广播扣减 + 累计战绩 + Toast 撤销。
    private async Task MoveToRecycleBinAsync()
    {
        if (Row is null) return;
        var row = Row;

        var normal = new ActionRequest(null, row.Path, ActionType.MoveToRecycleBin);
        var verdict = await Task.Run(() => _services.SafetyGuard.Evaluate(normal, row.Analysis.RuleMatch, row.Analysis.Risk));
        var request = normal;
        var highRisk = false;

        if (verdict.Outcome != GuardOutcome.Allowed)
        {
            var manual = new ActionRequest(null, row.Path, ActionType.MoveToRecycleBin, UserOverride: true);
            var manualVerdict = await Task.Run(() => _services.SafetyGuard.Evaluate(manual, row.Analysis.RuleMatch, row.Analysis.Risk));
            if (manualVerdict.Outcome != GuardOutcome.Allowed)
            {
                ActionStatus = string.IsNullOrWhiteSpace(manualVerdict.RecommendedAlternative)
                    ? $"操作被拒：{manualVerdict.Reason}"
                    : $"操作被拒：{manualVerdict.Reason}　建议：{manualVerdict.RecommendedAlternative}";
                return;
            }
            verdict = manualVerdict;
            request = manual;
            highRisk = true;
        }

        var model = highRisk
            ? new Views.ConfirmDialogModel
            {
                Title = "移入回收站（高风险项）",
                IsHighRisk = true,
                Intro = "此项未被识别为可安全清理，等级偏高。仅在你确认这是你自己的、了解其用途的数据时才继续。",
                Details = Views.ConfirmDialogModel.Rows(
                    ("名称", row.Name), ("路径", row.Path), ("大小", row.SizeText),
                    ("归属", row.Origin), ("风险", row.RiskMeaning)),
                WarningText = "移入回收站后仍可还原，但请自行确认它不是某个程序/系统正在使用的数据。",
                CheckText = "我确认这是我自己的数据，了解风险并自行承担（仍可从回收站还原）。",
                ConfirmText = "确认移入回收站",
            }
            : new Views.ConfirmDialogModel
            {
                Title = "移入回收站",
                Intro = "确定把以下项移入回收站吗？可从回收站随时还原，非永久删除。",
                Details = Views.ConfirmDialogModel.Rows(
                    ("名称", row.Name), ("路径", row.Path), ("大小", row.SizeText), ("归属", row.Origin)),
                ConfirmText = "移入回收站",
            };
        if (!Views.ConfirmDialog.Show(System.Windows.Application.Current?.MainWindow, model))
        {
            ActionStatus = "已取消，未做任何改动。";
            return;
        }

        ActionStatus = $"正在移入回收站「{row.Name}」…";
        var log = await Task.Run(() => _services.ActionExecutor.ExecuteAsync(request, verdict));
        if (!ReferenceEquals(_row, row)) return;   // 用户已切到别的项
        if (log.Result == ActionResult.Success)
        {
            row.MarkDeleted();
            _host.Session?.NotifyRemoved(row.Path, row.Size, row.CanRecycle);   // 广播扣减 (高风险项 CanRecycle=false, 不计入可清理量)
            Common.UserPrefs.Current.AddCleaned(row.Size, 1);
            _lastRecycled = (row.Path, row.Size, row.CanRecycle);
            GuardVerdict = "✅ 已移入回收站（可在回收站还原，非永久删除）。";
            ActionStatus = $"已移入回收站（可还原）：{row.Name}";
            Toast.Show($"已移入回收站：{row.Name}", ToastKind.Success, "撤销", () => _ = UndoRecycleAsync());
        }
        else
        {
            ActionStatus = $"操作未完成：{log.RejectReason}";
            Toast.Error($"未能移入回收站：{log.RejectReason}");
        }
    }

    private (string path, long size, bool cleanable)? _lastRecycled;

    // 撤销刚才的移入回收站: 还原到原位 + 回补累计战绩 + 广播还原。失败则提示去回收站手动还原。
    private async Task UndoRecycleAsync()
    {
        if (_lastRecycled is not { } last) return;
        var restored = await Task.Run(() => _services.RecycleRestore.TryRestore(last.path));
        if (restored)
        {
            _host.Session?.NotifyRestored(last.path, last.size, last.cleanable);
            Common.UserPrefs.Current.SubtractCleaned(last.size, 1);
            _lastRecycled = null;
            ActionStatus = "已撤销：已从回收站还原到原位。";
            Toast.Show("已还原到原位。", ToastKind.Success);
        }
        else
        {
            Toast.Show("未能自动还原，请在回收站手动还原。", ToastKind.Info);
        }
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
