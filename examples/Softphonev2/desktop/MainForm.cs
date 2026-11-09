using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SIPSorcery.Softphone;

/// <summary>
/// The window: a WebView2 showing <c>wwwroot/index.html</c>, and the message bridge between that
/// page and <see cref="Phone"/>. Page → host: ready, dial, answer, decline, hangup, add-video,
/// loss, media-answer, and for accounts account-select, account-save, account-enable,
/// account-remove, account-register. Host → page: state (with the accounts and their
/// registrations), media-offer (again when video is added), media-health, quality (RTCP reception
/// reports, for diagnostics), video-refused, account-saved, account-error, log.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Settings _settings;
    private readonly Phone _phone;
    private readonly AccountStore _accounts;
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
        _accounts = new AccountStore(settings.Profile, settings.Account);
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
            // Once, on starting to ring: Changed comes again while ringing (the page's media, details).
            if (_settings.AutoAnswer && state == Phone.CallState.Ringing && _lastState != state)
                _ = Task.Delay(1000).ContinueWith(_ => _phone.AnswerAsync());
            // Testing switches: hang up after a while, and dial again once a call has ended.
            if (state == Phone.CallState.Connected && _lastState != state && _settings.HangupAfter is { } secs)
                _ = Task.Delay(secs * 1000).ContinueWith(_ => { if (_phone.State == Phone.CallState.Connected) _phone.Hangup(); });
            if (state == Phone.CallState.Ended && _lastState != state && --_dialsLeft > 0 && !string.IsNullOrWhiteSpace(_settings.Dial))
                _ = Task.Delay(2000).ContinueWith(_ => _phone.DialAsync(_settings.Dial, _settings.VideoCall, _accounts.SelectedId));
            _lastState = state;
        };
        _phone.Log += WriteLog;
        LibraryLog.Line += WriteLog;
        _phone.Trace += text => { lock (_logFile) _logFile.WriteLine(text); };
        _phone.TxLostChanged += lost => Post(new { type = "media-health", txLost = lost });
        _phone.Quality += q => Post(new { type = "quality", q.Dir, q.Kind, q.LossPct, q.Lost, q.JitterMs, q.RttMs });
        _phone.TextReceived += text =>
        {
            // The log file only, and only how much: what they type stays out of the log.
            lock (_logFile) _logFile.WriteLine($"{DateTime.Now:HH:mm:ss.fff} real-time text in: {text.Length} characters");
            Post(new { type = "text", text });
        };
        _phone.Details += d => Post(new
        {
            type = "details", d.Direction, d.Account, d.Signalling, d.TheirPhone, d.CallId, d.TheirMedia,
            d.MediaEncrypted, d.SignallingEncrypted, d.Audio, d.Video,
        });
        _phone.VideoRefused +=() => Post(new { type = "video-refused" });
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
        if (_settings.Diagnostics) query.Add("diag=1");
        if (_settings.VideoCall) query.Add("call=video");
        if (_settings.TestPattern) query.Add("video=test");
        if (_settings.AudioBars) query.Add("video=bars");
        if (_settings.AddVideoAfter is { } after) query.Add($"addvideo={after}");
        if (!string.IsNullOrEmpty(_settings.Say)) query.Add("say=" + Uri.EscapeDataString(_settings.Say));
        core.Navigate("https://softphone.app/index.html" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));

        try
        {
            if (_accounts.LoadNote != null) WriteLog(_accounts.LoadNote);
            _phone.SetAccounts(_accounts.All);
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
                    if (_phone.WebSocketAddress != null) WriteLog($"browser phones: connect to {_phone.WebSocketAddress} (from this PC, ws://localhost:{_settings.WsPort})");
                    Post(StateMessage());
                    if (_pendingOffer != null) { Post(new { type = "media-offer", sdp = _pendingOffer }); _pendingOffer = null; }
                    if (!string.IsNullOrWhiteSpace(_settings.Dial)) await _phone.DialAsync(_settings.Dial, _settings.VideoCall, _accounts.SelectedId);
                    break;
                case "log":
                    lock (_logFile) _logFile.WriteLine($"{DateTime.Now:HH:mm:ss.fff} page: {msg["text"]}");
                    break;
                case "dial":
                    await _phone.DialAsync((string)msg["address"]!, (bool?)msg["video"] ?? false, _accounts.SelectedId);
                    break;
                case "account-select":
                    _accounts.Select((string?)msg["id"]);
                    Post(StateMessage());
                    break;
                case "account-save":
                    SaveAccount(msg["account"]!.AsObject());
                    break;
                case "account-enable":
                    if (_accounts.Find((string?)msg["id"]) is { } toggled)
                    {
                        toggled.Enabled = (bool?)msg["enabled"] ?? true;
                        _accounts.Save(toggled);
                        WriteLog($"{toggled.Label}: {(toggled.Enabled ? "enabled" : "disabled")}");
                        _phone.SetAccounts(_accounts.All);
                    }
                    break;
                case "account-remove":
                    _accounts.Remove((string)msg["id"]!);
                    _phone.SetAccounts(_accounts.All);
                    break;
                case "account-register":
                    _phone.Reregister((string)msg["id"]!);
                    break;
                case "add-video":
                    _phone.AddVideo();
                    break;
                case "text":
                    _phone.SendText((string)msg["text"]!);
                    break;
                case "add-text":
                    _phone.AddText();
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
                case "copy":
                    // The host's clipboard rather than the page's, which needs permissions the
                    // page may not have.
                    Clipboard.SetText((string)msg["text"]!);
                    WriteLog($"copied {msg["text"]}");
                    break;
                case "loss":
                    _phone.SetLoss((int?)msg["audio"] ?? 0, (int?)msg["video"] ?? 0);
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

    /// <summary>
    /// Adds or updates an account from the Manage accounts form. An empty password keeps the
    /// saved one. The page gets account-saved (with the id, for a new one) or account-error.
    /// </summary>
    private void SaveAccount(JsonObject a)
    {
        var id = (string?)a["id"];
        var existing = string.IsNullOrEmpty(id) ? null : _accounts.Find(id);
        var account = new SipAccount
        {
            Id = existing?.Id ?? "",
            Name = ((string?)a["name"] ?? "").Trim(),
            DisplayName = NullIfBlank((string?)a["displayName"]),
            Username = ((string?)a["username"] ?? "").Trim(),
            Server = ((string?)a["server"] ?? "").Trim(),
            AuthUsername = NullIfBlank((string?)a["authUsername"]),
            Password = (string?)a["password"] ?? "",
            Register = (bool?)a["register"] ?? true,
            Expiry = Math.Clamp((int?)a["expiry"] ?? 300, 60, 7200),
            Enabled = existing?.Enabled ?? true,
        };
        var missing = new[] { ("username", account.Username), ("server", account.Server) }.Where(f => f.Item2.Length == 0).Select(f => f.Item1).ToList();
        if (missing.Count > 0)
        {
            Post(new { type = "account-error", text = $"Fill in the {string.Join(" and ", missing)}." });
            return;
        }
        if (account.Server.Contains(' ') || account.Username.Contains(' '))
        {
            Post(new { type = "account-error", text = "The username and server can't contain spaces." });
            return;
        }
        var saved = _accounts.Save(account);
        WriteLog($"{saved.Label}: account {(existing == null ? "added" : "saved")}");
        _phone.SetAccounts(_accounts.All);
        Post(new { type = "account-saved", id = saved.Id });
        Post(StateMessage());
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void WriteLog(string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        lock (_logFile) _logFile.WriteLine(text);
        Post(new { type = "log", text });
    }

    private object StateMessage()
    {
        var selected = _accounts.Selected;
        var (registration, registrationDetail) = _phone.RegistrationOf(selected?.Id);
        // Our name, for the placeholder the page sends while the camera is off: the call's
        // account's, else the one calls go out from.
        var me = _phone.CallAccount ?? selected;
        var regs = _phone.Registrations.ToDictionary(r => r.Account.Id);
        return new
        {
            type = "state",
            call = _phone.State.ToString().ToLowerInvariant(),
            remote = _phone.Remote,
            reason = _phone.EndReason,
            // The account calls go out from (Call from), and its registration.
            registration = selected == null ? "none" : registration,
            registrationDetail,
            account = selected?.Aor,
            // What the header copies for others to call us on: the account's address, or with no
            // account, this PC's address and SIP port.
            address = selected != null ? $"sip:{selected.Aor}" : _phone.DirectAddress,
            me = me == null ? null : string.IsNullOrWhiteSpace(me.DisplayName) ? me.Username : me.DisplayName,
            // Every account, for Call from and Manage accounts (the web softphone sends none, and
            // shows neither). Passwords stay here: the page only learns whether there is one.
            accountId = selected?.Id,
            accounts = _accounts.All.Select(a => new
            {
                a.Id, name = a.Label, a.DisplayName, a.Username, a.Server, a.AuthUsername, hasPassword = a.Password.Length > 0,
                a.Register, a.Expiry, a.Enabled,
                reg = regs.TryGetValue(a.Id, out var r) ? r.State : "off",
                regDetail = regs.TryGetValue(a.Id, out var rd) ? rd.Detail : null,
            }).ToList(),
            videoAllowed = _settings.OfferVideo,
            incomingVideo = _phone.IncomingVideo,
            // Real-time text on the call: off, adding, on, refused or disabled.
            text = _phone.TextState,
            lossAudio = _phone.AudioLossPercent,
            lossVideo = _phone.VideoLossPercent,
        };
    }

    private void Post(object message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Post(message)); return; }
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOpts));
    }
}
