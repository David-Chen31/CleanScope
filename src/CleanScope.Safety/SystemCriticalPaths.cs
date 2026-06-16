namespace CleanScope.Safety;

/// <summary>
/// 系统关键路径黑名单 (安全§2) —— 闸门的**防御纵深**: 独立于规则引擎硬编码最危险的 Windows 目录,
/// 即使规则缺失/被绕过, 闸门仍据此拒绝删除 (IR-5: 黑名单不可被放宽)。
/// 前缀按路径段边界匹配; 含 %SystemRoot% 自身 → C:\Windows 下任何内容均视为系统关键。
/// </summary>
internal static class SystemCriticalPaths
{
    private static readonly string[] Prefixes = Build();

    public static bool IsBlacklisted(string path)
    {
        var p = Normalize(path);
        foreach (var pre in Prefixes)
            if (p.Equals(pre, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(pre + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string[] Build()
    {
        var sys = Env("SystemRoot", @"C:\Windows");
        var data = Env("ProgramData", @"C:\ProgramData");
        var raw = new[]
        {
            sys, sys + @"\System32", sys + @"\SysWOW64", sys + @"\WinSxS", sys + @"\Installer",
            sys + @"\System32\DriverStore", sys + @"\System32\drivers", sys + @"\System32\config",
            sys + @"\Boot", sys + @"\Fonts", data + @"\Package Cache",
            // 字面兜底 (保证在任意主机/CI 上一致): 即便环境变量缺失。
            @"C:\Windows", @"C:\ProgramData\Package Cache",
        };
        return raw.Select(Normalize)
                  .Where(s => s.Length > 0)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToArray();
    }

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    private static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\');
}
