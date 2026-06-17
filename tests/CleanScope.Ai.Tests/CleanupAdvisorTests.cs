using CleanScope.Ai.Advice;
using CleanScope.Ai.Chat;
using CleanScope.Domain.Enums;
using CleanScope.Domain.Models;

namespace CleanScope.Ai.Tests;

// S-H: 整盘清理参谋 —— 脱敏聚合 → 跨项建议; 降级安全; 输入无路径; 系统提示禁删除命令。
public sealed class CleanupAdvisorTests
{
    private static CleanupSummary Summary() => new(
        TotalSize: 10_000_000_000,
        ReclaimableSize: 3_000_000_000,
        Software: new[]
        {
            new SoftwareUsage("Miniconda (Python)", 5, 4_000_000_000, 2_000_000_000, RiskLevel.B),
            new SoftwareUsage("Anaconda (Python)", 3, 3_000_000_000, 1_000_000_000, RiskLevel.B),
        },
        Categories: new[]
        {
            new CleanupCategory("可重建缓存(按目录名推断)", 4, 1_500_000_000, RiskLevel.B, "用官方方式清理"),
        });

    [Fact]
    public async Task Returns_advice_text_when_enabled()
    {
        var chat = new FakeChat("- 你装了 Miniconda 和 Anaconda 两套 Python，可考虑保留其一。");
        var advice = await new CleanupAdvisor(chat).AdviseAsync(Summary());

        Assert.False(string.IsNullOrWhiteSpace(advice));
        Assert.Contains("Anaconda", advice);
        Assert.Equal(1, chat.Calls);
    }

    [Fact] // 脱敏: 喂给 AI 的用户提示只含软件/类别/容量, 不含任何路径或盘符。
    public async Task User_prompt_contains_no_paths()
    {
        var chat = new FakeChat("ok");
        await new CleanupAdvisor(chat).AdviseAsync(Summary());

        Assert.DoesNotContain(@":\", chat.LastUserPrompt);     // 无盘符路径
        Assert.DoesNotContain(@"\Users\", chat.LastUserPrompt);
        Assert.Contains("Miniconda (Python)", chat.LastUserPrompt);   // 仅软件名等聚合信息
    }

    [Fact] // 系统提示必须明令"不要输出删除命令"(IR-6: 输出只展示, 不执行)。
    public async Task System_prompt_forbids_delete_commands()
    {
        var chat = new FakeChat("ok");
        await new CleanupAdvisor(chat).AdviseAsync(Summary());
        Assert.Contains("不要输出", chat.LastSystemPrompt);
    }

    [Fact] // 未启用 → null, 不调用 AI (降级, 核心不依赖)
    public async Task Disabled_returns_null_without_calling()
    {
        var chat = new FakeChat("x") { Enabled = false };
        Assert.Null(await new CleanupAdvisor(chat).AdviseAsync(Summary()));
        Assert.Equal(0, chat.Calls);
    }

    [Fact] // 调用抛异常 → null (降级), 不冒泡
    public async Task Failure_degrades_to_null()
    {
        var advice = await new CleanupAdvisor(new ThrowingChat()).AdviseAsync(Summary());
        Assert.Null(advice);
    }

    private sealed class FakeChat : IAiChat
    {
        private readonly string _reply;
        public FakeChat(string reply) => _reply = reply;
        public bool Enabled { get; set; } = true;
        public int Calls { get; private set; }
        public string LastSystemPrompt { get; private set; } = "";
        public string LastUserPrompt { get; private set; } = "";
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default)
        {
            Calls++;
            LastSystemPrompt = s;
            LastUserPrompt = u;
            return Task.FromResult(_reply);
        }
    }

    private sealed class ThrowingChat : IAiChat
    {
        public bool Enabled => true;
        public Task<string> CompleteAsync(string s, string u, CancellationToken ct = default)
            => throw new HttpRequestException("boom");
    }
}
