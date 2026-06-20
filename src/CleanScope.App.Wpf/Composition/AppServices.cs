using System.Net.Http;
using CleanScope.Ai.Chat;
using CleanScope.Ai.Sanitization;
using CleanScope.Application;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Entities;
using CleanScope.Domain.Models;

namespace CleanScope.App.Wpf.Composition;

/// <summary>
/// 组合根产物: 已装配的应用服务 (供 ViewModel 调用)。
/// UI 层只持有领域抽象 + 编排用例, 不知具体实现 (组合在 <see cref="CompositionRoot"/> 集中完成)。
/// 删除能力不在此暴露: 唯一可改盘路径是 Safety 闸门, MVP 永不放行 (零删除)。
/// </summary>
public sealed class AppServices
{
    public required ScanAndAnalyzeUseCase UseCase { get; init; }

    /// <summary>按导出路径扩展名选报告导出器 (.csv → CSV, 否则 Markdown)。</summary>
    public required Func<string, IReportExporter> ReportExporterFor { get; init; }
    public required IIgnoreRepository IgnoreRepository { get; init; }
    public required IActionExecutor ActionExecutor { get; init; }
    public required ISafetyGuard SafetyGuard { get; init; }

    /// <summary>可热替换的对话客户端 (D): 设置页保存后即时生效, 无需重启。</summary>
    public required MutableAiChat AiChat { get; init; }

    /// <summary>共享 HttpClient (检索模型 / 测试连通性 / 对话)。</summary>
    public required HttpClient Http { get; init; }

    /// <summary>出云脱敏网关 (问题#3): 设置页改档位后即时生效 (与 AiChat 共享同一实例, 注解/解释两路都受其约束)。</summary>
    public required SanitizationGateway Sanitizer { get; init; }

    /// <summary>AI 解释是否启用 (脱敏后出云); 随 <see cref="AiChat"/> 当前配置实时反映, 未配置则全程本地。</summary>
    public bool AiEnabled => AiChat.Enabled;

    /// <summary>当前 AI 配置 (供设置页预填; Key 为用户自有, 不外传)。</summary>
    public AiOptions CurrentAiOptions { get; private set; } = AiOptions.Disabled;

    /// <summary>按需 AI 注解器 (S6): 详情页打开时按需解释单项 (缓存复用), 扫描不再批量串行。</summary>
    public required AiAnnotator Annotator { get; init; }

    /// <summary>整盘清理参谋 (S-H): 对脱敏聚合做一次跨项建议; AI 未配置则 Enabled=false。</summary>
    public ICleanupAdvisor? CleanupAdvisor { get; init; }

    /// <summary>AI 配置变化 (设置页保存后) → UI 刷新徽章/菜单可见性。</summary>
    public event Action? AiChanged;

    /// <summary>初始化当前配置 (组合根装配时调用一次)。</summary>
    public void InitAiOptions(AiOptions options)
    {
        CurrentAiOptions = options;
        Sanitizer.Level = options.Sanitization;
    }

    /// <summary>D 运行时重组: 用新配置热替换对话客户端 + 更新脱敏档位 + 持久化(DPAPI 加密 Key) + 广播。</summary>
    public void ReconfigureAi(AiOptions options)
    {
        CurrentAiOptions = options;
        AiChat.Reconfigure(options);
        Sanitizer.Level = options.Sanitization;
        AiConfigStore.Save(options);
        AiChanged?.Invoke();
    }

    /// <summary>系统级官方清理手段目录 (P0): 关闭休眠/清空回收站/DISM/磁盘清理等, 确定性检测, 经官方命令执行。</summary>
    public required IReadOnlyList<OfficialCleanupAction> OfficialActions { get; init; }

    /// <summary>跨盘目录迁移器 (P0): 把占大头但不能删的合法软件目录搬到其他盘 + 建目录联接 (绝不永久删除)。</summary>
    public required IDirectoryMigrator Migrator { get; init; }

    public required string AppVersion { get; init; }
}
