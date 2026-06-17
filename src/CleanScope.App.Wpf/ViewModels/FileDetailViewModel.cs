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
        BackCommand = new RelayCommand(_host.ShowList);
        OpenFolderCommand = new AsyncRelayCommand(_ => DoActionAsync(ActionType.OpenDir));
        CopyPathCommand = new AsyncRelayCommand(_ => CopyPathAsync());
        AddIgnoreCommand = new AsyncRelayCommand(_ => DoActionAsync(ActionType.AddIgnore));
    }

    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand CopyPathCommand { get; }
    public AsyncRelayCommand AddIgnoreCommand { get; }

    private FileRowViewModel? _row;
    public FileRowViewModel? Row { get => _row; private set => SetField(ref _row, value); }

    private string _actionStatus = "";
    public string ActionStatus { get => _actionStatus; private set => SetField(ref _actionStatus, value); }

    // —— AI 解释 (S6: 详情页按需生成, 扫描不再批量串行) ——
    private AiExplanationViewModel? _ai;
    public AiExplanationViewModel? Ai
    {
        get => _ai;
        private set { if (SetField(ref _ai, value)) OnPropertyChanged(nameof(HasAi)); }
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

        // AI 解释: 批量已得则直接用; 否则按需生成 (脱敏后请求, 仅供参考)。
        Ai = row.Ai;
        AiStatus = "";
        if (Ai is null && _services.AiEnabled && row.Analysis.RuleMatch?.IsSystemCritical != true)
        {
            AiStatus = "正在生成 AI 解释（脱敏后请求，仅供参考）…";
            _ = LoadAiAsync(row);
        }
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

    // 以"移入回收站"意图询问闸门 —— MVP 必被拒 (SR-1)。纯判定, 绝不执行。
    private void EvaluateGate(FileRowViewModel row)
    {
        var request = new ActionRequest(null, row.Path, ActionType.MoveToRecycleBin);
        var decision = _services.SafetyGuard.Evaluate(request, row.Analysis.RuleMatch, row.Analysis.Risk);
        var head = decision.Outcome == GuardOutcome.Rejected ? "🔒 删除被安全闸门拒绝" : "⚠ 闸门返回放行（MVP 不应出现）";
        GuardVerdict = string.IsNullOrWhiteSpace(decision.RecommendedAlternative)
            ? $"{head}：{decision.Reason}"
            : $"{head}：{decision.Reason}　建议：{decision.RecommendedAlternative}";
    }

    private async Task CopyPathAsync()
    {
        if (Row is null) return;
        try { Clipboard.SetText(Row.Path); } catch { /* 剪贴板偶发占用, 忽略 */ }
        await DoActionAsync(ActionType.CopyPath, silent: true);
        ActionStatus = "已复制路径到剪贴板。";
    }

    // 辅助操作: 经闸门放行 → 执行器 (先写审计后执行)。这些操作均无破坏性。
    private async Task DoActionAsync(ActionType action, bool silent = false)
    {
        if (Row is null) return;
        var request = new ActionRequest(null, ActionTarget(action), action);
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

    private string ActionTarget(ActionType action) => Row!.Path;

    private static string Describe(ActionType action) => action switch
    {
        ActionType.OpenDir => "已在资源管理器中打开所在位置。",
        ActionType.AddIgnore => "已加入忽略名单（后续扫描不再提示）。",
        ActionType.CopyPath => "已复制路径。",
        _ => "操作已完成。",
    };
}
