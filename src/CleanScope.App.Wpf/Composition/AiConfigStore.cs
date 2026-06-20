using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanScope.Ai.Chat;
using CleanScope.Domain.Enums;

namespace CleanScope.App.Wpf.Composition;

/// <summary>
/// AI 配置的本地读写 (D: 改由桌面应用配置, 不再让用户手改 JSON)。
///
/// 安全: Key 用 **DPAPI (按当前 Windows 用户)** 加密后落本地 %LocalAppData%\CleanScope, 绝不入库、绝不出现在日志/报告;
/// 文件仍在 gitignore 之外的用户目录。兼容旧的明文 appsettings.ai.local.json 与环境变量覆盖。
/// </summary>
public static class AiConfigStore
{
    /// <summary>应用写入的配置文件 (用户目录, 避免 Program Files 写权限问题)。</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CleanScope", "appsettings.ai.local.json");

    private sealed class Dto
    {
        public string? baseUrl { get; set; }
        public string? apiKey { get; set; }            // 旧格式 / 示例: 明文
        public string? apiKeyProtected { get; set; }   // 新格式: DPAPI 加密后的 base64
        public string? model { get; set; }
        public bool cloudEnabled { get; set; }
        public string? sanitization { get; set; }   // 出云脱敏档位 (Strict/Balanced/Off)
    }

    /// <summary>读取配置: 先用户目录、再回退仓库/输出目录旁的旧文件; 最后套环境变量覆盖 (经 AiOptions)。</summary>
    public static AiOptions Load(string? extraSearchFile = null)
    {
        var dto = ReadFile(DefaultPath) ?? (extraSearchFile is not null ? ReadFile(extraSearchFile) : null);

        string baseUrl = dto?.baseUrl ?? "";
        string apiKey = DecryptKey(dto);
        string model = string.IsNullOrWhiteSpace(dto?.model) ? "deepseek-chat" : dto!.model!;
        bool cloud = dto?.cloudEnabled ?? false;
        // 问题#4: 已保存过则用用户的选择; 从未配置过的新用户默认"均衡"(识别力更好, 仍隐去用户名)。
        var sanitization = Enum.TryParse<SanitizationLevel>(dto?.sanitization, ignoreCase: true, out var lvl)
            ? lvl : SanitizationLevel.Balanced;

        // 环境变量优先级最高 (与既有约定一致)。
        baseUrl = Env("CLEANSCOPE_AI_BASEURL") ?? baseUrl;
        apiKey = Env("CLEANSCOPE_AI_KEY") ?? apiKey;
        model = Env("CLEANSCOPE_AI_MODEL") ?? model;
        if (Env("CLEANSCOPE_AI_CLOUD") is { } e) cloud = e is "1" or "true" or "True";

        return new AiOptions(baseUrl, apiKey, model, cloud, sanitization);
    }

    /// <summary>保存配置到用户目录; Key 经 DPAPI 加密。</summary>
    public static void Save(AiOptions options)
    {
        var dir = Path.GetDirectoryName(DefaultPath)!;
        Directory.CreateDirectory(dir);
        var dto = new Dto
        {
            baseUrl = options.BaseUrl,
            apiKeyProtected = EncryptKey(options.ApiKey),
            model = options.Model,
            cloudEnabled = options.CloudEnabled,
            sanitization = options.Sanitization.ToString(),
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(DefaultPath, json);
    }

    private static Dto? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Dto>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private static string DecryptKey(Dto? dto)
    {
        if (dto is null) return "";
        if (!string.IsNullOrWhiteSpace(dto.apiKeyProtected))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(dto.apiKeyProtected!), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }   // 换机/损坏 → 视为未配置, 请用户重填
        }
        return dto.apiKey ?? "";   // 兼容旧明文
    }

    private static string? EncryptKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
