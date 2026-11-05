using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SIPSorcery.Softphone;

/// <summary>
/// The window: a WebView2 showing <c>wwwroot/index.html</c>, and the message bridge between that
/// page and <see cref="Phone"/>. Page → host: ready, dial, answer, decline, hangup, add-video,
/// media-answer. Host → page: state, media-offer (again when video is added), media-health,
/// video-refused, log.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Settings _settings;
    private readonly Phone _phone;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private string? _pendingOffer;
    private bool _pageReady;
    private Phone.CallState _lastState;
    private int _dialsLeft;
    private readonly StreamWriter _logFile;

    public MainForm(Settings settings)
    {
        _settings = settings;
        _dialsLeft = settings.DialCount;
        _phone = new Phone(settings);
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
        Directory.CreateDirectory(logDir);
        _logFile = new StreamWriter(Path.Combine(logDir, $"softphone-{settings.Profile}.log"), append: false) { AutoFlush = true };
        Text = "SIPSorcery Softphone";
        ClientSize = new Size(1100, 720);
        MinimumSize = new Size(560, 480);
        Controls.Add(_web);

        _phone.Changed += () =>
        {
            Post(StateMessage());
            var state = _phone.State;
            if (_settings.AutoAnswer && state == Phone.CallState.Ringing)
                _ = Task.Delay(1000).ContinueWith(_ => _phone.AnswerAsync());
            // Testing switches: hang up after a while, and dial again once a call has ended.
            if (state == Phone.CallState.Connected && _lastState != state && _settings.HangupAfter is { } secs)
                _ = Task.Delay(secs * 1000).ContinueWith(_ => { if (_phone.State == Phone.CallState.Connected) _phone.Hangup(); });
            if (state == Phone.CallState.Ended && _lastState != state && --_dialsLeft > 0 && !string.IsNullOrWhiteSpace(_settings.Dial))
                _ = Task.Delay(2000).ContinueWith(_ => _phone.DialAsync(_settings.Dial, _settings.VideoCall));
            _lastState = state;
        };
        _phone.Log += WriteLog;
        _phone.Trace += text => { lock (_logFile) _logFile.WriteLine(text); };
        _phone.TxLostChanged += lost => Post(new { type = "media-health", txLost = lost });
        _phone.VideoRefused += () => Post(new { type = "video-refused" });
        _phone.MediaOffer += sdp =>
        {
            if (_pageReady) Post(new { type = "media-offer", sdp });
            else _pendingOffer = sdp;
        };

        Load += async (_, _) => await StartAsync();
        FormClosing += (_, _) => { _phone.Dispose(); _logFile.Dispose(); };
    }

    private async Task StartAsync()
    {
        var args = "--disable-features=WebRtcHideLocalIpsWithMdns --autoplay-policy=no-user-gesture-required";
        if (_settings.FakeMedia) args += " --use-fake-device-for-media-stream=fps=30 --use-fake-ui-for-media-stream";
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SIPSorcery", "Softphonev2", "WebView2", _settings.Profile);
        var env = await CoreWebView2Environment.CreateAsync(null, dataDir, new CoreWebView2EnvironmentOptions(args));
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("softphone.app", Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            CoreWebView2HostResourceAccessKind.Deny);
        // The page is ours, so camera and microphone are always allowed; there's no browser prompt in an app.
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind is CoreWebView2PermissionKind.Camera or CoreWebView2PermissionKind.Microphone)
                e.State = CoreWebView2PermissionState.Allow;
        };
        core.WebMessageReceived += async (_, e) => await OnPageMessage(e.WebMessageAsJson);
        core.Settings.AreDevToolsEnabled = true;
        var query = new List<string>();
        if (_settings.CameraOff) query.Add("camera=off");
        if (_settings.VideoCall) query.Add("call=video");
        if (_settings.AddVideoAfter is { } after) query.Add($"addvideo={after}");
        core.Navigate("https://softphone.app/index.html" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));

        try
        {
            _phone.Start();
        }
        catch (Exception ex)
        {
            WriteLog($"SIP start failed: {ex.Message}");
        }
    }

    private async Task OnPageMessage(string json)
    {
        var msg = JsonNode.Parse(json)!.AsObject();
        try
        {
            switch ((string?)msg["type"])
            {
                case "ready":
                    _pageReady = true;
                    if (_phone.PortNote != null) WriteLog(_phone.PortNote);
                    WriteLog($"SIP listening on {_phone.ListeningOn}");
                    Post(StateMessage());
                    if (_pendingOffer != null) { Post(new { type = "media-offer", sdp = _pendingOffer }); _pendingOffer = null; }
                    if (!string.IsNullOrWhiteSpace(_settings.Dial)) await _phone.DialAsync(_settings.Dial, _settings.VideoCall);
                    break;
                case "log":
                    lock (_logFile) _logFile.WriteLine($"{DateTime.Now:HH:mm:ss.fff} page: {msg["text"]}");
                    break;
                case "dial":
                    await _phone.DialAsync((string)msg["address"]!, (bool?)msg["video"] ?? false);
                    break;
                case "add-video":
                    _phone.AddVideo();
                    break;
                case "answer":
                    await _phone.AnswerAsync();
                    break;
                case "decline":
                    _phone.Decline();
                    break;
                case "hangup":
                    _phone.Hangup();
                    break;
                case "media-answer":
                    _phone.SetPageAnswer((string)msg["sdp"]!);
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteLog($"{msg["type"]} failed: {ex.Message}");
        }
    }

    private void WriteLog(string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        lock (_logFile) _logFile.WriteLine(text);
        Post(new { type = "log", text });
    }

    private object StateMessage() => new
    {
        type = "state",
        call = _phone.State.ToString().ToLowerInvariant(),
        remote = _phone.Remote,
        reason = _phone.EndReason,
        registration = _phone.Registration,
        registrationDetail = _phone.RegistrationDetail,
        account = _settings.HasAccount ? $"{_settings.Account!.Username}@{_settings.Account.Server}" : null,
        // Our name, for the placeholder the page sends while the camera is off.
        me = _settings.HasAccount ? (string.IsNullOrWhiteSpace(_settings.Account!.DisplayName) ? _settings.Account.Username : _settings.Account.DisplayName) : null,
        videoAllowed = _settings.OfferVideo,
        incomingVideo = _phone.IncomingVideo,
    };

    private void Post(object message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Post(message)); return; }
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOpts));
    }
}
