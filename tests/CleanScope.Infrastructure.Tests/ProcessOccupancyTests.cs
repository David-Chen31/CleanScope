using System.Diagnostics;
using CleanScope.Infrastructure.Windows;

namespace CleanScope.Infrastructure.Tests;

// T2.3: GetOccupyingProcessName —— Restart Manager 占用检测 (IR-2)。
public sealed class ProcessOccupancyTests
{
    private static readonly WindowsAccess Wa = new();

    [Fact]
    public void Detects_current_process_holding_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "cs_lock_" + Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllText(path, "x");
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var name = Wa.GetOccupyingProcessName(path);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.Equal(Process.GetCurrentProcess().ProcessName, name, ignoreCase: true);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Unlocked_file_reports_no_occupier()
    {
        var path = Path.Combine(Path.GetTempPath(), "cs_free_" + Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllText(path, "x");   // 句柄已关闭
        try
        {
            Assert.Null(Wa.GetOccupyingProcessName(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Nonexistent_file_does_not_throw()
        => Assert.Null(Wa.GetOccupyingProcessName(@"C:\no\such\__cleanscope_lock__.dat"));

    [Fact]
    public void Returns_only_process_name_not_full_path()
    {
        var path = Path.Combine(Path.GetTempPath(), "cs_lock2_" + Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllText(path, "x");
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var name = Wa.GetOccupyingProcessName(path);
                Assert.NotNull(name);
                Assert.DoesNotContain('\\', name!);   // P1: 不含路径分隔符
                Assert.DoesNotContain(":", name);
            }
        }
        finally { File.Delete(path); }
    }
}
