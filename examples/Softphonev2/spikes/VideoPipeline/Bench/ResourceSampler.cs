using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VideoSpike.Bench;

public sealed class ResourceStats
{
    public double Seconds { get; set; }
    public double CpuPercentOfOneCore { get; set; }
    public double CpuPercentOfMachine { get; set; }
    public int LogicalCpus { get; set; }
    public int Processes { get; set; }
    public double PrivateMB { get; set; }
    public Dictionary<string, double> CpuByProcess { get; set; } = new();
    /// <summary>"luid engtype" -> average utilisation % of the busiest engine of that type, our processes only.</summary>
    public Dictionary<string, double> GpuOurs { get; set; } = new();
    /// <summary>Same, every process on the machine (includes DWM compositing the window).</summary>
    public Dictionary<string, double> GpuSystem { get; set; } = new();
}

/// <summary>
/// Samples the near end's whole process tree once a second from outside it: the host process
/// plus every WebView2 browser, GPU, renderer and utility process it spawned. CPU is the sum of
/// their processor time; GPU comes from the "GPU Engine" counters (what Task Manager shows), split
/// by adapter and engine type, so hardware video decode and encode show up as their own engines.
/// </summary>
public sealed partial class ResourceSampler : IDisposable
{
    private readonly int _rootPid;
    private readonly Dictionary<int, (TimeSpan cpu, string name)> _lastCpu = new();
    private readonly Dictionary<string, double> _cpuMsByName = new();
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
    private readonly Dictionary<string, double> _gpuOursSum = new();
    private readonly Dictionary<string, double> _gpuSystemSum = new();
    private readonly PerformanceCounterCategory _gpuCategory = new("GPU Engine");
    private Thread? _thread;
    private volatile bool _stop;
    private int _samples;
    private double _cpuMs;
    private double _privateMbMax;
    private int _processes;
    private readonly Stopwatch _wall = new();

    public ResourceSampler(int rootPid) => _rootPid = rootPid;

    public void Start()
    {
        Sample(record: false);   // prime CPU baselines and counters
        _wall.Start();
        _thread = new Thread(() =>
        {
            var next = Stopwatch.GetTimestamp();
            while (!_stop)
            {
                next += Stopwatch.Frequency;
                while (Stopwatch.GetTimestamp() < next && !_stop) Thread.Sleep(20);
                if (!_stop) Sample(record: true);
            }
        }) { IsBackground = true, Name = "sampler" };
        _thread.Start();
    }

