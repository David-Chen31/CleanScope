namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// T3 代表性二进制选择 (纯函数, 便于单测): 在目录内若干候选可执行文件中, 选最能代表"这是什么软件"的那个。
/// 评分: 主程序名与目录名一致 ＞ 名字互为前缀/包含 ＞ exe 优先于 dll ＞ 越浅越好 ＞ 越大越好。
/// 不碰文件系统 —— 枚举由 <see cref="WindowsAccess"/> 完成, 此处只给定候选列表挑最佳。
/// </summary>
public static class RepresentativeBinary
{
    /// <param name="FileName">仅文件名 (含扩展名), 如 "steam.exe"。</param>
    /// <param name="Depth">相对采样目录的深度, 根层为 0。</param>
    /// <param name="Size">字节大小 (用作末位决胜)。</param>
    /// <param name="IsExe">true=.exe; false=.dll。</param>
    public readonly record struct Candidate(string FileName, int Depth, long Size, bool IsExe);

    /// <summary>给定目录叶子名与候选列表, 返回最佳候选下标; 列表为空返回 null。</summary>
    public static int? PickBest(string dirLeaf, IReadOnlyList<Candidate> candidates)
    {
        if (candidates is null || candidates.Count == 0) return null;

        var leaf = Normalize(dirLeaf);
        var best = -1;
        long bestScore = long.MinValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var s = Score(leaf, candidates[i]);
            if (s > bestScore) { bestScore = s; best = i; }
        }
        return best;
    }

    private static long Score(string normLeaf, Candidate c)
    {
        var stem = Normalize(StripExt(c.FileName));
        long score = 0;

        // 名字信号 (最强): 与目录名相等 ＞ 互为前缀/包含。
        if (stem.Length >= 2 && stem == normLeaf) score += 1_000_000;
        else if (stem.Length >= 4 && normLeaf.Length >= 4 &&
                 (normLeaf.StartsWith(stem, StringComparison.Ordinal) ||
                  stem.StartsWith(normLeaf, StringComparison.Ordinal))) score += 400_000;
        else if (stem.Length >= 5 && normLeaf.Contains(stem, StringComparison.Ordinal)) score += 150_000;

        if (c.IsExe) score += 50_000;                       // exe 比 dll 更能代表"应用"
        score -= (long)c.Depth * 10_000;                    // 越浅越好 (主程序通常在根层)
        if (IsNoiseName(stem)) score -= 500_000;            // unins000 / vcredist / setup 等不代表主体

        // 末位决胜: 体量 (压到 0..9999, 不喧宾夺主)。
        score += Math.Min(c.Size / 1_000_000, 9_999);
        return score;
    }

    // 安装器/卸载器/运行库等"非主体"可执行名, 尽量不选作代表 (它们的厂商常是打包商而非软件本身)。
    private static readonly string[] NoiseStems =
    {
        "unins", "uninstall", "setup", "install", "vcredist", "vc_redist", "dotnet",
        "crashpad", "crashhandler", "crashreport", "update", "updater", "helper", "notification",
    };

    private static bool IsNoiseName(string normStem) =>
        NoiseStems.Any(n => normStem.StartsWith(n, StringComparison.Ordinal));

    private static string StripExt(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty
        : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
