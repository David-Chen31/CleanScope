namespace CleanScope.Domain.Abstractions;

// 外围契约 (报告/规则源/系统访问)。仅签名 (T0.5)。

/// <summary>报告导出。MVP 仅 Markdown; 多格式经 Format 区分。</summary>
public interface IReportExporter
{
    string Format { get; }   // "markdown" | "html" | "json" | "csv"
    Task ExportAsync(ScanReport report, string outputPath, CancellationToken ct = default);
}

/// <summary>规则源: 加载声明式规则包 (rules/*.json)。规则是数据非代码 (架构§7)。</summary>
public interface IRuleSource
{
    Task<IReadOnlyList<RuleDefinition>> LoadAsync(CancellationToken ct = default);
}

/// <summary>
/// Windows 系统访问 (只读取证)。注册表/进程/数字签名/已安装应用/Appx。
/// 实现在 Infrastructure (net8.0-windows)。删除能力不在此 (仅 Safety 可改盘)。
/// </summary>
public interface IWindowsAccess
{
    Task<FileMetadata?> ReadMetadataAsync(string path, CancellationToken ct = default);
    IReadOnlyList<InstalledApp> GetInstalledApplications();
    string? GetOccupyingProcessName(string path);     // IR-2: 占用检测
    string ResolveRealPath(string path);              // IR-4: 解析符号链接/junction
}
