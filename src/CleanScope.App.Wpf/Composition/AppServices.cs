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

    /// <summary>AI 解释是否启用 (脱敏后出云); 未配置密钥则为 false, 全程本地。</summary>
    public required bool AiEnabled { get; init; }

    /// <summary>按需 AI 注解器 (S6): 详情页打开时按需解释单项 (缓存复用), 扫描不再批量串行。</summary>
    public required AiAnnotator Annotator { get; init; }

    public required string AppVersion { get; init; }
}
