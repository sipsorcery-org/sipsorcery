// Runs the video spike: starts the far end, then for each configuration launches the right near
// end, waits for every tile to show video, lets it settle, measures for a fixed window (resources
// sampled from outside, frame stats reported by the near end), and writes one JSON file per run
// plus a summary table.
//
//   Bench --matrix default            the full set of runs
//   Bench --matrix smoke              one short run of each path
//   Bench --path relay --streams 8 --codec h264 --camera real --cameramode shared
//   options: --warmup 8 --duration 30 --affinity 0xF (near end on 4 logical CPUs) --out results

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VideoSpike;
using VideoSpike.Bench;

var root = FindRoot();
bool noFarEnd = args.Contains("--no-farend");
args = args.Where(a => a != "--no-farend").ToArray();
string matrix = "", outDir = Path.Combine(root, "results"), tag = "";
int warmup = 8, duration = 30;
long affinity = 0;
for (int i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--matrix": matrix = args[i + 1]; break;
        case "--warmup": warmup = int.Parse(args[i + 1]); break;
        case "--duration": duration = int.Parse(args[i + 1]); break;
        case "--affinity": affinity = Convert.ToInt64(args[i + 1], 16); break;
        case "--out": outDir = Path.GetFullPath(args[i + 1]); break;
        case "--tag": tag = args[i + 1]; break;
        case "--camera" when matrix.Length > 0 || args.Contains("--matrix"): Matrix.Camera = Enum.Parse<CameraKind>(args[i + 1], true); break;
    }
}

var configs = matrix switch
{
    "" => [RunConfig.Parse(args)],
    "smoke" => Matrix.Smoke(),
    "default" => Matrix.Default(),
    "cores4" => Matrix.Cores4(),
    "camera" => Matrix.CameraRuns(),
    _ => throw new ArgumentException($"Unknown matrix {matrix}."),
};
if (matrix == "smoke") { warmup = Math.Min(warmup, 4); duration = Math.Min(duration, 10); }

var rawDir = Path.Combine(outDir, "raw");
Directory.CreateDirectory(rawDir);
Console.WriteLine($"{configs.Count} runs, {warmup} s warm-up + {duration} s measured each" +
                  (affinity != 0 ? $", near end limited to CPUs 0x{affinity:X}" : ""));

using var farEnd = noFarEnd ? null : StartFarEnd(root, affinity);
await Task.Delay(1500);

var results = new List<JsonElement>();
int n = 0;
foreach (var cfg in configs)
{
    n++;
    var label = cfg.Label + (tag.Length > 0 ? "-" + tag : "");
    Console.Write($"[{n}/{configs.Count}] {label} ... ");
    try
    {
        var result = await RunOne(cfg, label);
        var json = JsonSerializer.Serialize(result, Json.Indented);
        File.WriteAllText(Path.Combine(rawDir, label + ".json"), json);
        results.Add(JsonDocument.Parse(json).RootElement);
        Console.WriteLine(Summary.OneLine(results[^1]));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.Message}");
        File.WriteAllText(Path.Combine(rawDir, label + ".failed.txt"), ex.ToString());
    }
    await Task.Delay(2000);   // let the far end tear down the previous calls
}

farEnd?.Kill(true);
var allRaw = Directory.GetFiles(rawDir, "*.json").Select(f => JsonDocument.Parse(File.ReadAllText(f)).RootElement).ToList();
File.WriteAllText(Path.Combine(outDir, "summary.md"), Summary.Markdown(allRaw));
File.WriteAllText(Path.Combine(outDir, "summary.csv"), Summary.Csv(allRaw));
Console.WriteLine($"Wrote {Path.Combine(outDir, "summary.md")}");
return 0;

async Task<object> RunOne(RunConfig cfg, string label)
{
    var exe = cfg.Path == PipelinePath.Wpf ? Exe(root, "NearEnd.Wpf") : Exe(root, "NearEnd.Web");
    var psi = new ProcessStartInfo(exe)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(exe),
    };
    foreach (var a in cfg.ToArgs()) psi.ArgumentList.Add(a);

    using var proc = Process.Start(psi)!;
    if (affinity != 0) proc.ProcessorAffinity = (IntPtr)affinity;

    var log = new StringBuilder();
    var lines = System.Threading.Channels.Channel.CreateUnbounded<string>();
    proc.OutputDataReceived += (_, e) => { if (e.Data != null) lines.Writer.TryWrite(e.Data); };
    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();

    async Task<string> Expect(string prefix, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            var line = await lines.Reader.ReadAsync(cts.Token);
            if (line.StartsWith("FAILED")) throw new ApplicationException(line);
            if (line.StartsWith(prefix)) return line;
        }
    }

    try
    {
        await Expect("READY", 60000);
        await Task.Delay(warmup * 1000);

        using var sampler = new ResourceSampler(proc.Id);
        proc.StandardInput.WriteLine("START");
        sampler.Start();
        await Task.Delay(duration * 1000);
        var resources = sampler.Stop();
        proc.StandardInput.WriteLine("STOP");
        var statsLine = await Expect("STATS ", 20000);
        var stats = JsonSerializer.Deserialize<NearEndStats>(statsLine[6..], Json.Options)!;

        proc.StandardInput.WriteLine("EXIT");
        if (!proc.WaitForExit(10000)) proc.Kill(true);

        return new
        {
            label,
            config = cfg,
            machine = Machine.Describe(affinity),
            when = DateTime.Now,
            warmupSeconds = warmup,
            resources,
            nearEnd = stats,
        };
    }
    finally
    {
        if (!proc.HasExited) proc.Kill(true);
        lock (log) File.WriteAllText(Path.Combine(rawDir, label + ".log"), log.ToString());
    }
}

static Process StartFarEnd(string root, long nearAffinity)
{
    var psi = new ProcessStartInfo(Exe(root, "FarEnd"))
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    var p = Process.Start(psi)!;
    var farLog = File.CreateText(Path.Combine(root, "artifacts", "farend.log"));
    farLog.AutoFlush = true;
    p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (farLog) farLog.WriteLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine("farend: " + e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    if (nearAffinity != 0)
    {
        // Keep the far end off the near end's CPUs so it doesn't compete with what's measured.
        long all = (1L << Environment.ProcessorCount) - 1;
        p.ProcessorAffinity = (IntPtr)(all & ~nearAffinity);
        Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)(all & ~nearAffinity);
    }
    return p;
}

static string Exe(string root, string name)
{
    var path = Path.Combine(root, "artifacts", "bin", name, "Release", name + ".exe");
    return File.Exists(path) ? path : throw new FileNotFoundException($"Build {name} in Release first.", path);
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "make-clips.ps1"))) dir = dir.Parent;
    return dir?.FullName ?? throw new DirectoryNotFoundException("Can't find the spike folder.");
}
