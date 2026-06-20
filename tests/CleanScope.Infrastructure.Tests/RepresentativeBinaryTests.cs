using CleanScope.Infrastructure.Windows;

namespace CleanScope.Infrastructure.Tests;

// T3 代表性二进制选择 (纯函数): 名字一致 ＞ exe ＞ 越浅 ＞ 越大; 安装器/卸载器降权; 空→null。
public sealed class RepresentativeBinaryTests
{
    private static RepresentativeBinary.Candidate C(string name, int depth = 0, long size = 1000, bool exe = true)
        => new(name, depth, size, exe);

    [Fact]
    public void Empty_returns_null()
        => Assert.Null(RepresentativeBinary.PickBest("steam", Array.Empty<RepresentativeBinary.Candidate>()));

    [Fact] // 与目录同名的 exe 胜出 (即便不是最大)
    public void Name_matching_exe_wins()
    {
        var list = new[] { C("vcredist_x64.exe", size: 9_000_000), C("steam.exe", size: 1000), C("crashhandler.exe") };
        var best = RepresentativeBinary.PickBest("steam", list);
        Assert.Equal("steam.exe", list[best!.Value].FileName);
    }

    [Fact] // 无名字匹配时 exe 优先于 dll
    public void Exe_preferred_over_dll()
    {
        var list = new[] { C("libfoo.dll", exe: false, size: 9_000_000), C("launcher.exe", size: 1000) };
        var best = RepresentativeBinary.PickBest("myapp", list);
        Assert.Equal("launcher.exe", list[best!.Value].FileName);
    }

    [Fact] // 越浅越好 (主程序通常在根层)
    public void Shallower_preferred()
    {
        var list = new[] { C("tool.exe", depth: 2), C("tool.exe", depth: 0) };
        var best = RepresentativeBinary.PickBest("whatever", list);
        Assert.Equal(0, list[best!.Value].Depth);
    }

    [Fact] // 安装器/卸载器/运行库降权 — 即便它在根层、且体量大, 也不该代表软件主体
    public void Installer_and_uninstaller_are_deprioritized()
    {
        var list = new[] { C("unins000.exe", size: 9_000_000), C("MyEditor.exe", size: 500_000) };
        var best = RepresentativeBinary.PickBest("MyEditor", list);
        Assert.Equal("MyEditor.exe", list[best!.Value].FileName);
    }

    [Fact] // 全是噪声名时仍能返回一个 (兜底, 不返回 null)
    public void Falls_back_to_some_candidate_when_all_noise()
    {
        var list = new[] { C("setup.exe"), C("uninstall.exe") };
        Assert.NotNull(RepresentativeBinary.PickBest("app", list));
    }
}
