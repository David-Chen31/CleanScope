using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CleanScope.Infrastructure.Windows;

/// <summary>
/// Restart Manager (rstrtmgr.dll) 互操作封装: 判定某文件正被哪些进程占用 (IR-2 删除前置)。
/// 这是 Windows 上检测文件占用者的标准途径 (比句柄枚举更可靠)。全程只读, 不改盘。
///
/// 隐私: 仅返回进程**名** (进程完整路径才属 P1, 此处不取)。任何失败 → null, 绝不崩。
/// </summary>
internal static class RestartManager
{
    private const int RmRebootReasonNone = 0;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorMoreData = 234;

    public static string? GetOccupyingProcessName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var sessionKey = Guid.NewGuid().ToString("N");   // 32 hex 字符 (CCH_RM_SESSION_KEY)
        try
        {
            if (RmStartSession(out var session, 0, sessionKey) != 0) return null;
            try
            {
                string[] files = { path };
                if (RmRegisterResources(session, 1, files, 0, null, 0, null) != 0) return null;

                uint needed = 0, count = 0, reasons = 0;
                var rv = RmGetList(session, out needed, ref count, null, out reasons);
                if (needed == 0) return null;                          // 无占用
                if (rv != ErrorMoreData && rv != 0) return null;

                var infos = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(session, out needed, ref count, infos, out reasons) != 0) return null;

                for (var i = 0; i < count; i++)
                {
                    var name = ResolveName(infos[i]);
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                return null;
            }
            finally
            {
                RmEndSession(session);
            }
        }
        catch
        {
            return null;   // 互操作异常不崩
        }
    }

    // 优先按 PID 解析真实进程名 (更准); 回退 RM 提供的应用友好名。
    private static string? ResolveName(RM_PROCESS_INFO pi)
    {
        try
        {
            using var p = Process.GetProcessById(pi.Process.dwProcessId);
            if (!string.IsNullOrEmpty(p.ProcessName)) return p.ProcessName;
        }
        catch { /* 进程已退出 → 回退友好名 */ }
        return string.IsNullOrWhiteSpace(pi.strAppName) ? null : pi.strAppName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;
        public uint ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle, uint nFiles, string[]? rgsFilenames,
        uint nApplications, RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, out uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);
}
