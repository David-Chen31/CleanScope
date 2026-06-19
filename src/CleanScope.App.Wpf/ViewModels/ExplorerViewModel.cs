using System.Collections.ObjectModel;
using System.Windows;
using CleanScope.App.Wpf.Common;
using CleanScope.App.Wpf.Composition;
using CleanScope.App.Wpf.Mvvm;
using CleanScope.Application;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.ViewModels;

/// <summary>节点右键操作契约 (E3): 复制 / 打开位置 / 移入回收站 / 按需 AI 识别。由 <see cref="ExplorerViewModel"/> 实现。</summary>
public interface IExplorerActions
{
    bool AiEnabled { get; }
    void CopyToClipboard(string text, string okStatus);
    Task OpenLocationAsync(string path);
    Task RecycleAsync(ExplorerNodeViewModel node);
    Task InvestigateAsync(ExplorerNodeViewModel node);
    Task MigrateAsync(ExplorerNodeViewModel node);
}

/// <summary>
/// 资源管理器树视图 (P1): 把整盘当目录树浏览——可展开/折叠、显大小与占比、按大小排序、标来源/用途/可清理。
/// E3: 每行支持右键复制路径/用途、在系统资源管理器打开、移入回收站 (走安全闸门 + 两步确认, 仅可恢复)。
/// </summary>
public sealed class ExplorerViewModel : ViewModelBase, IExplorerActions
{
    private readonly AppServices _services;
    private ScanSession? _session;

    public ExplorerViewModel(AppServices services) => _services = services;

    public ObservableCollection<ExplorerNodeViewModel> Roots { get; } = new();

    private string _summary = "扫描后在此像目录树一样浏览整个磁盘。";
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }

    private string _actionStatus = "";
    public string ActionStatus { get => _actionStatus; private set => SetField(ref _actionStatus, value); }

    // "只看可清理": 把深埋在谨慎容器(如 AppData\Local)里的可清理项一次性铺平, 不必逐层展开才找到 (如 %TEMP%)。
    private bool _showCleanableOnly;
    public bool ShowCleanableOnly
    {
        get => _showCleanableOnly;
        set { if (SetField(ref _showCleanableOnly, value)) Rebuild(); }
    }

    public void Load(ScanSession session)
    {
        _session = session;
        ActionStatus = "";
        Rebuild();
    }

    private void Rebuild()
    {
        Roots.Clear();
        var session = _session;
        if (session?.Tree is null)
        {
            Summary = "本次扫描未生成目录树。";
            return;
        }

        if (_showCleanableOnly)
        {
            // 扁平列出整盘所有"最顶层可清理"节点 (与概览的处数/总量同口径), 按大小降序; 比例条相对最大者。
            var items = ScanTreeStats.EnumerateCleanable(session.Tree);
            var max = items.Count > 0 ? items[0].Size : 1;
            foreach (var n in items)
                Roots.Add(new ExplorerNodeViewModel(n, max, withinCleanable: false, actions: this));
            Summary = $"只看可清理：{items.Count} 处，共约 {Format.HumanSize(session.TreeReclaimable)}" +
                      "（含深埋各软件内部缓存）。右键即可移入回收站（可还原）；取消勾选可看完整目录树。";
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

    public bool AiEnabled => _services.AiEnabled;

    // E5+ 按需 AI 识别 (最小用量): 只在用户点了右键"用 AI 识别"时, 对这一个目录发一次脱敏请求。
    // 确定性目录名启发(NameHeuristics)已先免费兜底; AI 仅消化它认不出的残余未知。未配置 AI 的用户菜单项不显示, 零开销。
    public async Task InvestigateAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanInvestigate) return;
        ActionStatus = $"正在用 AI 识别「{node.Name}」（脱敏后请求，仅供参考）…";

        // 构造轻量分析 (空证据、无逐项 I/O) 交注解器: 脱敏 → 解释 → 校验。AI 永不进入风险/删除判定。
        var fileNode = new FileNode(0, 0, null, node.Path, null, node.Name, node.RawIsDirectory, false,
            node.RawSize, null, null, null, AccessState.Accessible, null, DateTime.UtcNow);
        var analysis = new FileAnalysis(fileNode, new EvidenceBundle(0, null, Array.Empty<Evidence>()),
            RuleMatch: null, Array.Empty<AttributionCandidate>(), node.ToRiskAssessment(), Explanation: null);

        try
        {
            var ai = await _services.Annotator.AnnotateAsync(analysis);
            if (ai is not { Validated: true })
            {
                ActionStatus = $"AI 未能识别「{node.Name}」（以现有结论为准）。";
                return;
            }

            var owner = ai.OwnerApp?.Trim();
            var why = ai.UserFriendlyExplanation?.Trim();
            var what = ai.WhatIsIt?.Trim();
            var purpose = !string.IsNullOrWhiteSpace(why) ? why : what;
            var origin = !string.IsNullOrWhiteSpace(owner) ? $"{owner}（AI 推测）" : null;

            if (origin is null && string.IsNullOrWhiteSpace(purpose))
            {
                ActionStatus = $"AI 未能识别「{node.Name}」（以现有结论为准）。";
                return;
            }
            node.ApplyAiInvestigation(origin, purpose);
            ActionStatus = $"已用 AI 补充识别「{node.Name}」（推测，仅供参考）。";
        }
        catch
        {
            ActionStatus = $"AI 识别失败「{node.Name}」（以现有结论为准）。";
        }
    }

    // P0 跨盘迁移: 选目标盘 → 两步确认 → 迁移器 (复制/校验/审计/改名留备份/建联接, 绝不永久删除)。
    // 原目录改名留作 .cleanscope-bak 备份; 确认软件正常后用户可自行移入回收站释放原盘空间。
    public async Task MigrateAsync(ExplorerNodeViewModel node)
    {
        if (node is null || !node.CanMigrate) return;

        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"选择把「{node.Name}」迁移到的目标位置（请选其他磁盘的一个文件夹）",
        };
        if (picker.ShowDialog() != true) { ActionStatus = "已取消迁移。"; return; }
        var targetRoot = picker.FolderName;

        var confirm = MessageBox.Show(
            $"将把以下目录迁移到其他磁盘，并在原位创建目录联接（对软件透明，照常使用）：\n\n" +
            $"源：{node.Path}\n目标：{targetRoot}\n\n" +
            "过程：复制到目标盘 → 校验 → 原目录改名留作备份 → 建立联接。\n" +
            "我们不会永久删除任何数据；释放原盘空间需你之后确认软件正常、再删除那个 .cleanscope-bak 备份。\n\n确定继续吗？",
            "迁移到其他盘 — CleanScope",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) { ActionStatus = "已取消，未做任何改动。"; return; }

        ActionStatus = $"正在迁移「{node.Name}」…（大目录可能需要一些时间）";
        try
        {
            var result = await Task.Run(() => _services.Migrator.MigrateAsync(new MigrationRequest(node.Path, targetRoot)));
            ActionStatus = result.Outcome switch
            {
                MigrationOutcome.Success => $"✅ 已迁移「{node.Name}」。{result.Message}",
                MigrationOutcome.Rejected => $"未迁移：{result.Message}",
                _ => $"迁移未完成：{result.Message}",
            };
        }
        catch (Exception ex)
        {
            ActionStatus = $"迁移失败「{node.Name}」：{ex.Message}";
        }
    }

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
