using System.Globalization;
using System.Text;
using CleanScope.Ai.Chat;
using CleanScope.Domain.Abstractions;
using CleanScope.Domain.Models;

namespace CleanScope.Ai.Advice;

/// <summary>
/// 整盘清理参谋 (S-H, 实现 <see cref="ICleanupAdvisor"/>)。把脱敏聚合 (软件/类别/容量) 拼成提示,
/// 请 AI 做**跨项**推理: 冗余工具链、重复缓存、优先清理顺序。
///
/// 安全:
///  - 输入是 <see cref="CleanupSummary"/> —— 只有软件名/类别名/容量/计数, **无路径、无文件内容** (PR-1)。
///  - 输出仅作展示文本, 调用方绝不解析/执行其中任何内容 (AS-6/IR-6); 系统提示明令"不要输出删除命令"。
///  - AI 不可用/失败 → 返回 null (降级), 核心功能不依赖在线 AI (决议5)。
/// </summary>
public sealed class CleanupAdvisor : ICleanupAdvisor
{
    private const string SystemPrompt =
        "你是 Windows 磁盘清理顾问。基于用户的占用汇总, 给出简洁的中文**跨项**清理建议: " +
        "识别重复/冗余 (如多套同类工具链、重复缓存)、指出优先清理顺序、提示哪些通常可安全清理。" +
        "要求: 只给要点 (markdown 无序列表, 至多 6 条); 不要逐文件复述; " +
        "**绝对不要输出任何删除命令、脚本或路径**; 末尾提醒以官方方式清理、删除前确认。";

    private readonly IAiChat _chat;

    public CleanupAdvisor(IAiChat chat) => _chat = chat;

    public bool Enabled => _chat.Enabled;

    public async Task<string?> AdviseAsync(CleanupSummary summary, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (!Enabled) return null;

        try
        {
            var reply = await _chat.CompleteAsync(SystemPrompt, BuildUserPrompt(summary), ct);
            return string.IsNullOrWhiteSpace(reply) ? null : reply.Trim();
        }
        catch
        {
            return null;   // 降级: 失败不影响核心流程
        }
    }

    private static string BuildUserPrompt(CleanupSummary s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"总占用 {Size(s.TotalSize)}, 其中估算可清理 {Size(s.ReclaimableSize)}。").AppendLine();

        if (s.Software.Count > 0)
        {
            sb.AppendLine("按软件占用 (软件 | 占用 | 其中可清理):");
            foreach (var u in s.Software.Take(15))
                sb.AppendLine($"- {u.Name} | {Size(u.TotalSize)} | {Size(u.CleanableSize)}");
            sb.AppendLine();
        }

        if (s.Categories.Count > 0)
        {
            sb.AppendLine("可清理类别 (类别 | 项数 | 可回收 | 官方清理方式):");
            foreach (var c in s.Categories.Take(15))
                sb.AppendLine($"- {c.Name} | {c.ItemCount} | {Size(c.ReclaimableSize)} | {c.RecommendedAction}");
        }
        return sb.ToString();
    }

    private static string Size(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        double s = bytes;
        int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return i == 0
            ? $"{bytes} {u[i]}"
            : string.Create(CultureInfo.InvariantCulture, $"{s:0.##} {u[i]}");
    }
}
