namespace CleanScope.Core.Attribution;

/// <summary>
/// 目录名启发 (确定性, 非 AI): 仅凭"叶子目录名"就能说清用途/归属的常见目录, 用于补全那些
/// 既无应用归属、又不是系统/容器目录的"兜底未知"项, 消除"无法判断 / 未知来源"。
///
/// 三类: ① 已知用户外壳文件夹 (图片/Pictures、文档、下载…); ② 知名点目录 (.vscode、.git、.claude…);
/// ③ 通用点目录兜底 (.foo → "「foo」相关数据, 依据目录名推断")。
/// 推断结论一律带"(依据目录名推断)"语气, 与确凿事实区分 (安全§9)。
/// </summary>
public static class NameHeuristics
{
    /// <summary>来源短标签 + 用途完整描述。</summary>
    public readonly record struct Hint(string Origin, string Purpose);

    // ① 已知用户外壳文件夹 (中英文叶子名 → 用途)。来源标签即文件夹中文名。
    private static readonly Dictionary<string, Hint> ShellFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pictures"] = new("图片库", "图片库: 存放照片、截图等图片文件"),
        ["图片"] = new("图片库", "图片库: 存放照片、截图等图片文件"),
        ["documents"] = new("文档库", "文档库: 存放个人文档文件"),
        ["文档"] = new("文档库", "文档库: 存放个人文档文件"),
        ["downloads"] = new("下载", "下载目录: 浏览器与各程序下载的文件 (多可清理)"),
        ["下载"] = new("下载", "下载目录: 浏览器与各程序下载的文件 (多可清理)"),
        ["desktop"] = new("桌面", "桌面: 桌面上显示的文件与快捷方式"),
        ["桌面"] = new("桌面", "桌面: 桌面上显示的文件与快捷方式"),
        ["music"] = new("音乐库", "音乐库: 存放音频/音乐文件"),
        ["音乐"] = new("音乐库", "音乐库: 存放音频/音乐文件"),
        ["videos"] = new("视频库", "视频库: 存放视频文件"),
        ["视频"] = new("视频库", "视频库: 存放视频文件"),
        ["favorites"] = new("收藏夹", "收藏夹: 浏览器/资源管理器的收藏与书签"),
        ["收藏夹"] = new("收藏夹", "收藏夹: 浏览器/资源管理器的收藏与书签"),
        ["onedrive"] = new("OneDrive", "OneDrive: 微软云同步盘的本地副本"),
        ["saved games"] = new("游戏存档", "游戏存档: 部分游戏的存档目录"),
        ["contacts"] = new("联系人", "联系人: Windows 联系人数据"),
        ["links"] = new("链接", "链接: 资源管理器收藏夹中的快捷方式"),
        ["searches"] = new("搜索", "搜索: 已保存的搜索条件"),
    };

    // ② 知名点目录 (开发/工具链常见) → (来源, 用途)。
    private static readonly Dictionary<string, Hint> DotDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        [".vscode"] = new("VS Code", "VS Code 编辑器的用户/工作区配置与扩展数据"),
        [".git"] = new("Git", "Git 版本库的内部数据 (提交历史、对象库)"),
        [".idea"] = new("JetBrains IDE", "JetBrains 系列 IDE 的项目配置"),
        [".gradle"] = new("Gradle", "Gradle 构建工具的缓存与配置"),
        [".m2"] = new("Maven", "Maven 的本地依赖仓库与配置"),
        [".npm"] = new("npm", "npm 包管理器的缓存 (多可清理)"),
        [".nuget"] = new("NuGet", "NuGet 包管理器的本地包缓存"),
        [".cargo"] = new("Rust Cargo", "Rust Cargo 的依赖与工具缓存"),
        [".rustup"] = new("Rustup", "Rust 工具链管理器的安装数据"),
        [".android"] = new("Android SDK", "Android 开发工具的配置与虚拟设备数据"),
        [".docker"] = new("Docker", "Docker 客户端的配置"),
        [".kube"] = new("Kubernetes", "kubectl 的集群访问配置"),
        [".aws"] = new("AWS CLI", "AWS 命令行工具的凭据与配置"),
        [".ssh"] = new("SSH", "SSH 密钥与已知主机 (敏感, 勿删)"),
        [".config"] = new("应用配置", "跨平台应用的用户配置目录"),
        [".cache"] = new("缓存", "应用缓存目录 (通常可重建)"),
        [".claude"] = new("Claude", "Claude / Claude Code 的配置与本地数据"),
        [".cursor"] = new("Cursor", "Cursor 编辑器的配置与数据"),
        [".conda"] = new("Conda", "Conda 环境管理器的配置"),
        [".vs"] = new("Visual Studio", "Visual Studio 的解决方案级缓存 (可重建)"),
    };

    /// <summary>按叶子目录名给出来源/用途提示; 无法判断返回 null。</summary>
    public static Hint? Resolve(string? path)
    {
        var leaf = LeafName(path);
        if (string.IsNullOrEmpty(leaf)) return null;

        if (ShellFolders.TryGetValue(leaf, out var s)) return s;
        if (DotDirs.TryGetValue(leaf, out var d)) return d;

        // ③ 通用点目录兜底: ".foo" → 「foo」工具的配置/数据 (依据目录名推断)。
        if (leaf.Length > 1 && leaf[0] == '.' && leaf.IndexOf('.', 1) < 0)
        {
            var app = leaf[1..];
            return new Hint($"{app}（推断自目录名）", $"「{app}」相关的配置或数据 (依据目录名推断)");
        }
        return null;
    }

    private static string LeafName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var s = path.Replace('/', '\\').TrimEnd('\\');
        var i = s.LastIndexOf('\\');
        return i >= 0 && i + 1 < s.Length ? s[(i + 1)..] : s;
    }
}
