using System.Collections.ObjectModel;
using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>节点右键操作契约 (E3): 复制 / 打开位置 / 移入回收站。由 <see cref="ExplorerViewModel"/> 实现。</summary>
public interface IExplorerActions
{
    void CopyToClipboard(string text, string okStatus);
    Task OpenLocationAsync(string path);
    Task RecycleAsync(ExplorerNodeViewModel node);
}

/// <summary>
/// 资源管理器树视图 (P1): 把整盘当目录树浏览——可展开/折叠、显大小与占比、按大小排序、标来源/用途/可清理。
/// E3: 每行支持右键复制路径/用途、在系统资源管理器打开、移入回收站 (走安全闸门 + 两步确认, 仅可恢复)。
/// </summary>
public sealed class ExplorerViewModel : ViewModelBase, IExplorerActions
{
    private readonly AppServices _services;

    public ExplorerViewModel(AppServices services) => _services = services;

    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = new();

    private string _summary = "扫描后在此像目录树一样浏览整个磁盘。";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    private string _actionStatus = "";
    public string ActionStatus { get => _actionStatus; private set => SetField(ref _actionStatus, value); }

    public void Load(ScanSession session)
    {
        Roots.Clear();
        ActionStatus = "";
        if (session.Tree is null)
        {
            Summary = "本次扫描未生成目录树。";
            return;
        }

        var root = new ExplorerNodeViewModel(session.Tree, session.Tree.Size, withinCleanable: false, actions: this)
        { IsExpanded = true };
        Roots.Add(root);
        Summary = $"{session.TargetPath} — 共 {Format.HumanSize(session.Tree.Size)}；" +
                  $"其中 ✓可清理约 {Format.HumanSize(session.TreeReclaimable)}（{session.TreeCleanableCount} 处，含各软件内部缓存）。" +
                  "点击 ▸ 展开目录；右键可复制路径/打开位置/移入回收站。";
    }

    // —— IExplorerActions ——

    public void CopyToClipboard(string text, string okStatus)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); ActionStatus = $"{okStatus}：{text}"; }
        catch { ActionStatus = "复制失败（剪贴板被占用），请稍后重试。"; }
    }

    public Task OpenLocationAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return Task.CompletedTask;
        var request = new ActionRequest(null, path, ActionType.OpenDir);
        var approval = _services.SafetyGuard.Evaluate(request, null, null);   // 只读操作, 闸门直接放行
        return RunAsync(request, approval, "已在系统资源管理器中打开位置。");
    }

    // E3 删除: 安全闸门当场判定 (黑名单/容器/A·B/占用按路径独立强制) → 两步确认 → 执行器先写审计后仅移入回收站。
    // 绝不永久删除; 被拒则只提示理由, 连确认框都不弹。
    public async Task RecycleAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanRecycle) return;

        var request = new ActionRequest(null, node.Path, ActionType.MoveToRecycleBin);
        var verdict = _services.SafetyGuard.Evaluate(request, null, node.ToRiskAssessment());
        if (verdict.Outcome != GuardOutcome.Allowed)
        {
            ActionStatus = string.IsNullOrWhiteSpace(verdict.RecommendedAlternative)
                ? $"不可删除：{verdict.Reason}"
                : $"不可删除：{verdict.Reason}　建议：{verdict.RecommendedAlternative}";
            return;
        }

        var confirm = MessageBox.Show(
            $"确定把以下项移入回收站吗？（可从回收站还原，非永久删除）\n\n{node.Path}\n\n大小：{node.SizeText}　来源：{node.Origin}",
            "移入回收站 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { ActionStatus = "已取消，未做任何改动。"; return; }

        var log = await _services.ActionExecutor.ExecuteAsync(request, verdict);
        if (log.Result == ActionResult.Success)
        {
            node.MarkDeleted();
            ActionStatus = $"已移入回收站（可还原）：{node.Name}";
        }
        else
        {
            ActionStatus = $"操作未完成：{log.RejectReason}";
        }
    }

    private async Task RunAsync(ActionRequest request, GuardDecision approval, string okStatus)
    {
        var log = await _services.ActionExecutor.ExecuteAsync(request, approval);
        ActionStatus = log.Result == ActionResult.Success ? okStatus : $"操作未完成：{log.RejectReason}";
    }
}
