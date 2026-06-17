using System.Text.Json;

namespace CleanScope.Infrastructure.Rules;

/// <summary>
/// 声明式规则包加载器 (实现 <see cref="IRuleSource"/>): 读取 <c>rules/*.json</c>, 展开环境变量,
/// 校验, 按 priority 降序 (就高) 返回 <see cref="RuleDefinition"/> 列表。
///
/// 安全 (规则是数据非代码, 架构§7):
///  - 仅用 <see cref="JsonSerializer"/> 反序列化到受限 DTO; 无多态/类型信息, 无法注入代码。
///  - 任一文件非法 → 抛 <see cref="RulePackException"/> 标明文件 (报错不崩); 绝不静默吞掉,
///    以免悄悄丢失系统关键黑名单 (T1.6 🔴)。
///  - 校验不变量: <c>is_system_critical=true</c> 必须 risk=D 且 direct_delete=false, 否则拒绝加载
///    (防规则被错误放宽, 知识库§0)。
/// </summary>
public sealed class RulePackLoader : IRuleSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _rulesDir;
    private readonly Func<string, string> _expandVars;

    /// <param name="rulesDirectory">规则包目录 (含 *.json)。</param>
    /// <param name="expandVars">环境变量展开器; 默认 <see cref="Environment.ExpandEnvironmentVariables"/> (注入便于跨平台测试)。</param>
    public RulePackLoader(string rulesDirectory, Func<string, string>? expandVars = null)
    {
        _rulesDir = rulesDirectory;
        _expandVars = expandVars ?? Environment.ExpandEnvironmentVariables;
    }

    /// <summary>默认规则目录: 可执行文件旁的 <c>rules/</c>。</summary>
    public static string DefaultRulesDirectory => Path.Combine(AppContext.BaseDirectory, "rules");

    public async Task<IReadOnlyList<RuleDefinition>> LoadAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_rulesDir))
            throw new RulePackException($"规则目录不存在: {_rulesDir}");

        var all = new List<RuleDefinition>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 文件名排序 (00- / 10- / 20- ...) 保证确定性; 最终再按 priority 降序。
        foreach (var file in Directory.EnumerateFiles(_rulesDir, "*.json").OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);

            RuleDto[]? dtos;
            try
            {
                await using var fs = File.OpenRead(file);
                dtos = await JsonSerializer.DeserializeAsync<RuleDto[]>(fs, JsonOptions, ct);
            }
            catch (JsonException ex)
            {
                throw new RulePackException($"JSON 解析失败: {ex.Message}", name, ex);
            }

            if (dtos is null)
                throw new RulePackException("规则文件为空或为 null", name);

            foreach (var dto in dtos)
            {
                var rule = MapAndValidate(dto, name);
                if (!seenIds.Add(rule.Id))
                    throw new RulePackException($"规则 id 重复: {rule.Id}", name);
                all.Add(rule);
            }
        }

        // 冲突就高: priority 降序; 稳定次序按 id 兜底。
        return all
            .OrderByDescending(static r => r.Priority)
            .ThenBy(static r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private RuleDefinition MapAndValidate(RuleDto d, string file)
    {
        string Req(string? v, string field) =>
            string.IsNullOrWhiteSpace(v) ? throw new RulePackException($"缺少必填字段: {field}", file) : v;

        var id = Req(d.Id, "id");
        var pattern = Req(d.Pattern, "pattern");
        var matchKind = ParseMatchKind(d.MatchType, file, id);
        var risk = ParseRisk(d.RiskLevel, file, id);

        // 不变量: 系统关键规则不可被放宽 (知识库§0)。
        if (d.IsSystemCritical && (risk != RiskLevel.D || d.DirectDelete))
            throw new RulePackException(
                $"系统关键规则被放宽 (id={id}): is_system_critical=true 必须 risk=D 且 direct_delete=false", file);

        return new RuleDefinition(
            Id: id,
            Pattern: _expandVars(pattern),       // 展开 %SystemRoot% 等; 不假定 C 盘
            MatchKind: matchKind,
            Category: d.Category ?? string.Empty,
            RiskLevel: risk,
            DirectDelete: d.DirectDelete,
            IsSystemCritical: d.IsSystemCritical,
            Description: d.Description ?? string.Empty,
            RecommendedAction: d.RecommendedAction ?? string.Empty,
            EvidenceType: d.EvidenceType ?? string.Empty,
            Confidence: d.Confidence,
            Priority: d.Priority,
            Command: string.IsNullOrWhiteSpace(d.Command) ? null : d.Command);
    }

    private static RuleMatchKind ParseMatchKind(string? raw, string file, string id) => raw switch
    {
        "path_prefix" => RuleMatchKind.PathPrefix,
        "path_glob" => RuleMatchKind.PathGlob,
        "dir_name" => RuleMatchKind.DirName,
        "extension" => RuleMatchKind.Extension,
        _ => throw new RulePackException($"未知 match_type='{raw}' (id={id})", file),
    };

    private static RiskLevel ParseRisk(string? raw, string file, string id) =>
        Enum.TryParse<RiskLevel>(raw, ignoreCase: true, out var lvl) && Enum.IsDefined(lvl)
            ? lvl
            : throw new RulePackException($"未知 risk_level='{raw}' (id={id})", file);

    // 受限 DTO: 字段与 rules/*.json 一一对应 (snake_case 经命名策略映射)。
    private sealed class RuleDto
    {
        public string? Id { get; set; }
        public string? Pattern { get; set; }
        public string? MatchType { get; set; }       // path_prefix | path_glob | dir_name | extension
        public string? Category { get; set; }
        public string? RiskLevel { get; set; }       // A|B|C|D|E
        public bool DirectDelete { get; set; }
        public bool IsSystemCritical { get; set; }
        public string? Description { get; set; }
        public string? RecommendedAction { get; set; }
        public string? EvidenceType { get; set; }
        public double Confidence { get; set; }
        public int Priority { get; set; }
        public string? Command { get; set; }          // S-D: 官方清理命令 (可选)
    }
}
