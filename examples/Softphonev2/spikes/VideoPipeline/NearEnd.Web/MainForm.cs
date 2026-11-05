using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SIPSorcery.SIP;
using VideoSpike;

namespace NearEnd.Web;

/// <summary>
/// Hosts the grid page in WebView2 and runs one of the two WebView2 pipelines behind it:
///   Relay  - SIP RTP is forwarded, still encoded, to the page over a local WebRTC connection;
///            the browser engine decodes, renders and encodes the camera.
///   Buffer - C# decodes with FFmpeg and writes NV12 frames into WebView2 shared buffers;
///            the page uploads them to WebGL textures.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly RunConfig _cfg;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly BenchLink _control = new();
    private readonly SIPTransport _transport = SipLeg.CreateTransport();
    private readonly List<SipLeg> _legs = new();
    private IPipeline? _pipeline;
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _waits = new();
    private readonly List<(double rtt, double offset)> _pings = new();
    private readonly HashSet<int> _firstFrames = new();
    private bool _ready;
    private double _windowStart;

    public CoreWebView2 Core => _web.CoreWebView2;
    public double ClockOffsetMs { get; private set; }
    public double ClockRttMs { get; private set; }

    public MainForm(RunConfig cfg)
    {
        _cfg = cfg;
        Text = $"Video spike - {cfg.Label}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(40, 40);
        ClientSize = new Size(cfg.Width, cfg.Height);
        // Chromium throttles rendering in windows it thinks are hidden, which would skew the run.
        TopMost = true;
        Controls.Add(_web);
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) => Shutdown();
    }

    private async Task StartAsync()
    {
        try
        {
            var args = "--disable-features=WebRtcHideLocalIpsWithMdns --use-fake-ui-for-media-stream " +
                       "--autoplay-policy=no-user-gesture-required";
            if (_cfg.Camera == CameraKind.Fake) args += " --use-fake-device-for-media-stream=fps=30";
            var env = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Path.GetTempPath(), "videospike-webview2"),
                new CoreWebView2EnvironmentOptions(args));
            await _web.EnsureCoreWebView2Async(env);

            Core.SetVirtualHostNameToFolderMapping("spike.example",
                Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Allow);
            Core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Allow;
            Core.WebMessageReceived += OnWebMessage;
            Core.ProcessFailed += (_, e) => BenchLink.Log($"WebView2 process failed: {e.ProcessFailedKind} {e.Reason}");

            var page = _cfg.Path == PipelinePath.Relay ? "relay.html" : "buffer.html";
            var q = $"streams={_cfg.Streams}&camera={_cfg.Camera.ToString().ToLowerInvariant()}" +
                    $"&mode={_cfg.CameraMode.ToString().ToLowerInvariant()}&codec={_cfg.CodecName}&kbps={_cfg.CameraBitrateKbps}";
            var hello = Wait("hello");
            Core.Navigate($"https://spike.example/{page}?{q}");
            await hello;

            await CalibrateClockAsync();
            BenchLink.Log($"clock offset {ClockOffsetMs:0.00} ms (rtt {ClockRttMs:0.00} ms)");

            _legs.AddRange(await SipLeg.CallAllAsync(_transport, _cfg));
            BenchLink.Log($"{_legs.Count} calls up");

            _pipeline = _cfg.Path == PipelinePath.Relay ? new RelayPipeline(this, _cfg, _legs) : new BufferPipeline(this, _cfg, _legs);
            await _pipeline.StartAsync();

            _control.Start += () => BeginInvoke(() =>
            {
                _windowStart = Clock.NowMs;
                _pipeline.ResetCounters();
                Post(new { type = "start" });
            });
            _control.Stop += StopAsync;
            _control.Exit += () => BeginInvoke(Close);
            _control.Run();

            // Give every tile up to 20 s to show its first frame; report READY regardless.
            _ = Task.Delay(20000).ContinueWith(_ => BeginInvoke(() => SetReady("timeout")));
        }
        catch (Exception ex)
        {
            BenchLink.Log($"startup failed: {ex}");
            BenchLink.Send("FAILED " + ex.Message.Replace('\n', ' '));
            Close();
        }
    }

    private void SetReady(string why)
    {
        if (_ready) return;
        _ready = true;
        BenchLink.Log($"ready ({why})");
        _control.Ready();
    }

    private Task<NearEndStats> StopAsync()
    {
        var tcs = new TaskCompletionSource<NearEndStats>();
        BeginInvoke(async () =>
        {
            try
            {
                var window = (Clock.NowMs - _windowStart) / 1000.0;
                var pageStats = await Request(new { type = "stop" }, "stats");
                var stats = _pipeline!.Collect(pageStats, window);
                stats.WindowSeconds = Math.Round(window, 2);
                stats.ClockOffsetMs = Math.Round(ClockOffsetMs, 2);
                stats.ClockRttMs = Math.Round(ClockRttMs, 2);
                tcs.SetResult(stats);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Estimates (browser clock - C# clock) from the ping with the shortest round trip; the page's
    /// performance.timeOrigin isn't guaranteed to agree with the system clock to the millisecond.
    /// </summary>
    private async Task CalibrateClockAsync()
    {
        for (int i = 0; i < 40; i++)
        {
            var t0 = Clock.NowMs;
            var pong = await Request(new { type = "ping", t0 }, "pong");
            var t1 = Clock.NowMs;
            var tp = pong["tp"]!.GetValue<double>();
            _pings.Add((t1 - t0, tp - (t0 + t1) / 2));
        }
        var best = _pings.OrderBy(p => p.rtt).First();
        (ClockRttMs, ClockOffsetMs) = best;
    }

    public void Post(object message)
    {
        if (InvokeRequired) { BeginInvoke(() => Post(message)); return; }
        Core.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json.Options));
    }

    private Task<JsonObject> Wait(string type)
    {
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waits[type] = tcs;
        return tcs.Task;
    }

    public Task<JsonObject> Request(object message, string replyType)
    {
        var t = Wait(replyType);
        Post(message);
        return t;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = JsonNode.Parse(e.WebMessageAsJson)!.AsObject();
        var type = msg["type"]!.GetValue<string>();
        switch (type)
        {
            case "log":
                BenchLink.Log("page: " + msg["text"]);
                break;
            case "firstFrame":
                _firstFrames.Add(msg["tile"]!.GetValue<int>());
                int expected = _cfg.Streams + (_cfg.Camera == CameraKind.Off ? 0 : 1);
                if (_firstFrames.Count >= expected) SetReady("all tiles showing");
                break;
            default:
                if (_waits.Remove(type, out var tcs)) tcs.SetResult(msg);
                else _pipeline?.OnMessage(type, msg);
                break;
        }
    }

    private void Shutdown()
    {
        foreach (var leg in _legs) leg.Hangup();
        _pipeline?.Dispose();
        Thread.Sleep(300);   // let the BYEs go out
        _transport.Shutdown();
    }
}

internal interface IPipeline : IDisposable
{
    Task StartAsync();
    void ResetCounters();
    void OnMessage(string type, JsonObject msg);
    NearEndStats Collect(JsonObject pageStats, double windowSeconds);
}