    public ResourceStats Stop()
    {
        _stop = true;
        _thread?.Join();
        Sample(record: true, gpu: false);
        var secs = _wall.Elapsed.TotalSeconds;
        var n = Math.Max(1, _samples);
        return new ResourceStats
        {
            Seconds = Math.Round(secs, 2),
            CpuPercentOfOneCore = Math.Round(_cpuMs / 10 / secs, 1),
            CpuPercentOfMachine = Math.Round(_cpuMs / 10 / secs / Environment.ProcessorCount, 1),
            LogicalCpus = Environment.ProcessorCount,
            Processes = _processes,
            PrivateMB = Math.Round(_privateMbMax, 0),
            CpuByProcess = _cpuMsByName.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / 10 / secs, 1)),
            GpuOurs = _gpuOursSum.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / n, 1)),
            GpuSystem = _gpuSystemSum.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / n, 1)),
        };
    }

    private void Sample(bool record, bool gpu = true)
    {
        var tree = ProcessTree(_rootPid);
        double privateMb = 0;
        foreach (var pid in tree)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var cpu = p.TotalProcessorTime;
                var name = DescribeProcess(p);
                privateMb += p.PrivateMemorySize64 / 1048576.0;
                // A process that appeared since the last sample counts from zero (it started in the window).
                var prev = _lastCpu.TryGetValue(pid, out var l) ? l.cpu : (record ? TimeSpan.Zero : cpu);
                if (record)
                {
                    var delta = (cpu - prev).TotalMilliseconds;
                    _cpuMs += delta;
                    _cpuMsByName[name] = _cpuMsByName.GetValueOrDefault(name) + delta;
                }
                _lastCpu[pid] = (cpu, name);
            }
            catch { }
        }
        if (record)
        {
            _privateMbMax = Math.Max(_privateMbMax, privateMb);
            _processes = Math.Max(_processes, tree.Count);
        }
        if (!gpu) return;

        string[] instances;
        try { instances = _gpuCategory.GetInstanceNames(); } catch { return; }
        var ours = new Dictionary<string, double>();     // per engine
        var system = new Dictionary<string, double>();
        var live = new HashSet<string>();
        foreach (var inst in instances)
        {
            var m = GpuInstance().Match(inst);
            if (!m.Success) continue;
            live.Add(inst);
            if (!_gpuCounters.TryGetValue(inst, out var counter))
            {
                counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                _gpuCounters[inst] = counter;
                try { counter.NextValue(); } catch { }
                continue;
            }
            float v;
            try { v = counter.NextValue(); } catch { continue; }
            var engine = $"{m.Groups["luid"].Value} {m.Groups["type"].Value}#{m.Groups["eng"].Value}";
            system[engine] = system.GetValueOrDefault(engine) + v;
            if (tree.Contains(int.Parse(m.Groups["pid"].Value))) ours[engine] = ours.GetValueOrDefault(engine) + v;
        }
        foreach (var stale in _gpuCounters.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _gpuCounters[stale].Dispose();
            _gpuCounters.Remove(stale);
        }
        if (!record) return;

        _samples++;
        Accumulate(_gpuOursSum, ours);
        Accumulate(_gpuSystemSum, system);
    }

    /// <summary>Engine type value = its busiest engine, as Task Manager shows it.</summary>
    private static void Accumulate(Dictionary<string, double> sums, Dictionary<string, double> perEngine)
    {
        foreach (var g in perEngine.GroupBy(kv => kv.Key[..kv.Key.IndexOf('#')]))
        {
            sums[g.Key] = sums.GetValueOrDefault(g.Key) + Math.Min(100, g.Max(kv => kv.Value));
        }
    }

    private static string DescribeProcess(Process p)
    {
        var name = p.ProcessName;
        if (name != "msedgewebview2") return name;
        try
        {
            // WebView2 child processes are distinguished by their --type= switch.
            var cmd = CommandLine(p.Id) ?? "";
            var m = Regex.Match(cmd, @"--type=(\S+)");
            var type = m.Success ? m.Groups[1].Value : "browser";
            if (type == "utility")
            {
                var s = Regex.Match(cmd, @"--utility-sub-type=(\S+)");
                if (s.Success) type += ":" + s.Groups[1].Value.Split('.').Last();
            }
            return $"webview2-{type}";
        }
        catch { return name; }
    }

    private static readonly Dictionary<int, string?> _cmdCache = new();
    private static string? CommandLine(int pid)
    {
        if (_cmdCache.TryGetValue(pid, out var c)) return c;
        try
        {
            using var s = new System.Management.ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pid}");
            foreach (var o in s.Get()) { c = o["CommandLine"] as string; break; }
        }
        catch { }
        _cmdCache[pid] = c;
        return c;
    }

    [GeneratedRegex(@"pid_(?<pid>\d+)_luid_(?<luid>0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_eng_(?<eng>\d+)_engtype_(?<type>\w+)")]
    private static partial Regex GpuInstance();

    public static HashSet<int> ProcessTree(int root)
    {
        var parents = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(2, 0);
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snap, ref e))
            {
                do { parents[(int)e.th32ProcessID] = (int)e.th32ParentProcessID; } while (Process32NextW(snap, ref e));
            }
        }
        finally { CloseHandle(snap); }

        var tree = new HashSet<int> { root };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (pid, parent) in parents)
            {
                if (pid != parent && tree.Contains(parent) && tree.Add(pid)) grew = true;
            }
        }
        return tree;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    public void Dispose()
    {
        foreach (var c in _gpuCounters.Values) c.Dispose();
    }
}
