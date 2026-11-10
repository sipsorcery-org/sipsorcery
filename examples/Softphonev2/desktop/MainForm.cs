using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SIPSorcery.Softphone;

/// <summary>
/// The window: a WebView2 showing <c>wwwroot/index.html</c>, and the message bridge between that
/// page and <see cref="Phone"/>. Page → host: ready, dial, answer, decline, hangup, hold,
/// add-video, text, add-text, loss, media-answer, and for accounts account-select, account-save,
/// account-enable, account-remove, account-register. Host → page: state (every call, with the
/// accounts and their registrations), media-offer (again when video is added), media-health,
/// quality (RTCP reception reports, for diagnostics), details, text, video-refused, account-saved,
/// account-error, log. Messages about one call carry its <c>id</c>.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Settings _settings;
    private readonly Phone _phone;
    private readonly AccountStore _accounts;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly List<object> _pendingOffers = new();   // media offers made before the page was ready
    private bool _pageReady;
    private readonly Dictionary<string, Phone.CallState> _lastStates = new();   // each call's state at the last Changed
    private int _dialsLeft;
    private string? _dialCallId;   // the call --dial placed, to place it again when it ends (--dial-count)
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
            lock (_lastStates)
            {
                foreach (var c in _phone.Calls)
                {
                    var state = c.State;
                    _lastStates.TryGetValue(c.Id, out var last);
                    if (state == last) continue;
                    _lastStates[c.Id] = state;
                    var id = c.Id;
                    // Once per call, on starting to ring: Changed comes again while it rings.
                    // Answered alongside any other calls; holding them is the page's choice.
                    if (_settings.AutoAnswer && state == Phone.CallState.Ringing)
                        _ = Task.Delay(1000).ContinueWith(_ => _phone.AnswerAsync(id));
                    // Testing switches: hang up after a while, and dial again once a call has ended.
                    if (state == Phone.CallState.Connected && _settings.HangupAfter is { } secs)
                        _ = Task.Delay(secs * 1000).ContinueWith(_ => _phone.Hangup(id));
                    if (state == Phone.CallState.Ended && id == _dialCallId && --_dialsLeft > 0 && !string.IsNullOrWhiteSpace(_settings.Dial))
                        _ = Task.Delay(2000).ContinueWith(_ => DialAtStart());
                }
                // Calls the phone has dropped from its list.
                foreach (var gone in _lastStates.Keys.Where(k => _phone.Find(k) == null).ToList()) _lastStates.Remove(gone);
            }
        };
        _phone.Log += WriteLog;
        LibraryLog.Line += WriteLog;
        _phone.Trace += text => { lock (_logFile) _logFile.WriteLine(text); };
        _phone.TxLostChanged += (c, lost) => Post(new { type = "media-health", id = c.Id, txLost = lost });
        _phone.Quality += (c, q) => Post(new { type = "quality", id = c.Id, q.Dir, q.Kind, q.LossPct, q.Lost, q.JitterMs, q.RttMs });
        _phone.TextReceived += (c, text) =>
        {
            // The log file only, and only how much: what they type stays out of the log.
            lock (_logFile) _logFile.WriteLine($"{DateTime.Now:HH:mm:ss.fff} real-time text in on call {c.Id}: {text.Length} characters");
            Post(new { type = "text", id = c.Id, text });
        };
        _phone.Details += (c, d) => Post(new
        {
            type = "details", id = c.Id, d.Direction, d.Account, d.Signalling, d.TheirPhone, d.CallId, d.TheirMedia,
            d.MediaEncrypted, d.SignallingEncrypted, d.Audio, d.Video,
        });
        _phone.VideoRefused += c => Post(new { type = "video-refused", id = c.Id });
        _phone.MediaOffer += (c, sdp) =>
        {
            var msg = new { type = "media-offer", id = c.Id, sdp };
            if (_pageReady) Post(msg);
            else lock (_pendingOffers) _pendingOffers.Add(msg);
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
                    lock (_pendingOffers) { _pendingOffers.ForEach(Post); _pendingOffers.Clear(); }
                    if (!string.IsNullOrWhiteSpace(_settings.Dial)) DialAtStart();
                    break;
                case "log":
                    lock (_logFile) _logFile.WriteLine($"{DateTime.Now:HH:mm:ss.fff} page: {msg["text"]}");
                    break;
                case "dial":
                    _phone.Dial((string)msg["address"]!, (bool?)msg["video"] ?? false, _accounts.SelectedId);
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
                    _phone.AddVideo(Id(msg));
                    break;
                case "text":
                    _phone.SendText(Id(msg), (string)msg["text"]!);
                    break;
                case "add-text":
                    _phone.AddText(Id(msg));
                    break;
                case "answer":
                    await _phone.AnswerAsync(Id(msg));
                    break;
                case "decline":
                    _phone.Decline(Id(msg));
                    break;
                case "hangup":
                    _phone.Hangup(Id(msg));
                    break;
                case "hold":
                    _phone.Hold(Id(msg), (bool?)msg["hold"] ?? true);
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
                    _phone.SetPageAnswer(Id(msg), (string)msg["sdp"]!);
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
            Transport = ((string?)a["transport"] ?? SipAccount.DefaultTransport).Trim().ToLowerInvariant(),
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
        if (!SipAccount.Transports.Contains(account.Transport))
        {
            Post(new { type = "account-error", text = $"The transport has to be one of {string.Join(", ", SipAccount.Transports.Select(t => t.ToUpperInvariant()))}." });
            return;
        }
        // A server typed as sip:host;transport=tcp says which transport it means.
        account.TakeTransportFromServer();
        var saved = _accounts.Save(account);
        WriteLog($"{saved.Label}: account {(existing == null ? "added" : "saved")}");
        _phone.SetAccounts(_accounts.All);
        Post(new { type = "account-saved", id = saved.Id });
        Post(StateMessage());
    }

    /// <summary>The call a page message is about.</summary>
    private static string Id(JsonObject msg) => (string?)msg["id"] ?? "";

    /// <summary>--dial (testing): places its call, and remembers it for --dial-count.</summary>
    private void DialAtStart() => _dialCallId = _phone.Dial(_settings.Dial!, _settings.VideoCall, _accounts.SelectedId)?.Id;

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
        var calls = _phone.Calls;
        var me = calls.LastOrDefault(c => c.Live && c.Account != null)?.Account ?? selected;
        var regs = _phone.Registrations.ToDictionary(r => r.Account.Id);
        return new
        {
            type = "state",
            // Every call, oldest first: calling, ringing, connected, or ended (for a few seconds).
            calls = calls.Select(c => new
            {
                id = c.Id,
                call = c.State.ToString().ToLowerInvariant(),
                remote = c.Remote,
                reason = c.EndReason,
                outgoing = c.Outgoing,
                incomingVideo = c.IncomingVideo,
                held = c.Held,
                heldByThem = c.HeldByThem,
                // Real-time text on the call: off, adding, on, refused or disabled.
                text = c.TextState,
            }).ToList(),
            maxCalls = Phone.MaxCalls,
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
                a.Id, name = a.Label, a.DisplayName, a.Username, a.Server, a.Transport, a.AuthUsername, hasPassword = a.Password.Length > 0,
                a.Register, a.Expiry, a.Enabled,
                reg = regs.TryGetValue(a.Id, out var r) ? r.State : "off",
                regDetail = regs.TryGetValue(a.Id, out var rd) ? rd.Detail : null,
            }).ToList(),
            videoAllowed = _settings.OfferVideo,
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
