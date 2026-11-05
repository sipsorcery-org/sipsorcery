using System.Globalization;
using System.Management;
using System.Text;
using System.Text.Json;

namespace VideoSpike.Bench;

public static class Machine
{
    private static object? _cached;

    public static object Describe(long affinity)
    {
        _cached ??= new
        {
            cpu = Wmi("Win32_Processor", "Name").FirstOrDefault(),
            logicalCpus = Environment.ProcessorCount,
            gpus = Wmi("Win32_VideoController", "Name"),
            display = Wmi("Win32_VideoController", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate")
                .Where(s => !s.StartsWith(" ")).ToList(),
            gpuLuids = Gpus.Names(),
            os = Environment.OSVersion.VersionString,
            webView2 = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "pv", null),
        };
        return new { info = _cached, nearEndAffinity = affinity == 0 ? "all" : $"0x{affinity:X}" };
    }

    private static List<string> Wmi(string cls, params string[] props)
    {
        var list = new List<string>();
        try
        {
            using var s = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}");
            foreach (var o in s.Get()) list.Add(string.Join(" ", props.Select(p => o[p]?.ToString() ?? "")));
        }
        catch { }
        return list;
    }
}

/// <summary>Turns the per-run JSON into one line for the console and a table for the write-up.</summary>
public static class Summary
{
    private static readonly string[] Cols =
    [
        "run", "path", "streams", "codec", "res", "camera",
        "cpu (1 core = 100)", "cpu % of machine", "gpu 3d %", "gpu decode %", "gpu encode %",
        "min tile fps", "avg tile fps", "dropped %", "stalls", "latency p50 ms", "latency p95 ms",
        "decoder", "hw decode", "camera encoder", "camera fps/leg", "private MB",
    ];

