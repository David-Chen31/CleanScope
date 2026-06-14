using System.Reflection;
using NetArchTest.Rules;

namespace CleanScope.Architecture.Tests;

// T0.6 架构守卫: 把技术决议 9/10 + 决议 4/8 变成机器断言, 纳入 CI 硬门禁。
// 一旦有人误加跨界引用 (例如 Ai -> Safety), 这些测试立即变红。
// 注: 现阶段实现类尚少, 空程序集会"真空通过"; 随代码增长自动生效。
public class DependencyRulesTests
{
    private static readonly string[] WpfAssemblies =
        { "PresentationFramework", "PresentationCore", "WindowsBase", "CleanScope.App.Wpf" };

    private static Assembly Load(string name) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, name + ".dll"));

    private static readonly Assembly Domain = Load("CleanScope.Domain");
    private static readonly Assembly Core = Load("CleanScope.Core");
    private static readonly Assembly Safety = Load("CleanScope.Safety");
    private static readonly Assembly Ai = Load("CleanScope.Ai");

    private static string Fail(TestResult r) =>
        "违规类型: " + string.Join(", ", r.FailingTypeNames ?? Array.Empty<string>());

    [Fact] // 决议10: AI 旁路不得进入裁决/改盘路径
    public void Ai_must_not_depend_on_Safety()
    {
        var result = Types.InAssembly(Ai)
            .Should().NotHaveDependencyOn("CleanScope.Safety")
            .GetResult();
        Assert.True(result.IsSuccessful,
            "决议10: CleanScope.Ai 不得依赖 CleanScope.Safety (AI 在结构上发不起删除)。" + Fail(result));
    }

    [Fact] // 决议9: 核心引擎不得依赖 WPF
    public void Domain_must_not_depend_on_WPF()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOnAny(WpfAssemblies)
            .GetResult();
        Assert.True(result.IsSuccessful, "决议9: Domain 不得依赖 WPF。" + Fail(result));
    }

    [Fact] // 决议9
    public void Core_must_not_depend_on_WPF()
    {
        var result = Types.InAssembly(Core)
            .Should().NotHaveDependencyOnAny(WpfAssemblies)
            .GetResult();
        Assert.True(result.IsSuccessful, "决议9: Core 不得依赖 WPF。" + Fail(result));
    }

    [Fact] // Domain 是最内核, 零依赖其他 CleanScope 项目
    public void Domain_must_be_innermost()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOnAny(
                "CleanScope.Core", "CleanScope.Safety", "CleanScope.Ai",
                "CleanScope.Infrastructure", "CleanScope.Reporting", "CleanScope.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, "Domain 应零依赖其他 CleanScope 项目。" + Fail(result));
    }

    [Fact] // 决议4/8: SQLite 只允许出现在 Infrastructure
    public void Sqlite_must_only_be_in_Infrastructure()
    {
        foreach (var asm in new[] { Domain, Core, Safety, Ai })
        {
            var result = Types.InAssembly(asm)
                .Should().NotHaveDependencyOn("Microsoft.Data.Sqlite")
                .GetResult();
            Assert.True(result.IsSuccessful,
                $"决议4/8: {asm.GetName().Name} 不得直接依赖 Microsoft.Data.Sqlite (只 Infrastructure 可)。" + Fail(result));
        }
    }

    [Fact] // 守卫自检: 程序集确实被加载 (否则上面规则会真空通过而无意义)
    public void Domain_assembly_is_loaded_with_types()
    {
        Assert.NotEmpty(Domain.GetTypes());
    }
}
