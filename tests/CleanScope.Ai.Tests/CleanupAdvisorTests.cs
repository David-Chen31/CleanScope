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

    [Fact] // 非 JSON 回复 → 退化为纯文本计划 (Markdown 含原文, 无结构化步骤)。
    public async Task Returns_advice_text_when_enabled()
    {
        var chat = new FakeChat("- 你装了 Miniconda 和 Anaconda 两套 Python，可考虑保留其一。");
        var plan = await new CleanupAdvisor(chat).AdviseAsync(Summary());

        Assert.NotNull(plan);
        Assert.Contains("Anaconda", plan!.Markdown);
        Assert.Empty(plan.Steps);              // 非 JSON → 无结构化步骤, 回退纯文本
        Assert.Equal(1, chat.Calls);
    }

    [Fact] // JSON 回复 → 解析成结构化分步计划 (供卡片渲染) + 合成可读 Markdown (供报告)。
    public async Task Parses_structured_json_plan()
    {
        const string json = """
        {"summary":"预计可省约 1.4 GB","steps":[
          {"title":"清理可重建的编译缓存","detail":"删后不影响源码，下次构建会重建。","saving":"约 1.4 GB","difficulty":"简单","where":"可清理清单"}
        ],"note":"删除前再确认；只进回收站可还原。"}
        """;
        var plan = await new CleanupAdvisor(new FakeChat(json)).AdviseAsync(Summary());

        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);
        Assert.Equal("清理可重建的编译缓存", plan.Steps[0].Title);
        Assert.Equal("简单", plan.Steps[0].Difficulty);
        Assert.Equal("可清理清单", plan.Steps[0].Where);
        Assert.Contains("可省约 1.4 GB", plan.Summary);
        Assert.Contains("清理可重建的编译缓存", plan.Markdown);   // 合成的 markdown 含步骤
    }

    [Fact] // 问题#4: JSON 被截断 → 抢救出已完整的步骤, 不把半截 JSON 倒给用户。
    public async Task Salvages_complete_steps_from_truncated_json()
    {
        const string truncated = """
        {"summary":"预计可省约 5 GB","steps":[
          {"title":"去可清理清单一键回收","detail":"A/B 项删后进回收站可还原。","saving":"约 5 GB","difficulty":"简单","where":"可清理清单"},
          {"title":"关闭休眠","detail":"释放 hiber
        """;
        var plan = await new CleanupAdvisor(new FakeChat(truncated)).AdviseAsync(Summary());

        Assert.NotNull(plan);
        Assert.Single(plan!.Steps);                         // 仅抢救出完整的第一步
        Assert.Equal("去可清理清单一键回收", plan.Steps[0].Title);
        Assert.Contains("可省约 5 GB", plan.Summary);
        Assert.DoesNotContain("{", plan.Markdown);          // 绝不把原始 JSON 倒给用户
    }

    [Fact] // 问题#4: JSON 残缺到一步都抢救不出 → 给可重试提示, 而非原始花括号。
    public async Task Broken_json_shows_retry_hint_not_raw_braces()
    {
        var plan = await new CleanupAdvisor(new FakeChat("{\"summary\":\"x\",\"steps\":[ {\"title\":")).AdviseAsync(Summary());

        Assert.NotNull(plan);
        Assert.Empty(plan!.Steps);
        Assert.DoesNotContain("{", plan.Markdown);
        Assert.Contains("重试", plan.Markdown);
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

    [Fact] // 系统提示必须明令禁止输出删除命令 (IR-6: 输出只展示, 不执行)。
    public async Task System_prompt_forbids_delete_commands()
    {
        var chat = new FakeChat("ok");
        await new CleanupAdvisor(chat).AdviseAsync(Summary());
        Assert.Contains("严禁", chat.LastSystemPrompt);
        Assert.Contains("删除命令", chat.LastSystemPrompt);
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
