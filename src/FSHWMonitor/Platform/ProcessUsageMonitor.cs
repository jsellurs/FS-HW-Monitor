using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FSHWMonitor.Core;
using FSHWMonitor.Sensors;

namespace FSHWMonitor.Platform;

/// <summary>
/// Per-app CPU, memory, disk and GPU use, measured the way Task Manager does: one
/// NtQuerySystemInformation call returns every process's CPU time, private working set and I/O
/// byte counts, and the "GPU Engine" performance counters give GPU use per process.
/// Disk is all file I/O a process does (reads + writes), which is close to, but not exactly, physical disk traffic.
/// </summary>
public sealed partial class ProcessUsageMonitor : IProcessUsageSource
{
    readonly GpuEngineCounters? _gpu = GpuEngineCounters.TryCreate();
    readonly Dictionary<(long Pid, long Created), (long CpuTime, long IoBytes)> _previous = [];
    readonly Dictionary<(long Pid, long Created), string?> _paths = [];
    readonly Dictionary<string, string> _descriptions = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    long _previousStamp;
    bool _disposed;

    public UsageSnapshot Sample()
    {
        lock (_gate)
            return _disposed ? UsageSnapshot.Empty : SampleCore();
    }

    UsageSnapshot SampleCore()
    {
        var processes = ReadProcesses();
        long now = Stopwatch.GetTimestamp();
        double seconds = _previousStamp == 0 ? 0 : (double)(now - _previousStamp) / Stopwatch.Frequency;
        _previousStamp = now;
        var gpuByPid = _gpu?.SampleByPid() ?? [];

        var cpu = new List<(Proc P, double Value)>();
        var disk = new List<(Proc P, double Value)>();
        var current = new Dictionary<(long, long), (long, long)>(processes.Count);
        foreach (var p in processes)
        {
            var key = (p.Pid, p.Created);
            current[key] = (p.CpuTime, p.IoBytes);
            if (seconds > 0.2 && _previous.TryGetValue(key, out var before))
            {
                // CPU time is in 100 ns units; percent of the whole CPU, like Task Manager.
                double cpuPercent = (p.CpuTime - before.CpuTime) / (seconds * 1e7 * Environment.ProcessorCount) * 100;
                cpu.Add((p, Math.Max(0, cpuPercent)));
                disk.Add((p, Math.Max(0, (p.IoBytes - before.IoBytes) / seconds)));
            }
        }
        _previous.Clear();
        foreach (var (key, value) in current)
            _previous[key] = value;
        foreach (var stale in _paths.Keys.Where(k => !current.ContainsKey(k)).ToList())
            _paths.Remove(stale);

        var gpu = processes
            .Where(p => gpuByPid.ContainsKey(p.Pid))
            .Select(p => (p, gpuByPid[p.Pid]))
            .ToList();

        return new UsageSnapshot
        {
            Cpu = Top(cpu, UsageFormat.Percent, minimum: 0.1, cap: 100),
            Gpu = Top(gpu, UsageFormat.Percent, minimum: 0.5, cap: 100),
            Ram = Top(processes.Select(p => (p, (double)p.PrivateBytes)), UsageFormat.Bytes, minimum: 1e6),
            Disk = Top(disk, UsageFormat.Rate, minimum: 1024),
        };
    }

