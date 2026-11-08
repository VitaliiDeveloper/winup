using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Winup.Core;

/// <summary>Tells which apps are open and using a candidate's files. Tests use a fake one.</summary>
public interface IAppProbe
{
    IReadOnlyList<string> OpenApps(Candidate candidate);
}

/// <summary>Running process names from the rule, plus (for apps found by layout) Windows' Restart Manager.</summary>
public sealed class WindowsAppProbe : IAppProbe
{
    public IReadOnlyList<string> OpenApps(Candidate c)
    {
        var open = new List<string>();
        foreach (var name in c.Rule.Processes)
        {
            Process[] ps;
            try { ps = Process.GetProcessesByName(name); }
            catch { continue; }
            if (ps.Length > 0)
            {
                var friendly = FriendlyName(ps[0]) ?? name;
                if (!open.Contains(friendly, StringComparer.OrdinalIgnoreCase)) open.Add(friendly);
            }
            foreach (var p in ps) p.Dispose();
        }
        if (c.Rule.ProbeOpenFiles)
        {
            var sample = c.Targets.SelectMany(t => FileSystem.SampleFiles(t, 16)).Take(64).ToArray();
            foreach (var app in RestartManager.WhoHolds(sample))
                if (!open.Contains(app, StringComparer.OrdinalIgnoreCase)) open.Add(app);
        }
        return open;
    }

    /// <summary>"Steam" rather than "steamwebhelper": the product name from the exe, when Windows lets us read it.</summary>
    private static string? FriendlyName(Process p)
    {
        try
        {
            var name = p.MainModule?.FileVersionInfo.ProductName?.Trim();
            return string.IsNullOrEmpty(name) || name.Length > 40 ? null : name;
        }
        catch { return null; }
    }
}

/// <summary>Restart Manager: the Windows API installers use to find programs holding files open.</summary>
public static class RestartManager
{
    public static IReadOnlyList<string> WhoHolds(string[] files)
    {
        var result = new List<string>();
        if (files.Length == 0) return result;
        var key = new StringBuilder(64);
        if (RmStartSession(out uint h, 0, key) != 0) return result;
        try
        {
            if (RmRegisterResources(h, (uint)files.Length, files, 0, IntPtr.Zero, 0, null) != 0) return result;
            uint needed = 0, count = 0, reasons = 0;
            int r = RmGetList(h, out needed, ref count, null, ref reasons);
            if (r == 234 /* ERROR_MORE_DATA */ && needed > 0)
            {
                var info = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(h, out needed, ref count, info, ref reasons) == 0)
                {
                    int self = Environment.ProcessId;
                    for (int i = 0; i < count; i++)
                    {
                        if (info[i].Process.dwProcessId == self) continue;
                        var name = string.IsNullOrWhiteSpace(info[i].strAppName) ? $"PID {info[i].Process.dwProcessId}" : info[i].strAppName;
                        if (!result.Contains(name, StringComparer.OrdinalIgnoreCase)) result.Add(name);
                    }
                }
            }
        }
        catch { }
        finally { RmEndSession(h); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);
}
