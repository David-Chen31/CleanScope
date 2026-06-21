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

    /// <summary>
    /// T3 (离线 ground-truth 归因): 采样目录内"代表性二进制"(主程序 exe/dll) 的元数据 ——
    /// 厂商把公司/产品/签名写进二进制, 与我们是否"收录过"无关, 故对任意机器、任意软件都成立。
    /// 有界搜索 (浅层 + 文件数封顶); 无可执行文件 → null。
    /// <paramref name="includeSignature"/>=false 时只读版本信息 (省去昂贵的 Authenticode, 供建树批量调用)。
    /// 默认实现返回 null (非 Windows 实现/测试替身无需采样)。
    /// </summary>
    Task<FileMetadata?> SampleDirectoryBinaryAsync(
        string dirPath, bool includeSignature, CancellationToken ct = default)
        => Task.FromResult<FileMetadata?>(null);
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

/// <summary>
/// 回收站还原端口 (H): 把之前移入回收站的某原始路径项**还原回原位**。纯还原, 不删除任何东西。
/// 受系统/区域影响可能失败 → 返回 false, 由上层回退"打开回收站手动还原"。Windows 实现在 Infrastructure。
/// </summary>
public interface IRecycleRestore
{
    /// <summary>尝试把原路径为 <paramref name="originalPath"/> 的回收站项还原回原位; 成功 true, 否则 false (不抛)。</summary>
    bool TryRestore(string originalPath);
}