    /// <summary>Groups processes by executable, adds them up, and keeps the five biggest.</summary>
    List<ProcessUsage> Top(IEnumerable<(Proc P, double Value)> items, Func<double, string> format, double minimum, double cap = double.MaxValue)
    {
        return items
            .Where(i => i.P.Pid != 0) // the Idle "process"
            .GroupBy(i => i.P.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Group: g, Total: Math.Min(cap, g.Sum(i => i.Value))))
            .Where(g => g.Total >= minimum)
            .OrderByDescending(g => g.Total)
            .Take(5)
            .Select(g =>
            {
                var first = g.Group.OrderByDescending(i => i.Value).First().P;
                string? path = PathOf(first);
                return new ProcessUsage(DisplayName(first.Name, path), path, g.Total, format(g.Total), g.Group.Count());
            })
            .ToList();
    }

    string? PathOf(Proc p)
    {
        var key = (p.Pid, p.Created);
        if (!_paths.TryGetValue(key, out string? path))
            _paths[key] = path = QueryPath(p.Pid);
        return path;
    }

    /// <summary>Turns "chrome.exe" into "Google Chrome" using the exe's version info, or just drops the ".exe".</summary>
    string DisplayName(string imageName, string? path)
    {
        string fallback = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName[..^4] : imageName;
        if (path is null)
            return fallback;
        if (!_descriptions.TryGetValue(path, out string? description))
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            }
            catch
            {
                description = null;
            }
            _descriptions[path] = description = string.IsNullOrWhiteSpace(description) ? fallback : description;
        }
        return description;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _gpu?.Dispose();
        }
    }

    // Process list (NtQuerySystemInformation)

    readonly record struct Proc(long Pid, long Created, string Name, long CpuTime, long PrivateBytes, long IoBytes);

    static List<Proc> ReadProcesses()
    {
        int size = 1 << 20;
        while (true)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out int needed);
                if (status == StatusInfoLengthMismatch)
                {
                    size = Math.Max(size * 2, needed + (1 << 16));
                    continue;
                }
                if (status != 0)
                    return [];

                // SYSTEM_PROCESS_INFORMATION, 64-bit layout.
                var list = new List<Proc>(512);
                int offset = 0;
                while (true)
                {
                    IntPtr entry = buffer + offset;
                    int next = Marshal.ReadInt32(entry, 0);
                    long privateWorkingSet = Marshal.ReadInt64(entry, 8);
                    long created = Marshal.ReadInt64(entry, 32);
                    long userTime = Marshal.ReadInt64(entry, 40);
                    long kernelTime = Marshal.ReadInt64(entry, 48);
                    int nameBytes = (ushort)Marshal.ReadInt16(entry, 56);
                    IntPtr nameBuffer = Marshal.ReadIntPtr(entry, 64);
                    long pid = Marshal.ReadIntPtr(entry, 80).ToInt64();
                    long readBytes = Marshal.ReadInt64(entry, 232);
                    long writeBytes = Marshal.ReadInt64(entry, 240);

                    string name = nameBuffer != IntPtr.Zero && nameBytes > 0
                        ? Marshal.PtrToStringUni(nameBuffer, nameBytes / 2)
                        : pid == 0 ? "Idle" : "System";

                    list.Add(new Proc(pid, created, name, userTime + kernelTime, privateWorkingSet, readBytes + writeBytes));

                    if (next == 0)
                        break;
                    offset += next;
                }
                return list;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    static string? QueryPath(long pid)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, (int)pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var path = new StringBuilder(1024);
            int length = path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString(0, length) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    const int SystemProcessInformation = 5;
    const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("ntdll.dll")]
    static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    // GPU (performance counters)

    /// <summary>"\GPU Engine(*)\Utilization Percentage", one instance per process per GPU engine.</summary>
    sealed partial class GpuEngineCounters : IDisposable
    {
        IntPtr _query;
        readonly IntPtr _counter;

        GpuEngineCounters(IntPtr query, IntPtr counter)
        {
            _query = query;
            _counter = counter;
        }

        public static GpuEngineCounters? TryCreate()
        {
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out IntPtr query) != 0)
                    return null;
                if (PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out IntPtr counter) != 0)
                {
                    PdhCloseQuery(query);
                    return null;
                }
                PdhCollectQueryData(query); // rates need a first sample
                return new GpuEngineCounters(query, counter);
            }
            catch (Exception ex)
            {
                Log.Write("GPU usage counters unavailable: " + ex.Message);
                return null;
            }
        }

        /// <summary>Busiest engine per process, in percent (Task Manager's GPU column).</summary>
        public Dictionary<long, double> SampleByPid()
        {
            var result = new Dictionary<long, double>();
            if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
                return result;

            uint size = 0;
            if (PdhGetFormattedCounterArray(_counter, FormatDouble | FormatNoCap100, ref size, out _, IntPtr.Zero) != PdhMoreData || size == 0)
                return result;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, FormatDouble | FormatNoCap100, ref size, out uint count, buffer) != 0)
                    return result;

                // PDH_FMT_COUNTERVALUE_ITEM: name pointer, then { DWORD status; (padding); double value }.
                int itemSize = IntPtr.Size + 16;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * itemSize;
                    int status = Marshal.ReadInt32(item, IntPtr.Size);
                    if (status is not (0 or 1)) // PDH_CSTATUS_VALID_DATA / NEW_DATA
                        continue;

                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, IntPtr.Size + 8));
                    string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    var match = name is null ? null : PidPattern().Match(name);
                    if (match is not { Success: true } || !long.TryParse(match.Groups[1].Value, out long pid))
                        continue;

                    result[pid] = Math.Max(result.GetValueOrDefault(pid), value);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
        }

        [GeneratedRegex(@"^pid_(\d+)_")]
        private static partial Regex PidPattern();

        const uint FormatDouble = 0x00000200;
        const uint FormatNoCap100 = 0x00008000;
        const uint PdhMoreData = 0x800007D2;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
        static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr query);
    }
}
