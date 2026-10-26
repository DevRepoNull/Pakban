using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pakban.Services;

/// <summary>System RAM statistics and a gentle working-set trim optimizer.</summary>
public static class RamService
{
    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public sealed record RamStats(double TotalGB, double UsedGB, double AvailableGB, double CachedGB, int LoadPercent);

    public static RamStats GetStats()
    {
        var status = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(status);
        double total = status.ullTotalPhys;
        double avail = status.ullAvailPhys;
        double used = total - avail;
        double cached = GetSystemCacheBytes();
        return new RamStats(
            total / (1L << 30),
            used / (1L << 30),
            avail / (1L << 30),
            cached / (1L << 30),
            (int)Math.Round(100.0 * used / Math.Max(total, 1)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nint CommitTotal;
        public nint CommitLimit;
        public nint CommitPeak;
        public nint PhysicalTotal;
        public nint PhysicalAvailable;
        public nint SystemCache;
        public nint KernelTotal;
        public nint KernelPaged;
        public nint KernelNonpaged;
        public nint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

    private static double GetSystemCacheBytes()
    {
        try
        {
            if (GetPerformanceInfo(out var info, (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>()) && info.PageSize > 0)
                return (double)info.SystemCache * info.PageSize;
        }
        catch { }
        return 0;
    }

    public sealed record OptimizeResult(double FreedGB, int ProcessesTouched);

    /// <summary>
    /// Asks every accessible process to release its unused memory (working-set trim)
    /// plus a full GC of Pakban itself. Returns the measured change in available RAM.
    /// </summary>
    public static OptimizeResult Optimize()
    {
        var before = GetStats();
        int touched = 0;

        foreach (var process in Process.GetProcesses())
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, (uint)process.Id);
                if (handle != IntPtr.Zero && EmptyWorkingSet(handle))
                    touched++;
            }
            catch { }
            finally
            {
                process.Dispose();
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        Thread.Sleep(900); // let the kernel settle so the measurement is honest
        var after = GetStats();
        double freed = after.AvailableGB - before.AvailableGB;
        return new OptimizeResult(Math.Max(0, freed), touched);
    }

    public sealed record ProcessMem(string Name, long WorkingSet);

    public static List<ProcessMem> GetTopProcesses(int count = 12)
    {
        var list = new List<ProcessMem>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.WorkingSet64 > 0)
                    list.Add(new ProcessMem(p.ProcessName, p.WorkingSet64));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return list.OrderByDescending(x => x.WorkingSet).Take(count).ToList();
    }
}
