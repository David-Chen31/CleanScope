using System.IO;
using System.Net.Http;
using CleanScope.Ai.Chat;
using CleanScope.Ai.Explanation;
using CleanScope.Ai.Sanitization;
using CleanScope.Ai.Validation;
using CleanScope.Application;
using CleanScope.Core.Attribution;
using CleanScope.Core.Decisions;
using CleanScope.Core.Evidences;
using CleanScope.Core.Risk;
using CleanScope.Core.Rules;
using CleanScope.Core.Scanning;
using CleanScope.Domain.Abstractions;
using CleanScope.Infrastructure.Repositories;
using CleanScope.Infrastructure.Rules;
using CleanScope.Infrastructure.Storage;
using CleanScope.Infrastructure.Windows;
using CleanScope.Reporting;
using CleanScope.Safety;

namespace CleanScope.App.Wpf.Composition;

/// <summary>
/// 手写 DI 组合根 (决议: 不用 MS.Extensions.DI)。在此集中装配裁决链 + 仓储 + 安全闸门 + AI 旁路,
/// 经领域抽象交给 UI。与 Console 宿主同构 (Program.cs), 仅终端不同。
/// </summary>
public static class CompositionRoot
{
    public const string AppVersion = "0.1.0";

    public static async Task<AppServices> BuildAsync(CancellationToken ct = default)
    {
        // —— 规则包 (声明式数据) ——
        var rulesDir = ResolveRulesDir();
        var rules = await new RulePackLoader(rulesDir).LoadAsync(ct);

        // —— 真实系统访问 (只读): 元数据/签名/已安装/占用 ——
        var windows = new WindowsAccess();

        // —— 本地存储 (审计/忽略名单): %LocalAppData%\CleanScope\cleanscope.db, 仅本地 ——
        var provider = new SqliteConnectionProvider(CleanScopeDb.DefaultConnectionString());
        await new SqliteStorage(provider).InitializeAsync(ct);
        var ignore = new IgnoreRepository(provider);
        var audit = new AuditLogRepository(provider);

        // —— 安全闸门 + 执行器 (唯一可改盘路径; S-E: 桌面端开启删除能力) ——
        // deleteEnabled=true ⇒ 仅"可清理"桶 (A/B)、非黑名单/非容器/未占用项可放行, 且**仅移入回收站 (可恢复)**;
        // 系统关键/容器/C-E/占用一律拒。执行器先写审计 (SR-9) 再经回收站端口删除 (无永久删除路径)。
        var safety = new SafetyGuard(windows, deleteEnabled: true);
        var executor = new ActionExecutor(new ShellLauncher(), audit, ignore, AppVersion, new WindowsRecycleBin());

        // —— AI 旁路 (可选): 脱敏 → 解释 → 校验。未配置可用密钥 ⇒ 纯本地规则/风险 ——
        ISanitizationGateway? sanitizer = null;
        IExplanationService? explanation = null;
        IAiOutputValidator? validator = null;
        var aiEnabled = false;
        var aiOptions = AiOptions.Load(ResolveAiConfig());
        if (aiOptions.IsUsable)
        {
            sanitizer = new SanitizationGateway();
            explanation = new ExplanationService(new OpenAiChatClient(SharedHttp, aiOptions));
            validator = new AiOutputValidator();
            aiEnabled = true;
        }

        var useCase = new ScanAndAnalyzeUseCase(
            new ScanEngine(), new EvidenceCollector(windows), new RuleEngine(rules),
            new AttributionEngine(), new RiskEngine(), new DecisionService(), AppVersion,
            sanitizer, explanation, validator);

        return new AppServices
        {
            UseCase = useCase,
            ReportExporterFor = path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? new CsvReportExporter(sanitizePaths: false)
                : new MarkdownReportExporter(sanitizePaths: false),
            IgnoreRepository = ignore,
            ActionExecutor = executor,
            SafetyGuard = safety,
            AiEnabled = aiEnabled,
            Annotator = new AiAnnotator(sanitizer, explanation, validator),  // 详情页按需解释 (S6)
            AppVersion = AppVersion,
        };
    }

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    // 优先输出目录旁 rules/; 开发期回退到仓库根 (CleanScope.sln 旁)。
    private static string ResolveRulesDir()
    {
        var def = RulePackLoader.DefaultRulesDirectory;
        if (Directory.Exists(def)) return def;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "rules");
            if (File.Exists(Path.Combine(d.FullName, "CleanScope.sln")) && Directory.Exists(candidate))
                return candidate;
        }
        return def; // 交给 loader 抛清晰错误
    }

    // appsettings.ai.local.json (已 gitignore): 输出目录旁或仓库根。
    private static string? ResolveAiConfig()
    {
        const string name = "appsettings.ai.local.json";
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