    public static Dictionary<string, string> Row(JsonElement r)
    {
        var cfg = r.GetProperty("config");
        var res = r.GetProperty("resources");
        var ne = r.GetProperty("nearEnd");
        var tiles = ne.GetProperty("tiles").EnumerateArray().ToList();
        var remote = tiles.Where(t => t.GetProperty("kind").GetString() == "remote").ToList();

        double Fps(JsonElement t) => t.GetProperty("presentedFps").GetDouble();
        long L(JsonElement t, string p) => t.TryGetProperty(p, out var v) ? v.GetInt64() : 0;
        var received = remote.Sum(t => L(t, "framesReceived"));
        var dropped = remote.Sum(t => L(t, "framesDropped"));
        var lat = remote.Where(t => t.TryGetProperty("latency", out _)).Select(t => t.GetProperty("latency")).ToList();

        string Gpu(string type)
        {
            var g = res.GetProperty("gpuOurs").EnumerateObject().Where(p => p.Name.EndsWith(" " + type)).ToList();
            if (g.Count == 0) return "0";
            return string.Join(" + ", g.Select(p =>
                $"{p.Value.GetDouble().ToString("0.#", CultureInfo.InvariantCulture)} {Gpus.Short(p.Name[..p.Name.LastIndexOf(' ')])}"));
        }

        var camera = cfg.GetProperty("camera").GetString()!.ToLowerInvariant();
        string camLabel = camera == "off" ? "off" : $"{camera} {cfg.GetProperty("cameraMode").GetString()!.ToLowerInvariant()}";
        ne.TryGetProperty("camera", out var cam);
        bool hasCam = cam.ValueKind == JsonValueKind.Object;
        string? S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

        return new Dictionary<string, string>
        {
            ["run"] = r.GetProperty("label").GetString()!,
            ["path"] = cfg.GetProperty("path").GetString()!.ToLowerInvariant(),
            ["streams"] = cfg.GetProperty("streams").GetInt32().ToString(),
            ["codec"] = cfg.GetProperty("codecName").GetString()!,
            ["res"] = cfg.GetProperty("resolution").GetString()!,
            ["camera"] = camLabel,
            ["cpu (1 core = 100)"] = res.GetProperty("cpuPercentOfOneCore").GetDouble().ToString("0", CultureInfo.InvariantCulture),
            ["cpu % of machine"] = res.GetProperty("cpuPercentOfMachine").GetDouble().ToString("0.0", CultureInfo.InvariantCulture),
            ["gpu 3d %"] = Gpu("3D"),
            ["gpu decode %"] = Gpu("VideoDecode"),
            ["gpu encode %"] = Gpu("VideoEncode"),
            ["min tile fps"] = remote.Count == 0 ? "" : remote.Min(Fps).ToString("0.0", CultureInfo.InvariantCulture),
            ["avg tile fps"] = remote.Count == 0 ? "" : remote.Average(Fps).ToString("0.0", CultureInfo.InvariantCulture),
            ["dropped %"] = received == 0 ? "" : (100.0 * dropped / received).ToString("0.0", CultureInfo.InvariantCulture),
            ["stalls"] = remote.Sum(t => t.GetProperty("stalls").GetInt32()).ToString(),
            ["latency p50 ms"] = lat.Count == 0 ? "" : Median(lat.Select(l => l.GetProperty("p50").GetDouble())).ToString("0", CultureInfo.InvariantCulture),
            ["latency p95 ms"] = lat.Count == 0 ? "" : lat.Max(l => l.GetProperty("p95").GetDouble()).ToString("0", CultureInfo.InvariantCulture),
            ["decoder"] = remote.Select(t => S(t, "decoder")).FirstOrDefault(d => d != null) ?? "",
            ["hw decode"] = remote.Select(t => S(t, "hardwareDecode")).FirstOrDefault(d => d != null) ?? "",
            ["camera encoder"] = hasCam ? $"{S(cam, "encoder")} x{S(cam, "encoders")}" + (S(cam, "hardwareEncode") == "True" ? " (hw)" : "") : "",
            ["camera fps/leg"] = hasCam ? S(cam, "sentFpsPerLeg") ?? "" : "",
            ["private MB"] = res.GetProperty("privateMB").GetDouble().ToString("0", CultureInfo.InvariantCulture),
        };
    }

    private static double Median(IEnumerable<double> v)
    {
        var a = v.OrderBy(x => x).ToArray();
        return a.Length == 0 ? 0 : a[a.Length / 2];
    }

    public static string OneLine(JsonElement r)
    {
        var row = Row(r);
        return $"cpu {row["cpu (1 core = 100)"]} ({row["cpu % of machine"]}% of machine), gpu 3d {row["gpu 3d %"]} dec {row["gpu decode %"]} enc {row["gpu encode %"]}, " +
               $"fps min {row["min tile fps"]} avg {row["avg tile fps"]}, dropped {row["dropped %"]}%, stalls {row["stalls"]}, " +
               $"latency p50 {row["latency p50 ms"]} p95 {row["latency p95 ms"]} ms, {row["decoder"]}" +
               (row["camera encoder"].Length > 0 ? $", camera {row["camera encoder"]} {row["camera fps/leg"]} fps/leg" : "");
    }

    public static string Markdown(List<JsonElement> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| " + string.Join(" | ", Cols) + " |");
        sb.AppendLine("|" + string.Concat(Cols.Select(_ => "---|")));
        foreach (var r in Order(runs)) sb.AppendLine("| " + string.Join(" | ", Cols.Select(c => Row(r)[c])) + " |");
        return sb.ToString();
    }

    public static string Csv(List<JsonElement> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Cols.Select(Quote)));
        foreach (var r in Order(runs)) sb.AppendLine(string.Join(",", Cols.Select(c => Quote(Row(r)[c]))));
        return sb.ToString();
    }

    private static IEnumerable<JsonElement> Order(List<JsonElement> runs) =>
        runs.OrderBy(r => r.GetProperty("config").GetProperty("codecName").GetString())
            .ThenBy(r => r.GetProperty("config").GetProperty("camera").GetString() != "Off")
            .ThenBy(r => r.GetProperty("config").GetProperty("resolution").GetString())
            .ThenBy(r => r.GetProperty("config").GetProperty("streams").GetInt32())
            .ThenBy(r => r.GetProperty("label").GetString())
            .ThenBy(r => r.GetProperty("config").GetProperty("path").GetString());

    private static string Quote(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
