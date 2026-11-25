// The window: WebView2 showing the page (wwwroot), and the messages between the page and the
// chain. The page owns the controls and both browser ends of the loop; this side owns
// SIPSorcery's two peer connections and the codecs in between.
//
// Page -> host: ready, clock (for the page to learn the offset between its clock and ours),
// start (with the settings), answer (per leg), stop, set (live settings), keyframe (per leg),
// pagestats (the page's own numbers, kept for --stats), log.
// Host -> page: hello (codecs available), clock, offers, state, stats, cpu, log, error.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SIPSorcery.TranscodeLoop;

internal sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Options _options;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _statsTimer = new() { Interval = 250 };
    private readonly StreamWriter _logFile;
    private readonly Task _probe;
    private TranscodeChain? _chain;
    private DecodedView? _view;
    private CoreWebView2Environment? _env;
    private JsonElement? _lastPageStats;
    private object? _lastStats;
    private double? _connectedAt;
    private bool _captured;

    // CPU: 1 core = 100, over each second.
    private int _cpuTick;
    private TimeSpan _hostCpu;
    private TimeSpan _webCpu;
    private double _cpuAt;

    public MainForm(Options options)
    {
        _options = options;
        Text = "TranscodeLoop: browser → SIPSorcery decode/encode → browser";
        Size = new Size(1680, 1000);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);

        Directory.CreateDirectory(Options.DataDir);
        _logFile = new StreamWriter(Path.Combine(Options.DataDir, "transcodeloop.log"), append: false) { AutoFlush = true };
        LibraryLog.Line += Log;
        _probe = Task.Run(() => Codecs.Probe(Log));

        _statsTimer.Tick += (_, _) => OnStatsTick();
        Load += async (_, _) => await StartAsync();
        FormClosing += (_, _) => Shutdown();
    }

    private async Task StartAsync()
    {
        // The fake camera runs at 20 fps unless told otherwise: 30 is the highest rate the page offers.
        var args = "--disable-features=WebRtcHideLocalIpsWithMdns --autoplay-policy=no-user-gesture-required " +
                   "--use-fake-device-for-media-stream=fps=30 --use-fake-ui-for-media-stream";
        _env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Options.DataDir, "WebView2"), new CoreWebView2EnvironmentOptions(args));
        await _web.EnsureCoreWebView2Async(_env);
        var core = _web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("transcodeloop.app", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.Deny);
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)
                e.State = CoreWebView2PermissionState.Allow;
        };
        core.WebMessageReceived += async (_, e) => await OnPageMessage(e.WebMessageAsJson);
        core.Settings.AreDevToolsEnabled = true;

        var query = string.Join("&", _options.Page.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        core.Navigate("https://transcodeloop.app/index.html" + (query.Length > 0 ? "?" + query : ""));
        _statsTimer.Start();
    }

    private async Task OnPageMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var msg = doc.RootElement;
        string type = msg.GetProperty("type").GetString() ?? "";
        try
        {
            switch (type)
            {
                case "ready":
                    await _probe;
                    _view?.PostTo(_web.CoreWebView2);
                    Post(new { type = "hello", caps = Codecs.Capabilities(), sipsorcery = typeof(SIPSorcery.Net.RTCPeerConnection).Assembly.GetName().Version?.ToString() });
                    break;
                case "clock":
                    Post(new { type = "clock", t0 = msg.GetProperty("t0").GetDouble(), host = Clock.NowMs });
                    break;
                case "start":
                    await StartChain(msg.GetProperty("config"));
                    break;
                case "answer":
                    _chain?.SetAnswer(msg.GetProperty("leg").GetString()!, msg.GetProperty("sdp").GetString()!);
                    break;
                case "stop":
                    StopChain();
                    break;
                case "set":
                    ApplySetting(msg.GetProperty("key").GetString()!, msg.GetProperty("value"));
                    break;
                case "keyframe":
                    if (msg.GetProperty("leg").GetString() == "a") _chain?.RequestKeyframeA("by hand", manual: true);
                    else _chain?.ForceKeyframeB("by hand");
                    break;
                case "pagestats":
                    _lastPageStats = msg.GetProperty("stats").Clone();
                    break;
                case "log":
                    Log("page: " + msg.GetProperty("line").GetString());
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"{type}: {ex.GetType().Name}: {ex.Message}");
            Post(new { type = "error", message = $"{type}: {ex.Message}" });
        }
    }

    private async Task StartChain(JsonElement c)
    {
        StopChain();
        string S(string name) => c.GetProperty(name).GetString() ?? "";
        var cfg = new ChainConfig(S("source"), c.GetProperty("width").GetInt32(), c.GetProperty("height").GetInt32(),
            S("mode"), S("codecIn"), S("decoder"), S("codecOut"), S("encoder"), c.GetProperty("encoderKbps").GetInt32());
        if (!cfg.Relay)
        {
            if (!Codecs.CanDecode(cfg.Decoder, cfg.CodecIn)) throw new InvalidOperationException($"{cfg.Decoder} can't decode {cfg.CodecIn}");
            if (!Codecs.CanEncode(cfg.Encoder, cfg.CodecOut)) throw new InvalidOperationException($"{cfg.Encoder} can't encode {cfg.CodecOut}");
        }
        SizeDecodedView(cfg.Width, cfg.Height);
        var chain = new TranscodeChain(cfg, _view, Log);
        chain.ShowDecoded = c.GetProperty("showDecoded").GetBoolean();
        chain.PliOnDrop = c.GetProperty("pliOnDrop").GetBoolean();
        chain.DropPercentA = c.GetProperty("dropA").GetInt32();
        chain.LossPercentB = c.GetProperty("lossB").GetInt32();
        chain.StateChanged += (leg, state) =>
        {
            Log($"leg {leg}: {state}");
            BeginInvoke(() =>
            {
                Post(new { type = "state", leg, state = state.ToString() });
                if (_chain == chain && chain.StateA == SIPSorcery.Net.RTCPeerConnectionState.connected &&
                    chain.StateB == SIPSorcery.Net.RTCPeerConnectionState.connected) _connectedAt ??= Clock.NowMs;
            });
        };
        _chain = chain;
        _connectedAt = null;
        var (a, b) = await chain.CreateOffersAsync();
        Post(new { type = "offers", a, b });
    }

    /// <summary>
    /// The decoded tile's shared buffer, sized for the stream about to start: replaced by a bigger
    /// one when the stream needs it, and by a smaller one after a big stream, so a 4K run's
    /// 75 MB isn't kept for a 640x480 one. On the UI thread, with no chain running.
    /// </summary>
    private void SizeDecodedView(int width, int height)
    {
        long needed = DecodedView.SlotBytesFor(width, height);
        if (_view != null && _view.SlotBytes >= needed && _view.SlotBytes <= needed * 4) return;
        var old = _view;
        _view = new DecodedView(_env!, width, height);
        _view.PostTo(_web.CoreWebView2);
        old?.Dispose();
    }

    private void StopChain()
    {
        var chain = _chain;
        _chain = null;
        _connectedAt = null;
        chain?.Dispose();
    }

    private void ApplySetting(string key, JsonElement value)
    {
        var chain = _chain;
        if (chain == null) return;
        switch (key)
        {
            case "dropA": chain.DropPercentA = value.GetInt32(); break;
            case "lossB": chain.LossPercentB = value.GetInt32(); break;
            case "pliOnDrop": chain.PliOnDrop = value.GetBoolean(); break;
            case "showDecoded": chain.ShowDecoded = value.GetBoolean(); break;
        }
    }

    private void OnStatsTick()
    {
        if (_chain is { } chain)
        {
            var stats = chain.Snapshot();
            _lastStats = stats;
            Post(new { type = "stats", stats });
        }
        if (++_cpuTick % 4 == 0) _ = PostCpuAsync();

        if (_connectedAt is { } at)
        {
            double secs = (Clock.NowMs - at) / 1000;
            if (_options.CapturePath != null && !_captured && secs >= _options.CaptureAfter)
            {
                _captured = true;
                _ = CaptureAsync(_options.CapturePath);
            }
            if (_options.ExitAfter is { } exit && secs >= exit + 0.5) Close();
        }
    }

    /// <summary>CPU of this process and of WebView2's processes, each with 1 core = 100.</summary>
    private async Task PostCpuAsync()
    {
        if (_env == null) return;
        try
        {
            var host = Process.GetCurrentProcess().TotalProcessorTime;
            var web = TimeSpan.Zero;
            foreach (var info in await _env.GetProcessExtendedInfosAsync())
            {
                try { using var p = Process.GetProcessById(info.ProcessInfo.ProcessId); web += p.TotalProcessorTime; }
                catch { /* gone */ }
            }
            double now = Clock.NowMs;
            if (_cpuAt > 0)
            {
                double wall = now - _cpuAt;
                Post(new
                {
                    type = "cpu",
                    host = Math.Round((host - _hostCpu).TotalMilliseconds / wall * 100),
                    web = Math.Round((web - _webCpu).TotalMilliseconds / wall * 100),
                    cores = Environment.ProcessorCount,
                });
            }
            (_hostCpu, _webCpu, _cpuAt) = (host, web, now);
        }
        catch (Exception ex)
        {
            Log($"cpu: {ex.Message}");
        }
    }

    private async Task CaptureAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = File.Create(path);
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            Log($"captured the window to {path}");
        }
        catch (Exception ex)
        {
            Log($"capture failed: {ex.Message}");
        }
    }

    private void Shutdown()
    {
        _statsTimer.Stop();
        if (_options.StatsPath != null)
        {
            try
            {
                File.WriteAllText(_options.StatsPath, JsonSerializer.Serialize(new { host = _lastStats, page = _lastPageStats }, new JsonSerializerOptions(JsonOpts) { WriteIndented = true }));
            }
            catch (Exception ex) { Log($"stats: {ex.Message}"); }
        }
        StopChain();
        LibraryLog.Line -= Log;
        _view?.Dispose();
        _logFile.Dispose();
    }

    private void Log(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        lock (_logFile)
        {
            try { _logFile.WriteLine(stamped); } catch (ObjectDisposedException) { }
        }
        if (!line.StartsWith("page: ")) Post(new { type = "log", line = stamped });
    }

    private void Post(object message)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Post(message)); } catch (InvalidOperationException) { }
            return;
        }
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOpts));
    }
}
