using System.Text.Json;

namespace CleanScope.Infrastructure.Attribution;

/// <summary>
/// 知名软件特征库加载器 (① 本地化): 读取 <c>signatures/*.json</c>, 合并为 <see cref="KnownSoftwareData"/>
/// (Core 的 KnownSoftwareCatalog 据此提供匹配)。
///
/// 与规则包不同, 特征库是**纯增强、非安全关键** —— 缺失/损坏绝不能影响主流程:
///  - 目录不存在 / 无文件 → 返回 <see cref="KnownSoftwareData.Empty"/> (静默降级)。
///  - 单个文件 JSON 损坏 → 跳过该文件, 继续合并其余 (不抛、不崩)。
/// 仅 <see cref="JsonSerializer"/> 反序列化到受限 DTO (数据非代码, 无法注入)。
/// </summary>
public sealed class KnownSoftwareLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _dir;

    public KnownSoftwareLoader(string signaturesDirectory) => _dir = signaturesDirectory;

    /// <summary>默认特征库目录: 可执行文件旁的 <c>signatures/</c>。</summary>
    public static string DefaultSignaturesDirectory => Path.Combine(AppContext.BaseDirectory, "signatures");

    public async Task<KnownSoftwareData> LoadAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_dir)) return KnownSoftwareData.Empty;

        var vendors = new List<VendorAlias>();
        var directories = new List<DirectoryAlias>();

        foreach (var file in Directory.EnumerateFiles(_dir, "*.json")
                     .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            PackDto? dto;
            try
            {
                await using var fs = File.OpenRead(file);
                dto = await JsonSerializer.DeserializeAsync<PackDto>(fs, JsonOptions, ct);
            }
            catch (JsonException)
            {
                continue;   // 损坏文件跳过, 不影响其余 (纯增强, 非安全关键)
            }

            if (dto is null) continue;
            foreach (var v in dto.Vendors ?? Enumerable.Empty<VendorDto>())
                if (!string.IsNullOrWhiteSpace(v.Contains) && !string.IsNullOrWhiteSpace(v.Name))
                    vendors.Add(new VendorAlias(v.Contains!.Trim(), v.Name!.Trim()));

            foreach (var d in dto.Directories ?? Enumerable.Empty<DirectoryDto>())
                if (!string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.App))
                    directories.Add(new DirectoryAlias(d.Name!.Trim(), d.App!.Trim(),
                        string.IsNullOrWhiteSpace(d.Purpose) ? null : d.Purpose!.Trim()));
        }

        return new KnownSoftwareData(vendors, directories);
    }

    private sealed class PackDto
    {
        public List<VendorDto>? Vendors { get; set; }
        public List<DirectoryDto>? Directories { get; set; }
    }

    private sealed class VendorDto
    {
        public string? Contains { get; set; }
        public string? Name { get; set; }
    }

    private sealed class DirectoryDto
    {
        public string? Name { get; set; }
        public string? App { get; set; }
        public string? Purpose { get; set; }
    }
}
