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

/// <summary>
/// 回收站端口 (S-E): 唯一"可改盘删除"出口 —— 把文件/目录**移入回收站 (可恢复)**。
/// 红线: 实现绝不永久删除, 仅把内容送入回收站 (可恢复; Windows 实现在 Infrastructure)。
/// 只能经安全闸门放行后, 由 ActionExecutor 先写审计 (SR-9) 再调用; 上层须两步确认 (C8)。
/// </summary>
public interface IRecycleBin
{
    /// <summary>把目标移入回收站 (可恢复)。目标不存在/被占用/出错时抛出, 绝不静默永久删除。</summary>
    void Send(string path);
}
