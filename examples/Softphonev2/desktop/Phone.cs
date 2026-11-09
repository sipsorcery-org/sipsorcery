using System.Net;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Softphone;

/// <summary>
/// The SIP side of the prototype: one transport, a registration for each account that registers
/// (see <see cref="SetAccounts"/>), and at most one call.
/// Everything the page needs to show is reported through <see cref="Changed"/> as a small state
/// object; the page's WebRTC side is handed over to <see cref="MediaRelay"/> once a call connects.
/// A second incoming call while one is in progress is answered 486 Busy Here.
/// </summary>
public sealed class Phone : IDisposable
{
    public enum CallState { Idle, Calling, Ringing, Connected, Ended }

    private readonly Settings _settings;
    private readonly SIPTransport _transport = new();
    /// <summary>The accounts, by id, each with its registration (if it registers).</summary>
    private readonly Dictionary<string, AccountRegistration> _accounts = new();
    /// <summary>The account the current call is on: the one it was placed from, or the one it was made to.</summary>
    private SipAccount? _callAccount;
    public SipAccount? CallAccount => _callAccount;
    private SIPUserAgent? _ua;
    private SIPServerUserAgent? _incoming;
    private RTPSession? _rtp;
    private MediaRelay? _relay;

    /// <summary>Simulated loss on what we send, in percent (diagnostics); carries over to new calls and relays.</summary>
    public int AudioLossPercent { get; private set; }
    public int VideoLossPercent { get; private set; }

    public void SetLoss(int audio, int video)
    {
        AudioLossPercent = Math.Clamp(audio, 0, 100);
        VideoLossPercent = Math.Clamp(video, 0, 100);
        if (_relay is { } relay) { relay.AudioLossPercent = AudioLossPercent; relay.VideoLossPercent = VideoLossPercent; }
        Log?.Invoke($"simulated loss: audio {AudioLossPercent}%, video {VideoLossPercent}%");
    }
    private readonly object _lock = new();

    public CallState State { get; private set; } = CallState.Idle;
    public string Remote { get; private set; } = "";

    /// <summary>
    /// Whether the ringing call offers video we'd take: an m=video line that isn't turned down
    /// (port 0), and video allowed here. An INVITE with no SDP counts as yes, as we'd make the
    /// offer, with video in it.
    /// </summary>
    public bool IncomingVideo { get; private set; }
    public string? EndReason { get; private set; }
    private SIPUserAgent? _hangingUp;
    private SIPServerUserAgent? _answering;   // the call we're hanging up ourselves

    /// <summary>
    /// An account and its registration. State: off (disabled, or doesn't register), registering,
    /// registered or failed, with <see cref="Detail"/> saying why it failed in plain words.
    /// </summary>
    public sealed class AccountRegistration
    {
        public required SipAccount Account { get; set; }
        public SIPRegistrationUserAgent? Agent { get; set; }
        /// <summary>The user and server the agent registers, to tell whether a new one replaces its binding.</summary>
        public string? RegisteredAs { get; set; }
        public string State { get; set; } = "off";
        public string? Detail { get; set; }
    }

    /// <summary>A snapshot of every account's registration, for the page.</summary>
    public IReadOnlyList<(SipAccount Account, string State, string? Detail)> Registrations
    {
        get { lock (_accounts) return _accounts.Values.Select(r => (r.Account, r.State, r.Detail)).ToList(); }
    }

    public (string State, string? Detail) RegistrationOf(string? id)
    {
        lock (_accounts) return id != null && _accounts.TryGetValue(id, out var r) ? (r.State, r.Detail) : ("none", null);
    }

    /// <summary>Raised on a SIP thread whenever anything the page shows changes.</summary>
    public event Action? Changed;
    /// <summary>A connected call's relay needs the page's answer to this offer.</summary>
    public event Action<string>? MediaOffer;
    public event Action<string>? Log;
    /// <summary>The far end's RTCP says our audio is (true) or is again not (false) failing to reach them.</summary>
    public event Action<bool>? TxLostChanged;
    /// <summary>An RTCP reception report on the SIP leg: theirs about what we send, or ours about their video.</summary>
    public event Action<MediaRelay.QualitySample>? Quality;

    /// <summary>
    /// The facts about a connected call for the page's Call details panel: how it was set up,
    /// who answered it with what, and the codecs agreed on the SIP side. Raised when the call
    /// connects, and again if its media changes (video added).
    /// </summary>
    public sealed record CallDetails(
        string Direction, string? Account, string? Signalling, string? TheirPhone, string? CallId,
        string? TheirMedia, bool MediaEncrypted, bool SignallingEncrypted, string? Audio, string? Video);
    public event Action<CallDetails>? Details;

    // ---- Real-time text, handled here in C# ----
    // On plain RTP calls: an m=text stream, T.140 with RED redundancy (RFC 4103). On WebRTC calls:
    // a T.140 data channel (RFC 8865, see TextPeerConnection). Either way, text isn't relayed to
    // the page's peer connection: the page sends what's typed as host messages and gets what
    // arrives the same way.

    /// <summary>
    /// Text on the call: off (not set up), adding (re-INVITE out, or a WebRTC call's T.140 data
    /// channel opening), on, refused, or disabled (--no-text).
    /// </summary>
    public string TextState { get; private set; } = "off";
    /// <summary>Characters from them, as they arrive: may include backspace (U+0008) and line separators.</summary>
    public event Action<string>? TextReceived;
    private RttEndPoint? _rtt;
    private System.Threading.Timer? _addTextTimer;
    private bool _outgoing;
    private string? _theirPhone;
    private string? _incomingOffer;   // the ringing call's SDP, if its INVITE had one

    // ---- Browser phones: SIP over WebSocket, with WebRTC media ----

    /// <summary>The call is with a browser phone (SIP over WebSocket), so its media is WebRTC.</summary>
    public bool WebRtc { get; private set; }

    /// <summary>Where browser phones connect, e.g. ws://192.168.1.20:8081, or null if WebSocket is off.</summary>
    public string? WebSocketAddress { get; private set; }
    /// <summary>Full SIP messages, for the log file only.</summary>
    public event Action<string>? Trace;

    public Phone(Settings settings)
    {
        _settings = settings;
        AudioLossPercent = settings.AudioLossPercent;
        VideoLossPercent = settings.VideoLossPercent;
        OpenChannels(settings.SipPort);
        _transport.SIPTransportRequestReceived += OnRequest;
        _transport.SIPRequestOutTraceEvent += (l, r, req) => { Log?.Invoke($"-> {r} {req.StatusLine}"); Trace?.Invoke($"-> {r}\n{req}"); };
        _transport.SIPRequestInTraceEvent += (l, r, req) => { Log?.Invoke($"<- {r} {req.StatusLine}"); Trace?.Invoke($"<- {r}\n{req}"); };
        _transport.SIPResponseInTraceEvent += (l, r, resp) => { Log?.Invoke($"<- {r} {resp.ShortDescription}"); Trace?.Invoke($"<- {r}\n{resp}"); };
        _transport.SIPResponseOutTraceEvent += (l, r, resp) => { Log?.Invoke($"-> {r} {resp.ShortDescription}"); Trace?.Invoke($"-> {r}\n{resp}"); };
    }

    /// <summary>Set when the configured SIP port was taken and another one was used instead.</summary>
    public string? PortNote { get; private set; }

    /// <summary>
    /// Where to call this softphone directly: the address this PC uses to reach the internet
    /// (which also works from this PC) and the UDP SIP port, e.g. sip:192.168.1.20:5070. The
    /// loopback address if there's no network.
    /// </summary>
    public string DirectAddress { get; private set; } = "";

    /// <summary>
    /// Listens for SIP on UDP and TCP: on the configured port if it's free (another softphone
    /// often has 5060), otherwise on a port the OS picks, as RTPChannel does. Incoming direct
    /// calls then need the new port, but registered and outgoing calls work as normal.
    ///
    /// UDP and TCP use the same port when they can, but don't have to: each channel puts its own
    /// port in Via and Contact. Insisting on one port made the OS's pick fail for TCP whenever it
    /// landed in a block Windows reserves for TCP (Hyper-V and WSL; see
    /// `netsh int ipv4 show excludedportrange protocol=tcp`). Retries were no help, as Windows
    /// hands out UDP ports sequentially, so they all landed in the same block.
    /// </summary>
    private void OpenChannels(int port)
    {
        var udp = TryChannel(() => new SIPUDPChannel(new IPEndPoint(IPAddress.Any, port)))
            ?? new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
        var tcp = TryChannel(() => new SIPTCPChannel(new IPEndPoint(IPAddress.Any, udp.Port)))
            ?? new SIPTCPChannel(new IPEndPoint(IPAddress.Any, 0));
        _transport.AddSIPChannel(udp);
        _transport.AddSIPChannel(tcp);
        DirectAddress = $"sip:{SIPSorcery.Sys.NetServices.InternetDefaultAddress ?? IPAddress.Loopback}:{udp.Port}";

        // SIP over WebSocket for browser phones, on its own port. Plain ws: browsers allow it from
        // a page on localhost; another machine needs wss (not yet).
        if (_settings.WsPort > 0)
        {
            try
            {
                var ws = new SIPWebSocketChannel(IPAddress.Any, _settings.WsPort);
                _transport.AddSIPChannel(ws);
                WebSocketAddress = $"ws://{SIPSorcery.Sys.NetServices.InternetDefaultAddress ?? IPAddress.Loopback}:{_settings.WsPort}";
            }
            catch (Exception ex)
            {
                PortNote = (PortNote == null ? "" : PortNote + "; ") + $"SIP WebSocket port {_settings.WsPort} isn't available ({ex.Message})";
            }
        }

        if (udp.Port != port || tcp.Port != port)
        {
            PortNote = udp.Port == tcp.Port ? $"SIP port {port} isn't available, using {udp.Port} instead"
                : udp.Port == port ? $"SIP port {port} isn't available for TCP, using {tcp.Port} for TCP"
                : $"SIP port {port} isn't available, using UDP {udp.Port} and TCP {tcp.Port} instead";
        }
    }

    /// <summary>Creates a channel, or returns null if its port can't be bound.</summary>
    private static T? TryChannel<T>(Func<T> create) where T : SIPChannel
    {
        try { return create(); }
        catch (Exception ex) when (ex is ApplicationException or System.Net.Sockets.SocketException) { return null; }
    }

    public string ListeningOn =>string.Join(", ", _transport.GetSIPChannels().Select(c => c.ListeningSIPEndPoint.ToString()));

    /// <summary>
    /// Brings the registrations in line with the accounts: registers new and enabled ones, removes
    /// (REGISTER Expires: 0) removed and disabled ones, and registers again any whose server,
    /// credentials or expiry changed. Accounts that only changed their name are left registered.
    /// </summary>
    public void SetAccounts(IReadOnlyList<SipAccount> accounts)
    {
        lock (_accounts)
        {
            foreach (var id in _accounts.Keys.Where(id => accounts.All(a => a.Id != id)).ToList())
            {
                Log?.Invoke($"{_accounts[id].Account.Label}: removed");
                StopRegistration(_accounts[id]);
                _accounts.Remove(id);
            }
            foreach (var account in accounts)
            {
                if (_accounts.TryGetValue(account.Id, out var reg))
                {
                    bool changed = reg.Account.RegistrationKey != account.RegistrationKey;
                    reg.Account = account.Clone();
                    if (changed) StartRegistration(reg, "settings changed");
                }
                else
                {
                    reg = new AccountRegistration { Account = account.Clone() };
                    _accounts[account.Id] = reg;
                    StartRegistration(reg, null);
                }
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Registers the account again now (Re-register), for one that failed or to check it.</summary>
    public void Reregister(string id)
    {
        lock (_accounts)
        {
            if (_accounts.TryGetValue(id, out var reg)) StartRegistration(reg, "re-register");
        }
        Changed?.Invoke();
    }

    /// <summary>(Re)starts an account's registration, or stops it if the account shouldn't register.</summary>
    private void StartRegistration(AccountRegistration reg, string? why)
    {
        var a = reg.Account;
        // Registering the same address again (Re-register, a new password) replaces the binding,
        // so the old agent leaves it be: its Expires: 0 could arrive after the new REGISTER and
        // remove it. A different address, or none, is unregistered.
        bool same = a.Enabled && a.Register && reg.RegisteredAs == $"{a.Username}\n{a.Server}";
        StopRegistration(reg, unregister: !same);
        if (!a.Enabled || !a.Register)
        {
            SetRegistration(reg, "off", !a.Enabled ? "Disabled" : "Doesn't register: calls out only");
            return;
        }
        if (string.IsNullOrWhiteSpace(a.Username) || string.IsNullOrWhiteSpace(a.Server))
        {
            SetRegistration(reg, "failed", "Needs a username and a server");
            return;
        }

        var server = a.Server.Trim();
        if (!server.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) && !server.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) server = "sip:" + server;
        if (!SIPURI.TryParse(server, out var serverUri) || serverUri == null)
        {
            SetRegistration(reg, "failed", $"\"{a.Server}\" isn't a server address");
            return;
        }
        var aor = serverUri.CopyOf();
        aor.User = a.Username;
        var agent = new SIPRegistrationUserAgent(_transport, null, aor, a.AuthUser, a.Password, null, a.Server,
            new SIPURI(aor.Scheme, IPAddress.Any, 0), a.Expiry, null);
        reg.Agent = agent;
        reg.RegisteredAs = $"{a.Username}\n{a.Server}";
        // Events from an agent that's since been replaced or stopped are ignored: its final
        // unregister would otherwise show over the new one's state.
        agent.RegistrationSuccessful += (_, _) => { if (reg.Agent == agent) SetRegistration(reg, "registered", null); };
        agent.RegistrationTemporaryFailure += (_, resp, err) => { if (reg.Agent == agent) SetRegistration(reg, "failed", Explain(a, resp, err) + ". Retrying…"); };
        agent.RegistrationFailed += (_, resp, err) => { if (reg.Agent == agent) SetRegistration(reg, "failed", Explain(a, resp, err)); };
        agent.RegistrationRemoved += (_, _) => { if (reg.Agent == agent) SetRegistration(reg, "off", null); };
        if (why != null) Log?.Invoke($"{a.Label}: registering {a.Aor} ({why})");
        SetRegistration(reg, "registering", null);
        agent.Start();
    }

    private static void StopRegistration(AccountRegistration reg, bool unregister = true)
    {
        var agent = reg.Agent;
        reg.Agent = null;
        reg.RegisteredAs = null;
        // Unregisters (Expires: 0) if it was registered.
        agent?.Stop(unregister);
    }

    /// <summary>A registration failure in plain words, with the server's own response after it.</summary>
    private static string Explain(SipAccount a, SIPResponse? resp, string err)
    {
        if (resp != null)
        {
            var said = $"({resp.StatusCode} {resp.ReasonPhrase})";
            return resp.StatusCode switch
            {
                401 or 403 or 407 => $"Wrong username or password {said}",
                404 => $"{a.Host} doesn't know the user {a.Username} {said}",
                423 => $"The server wants a longer expiry {said}",
                >= 500 => $"The server had a problem {said}",
                _ => $"The server said {resp.StatusCode} {resp.ReasonPhrase}",
            };
        }
        if (err.Contains("Could not resolve", StringComparison.OrdinalIgnoreCase)) return $"Can't find the server {a.Host} (DNS lookup failed)";
        if (err.Contains("Timeout", StringComparison.OrdinalIgnoreCase) || err.Contains("maximum number", StringComparison.OrdinalIgnoreCase))
            return $"No answer from {a.Host}";
        return err.TrimEnd('.');
    }

    private SipAccount? AccountById(string? id)
    {
        lock (_accounts) return id != null && _accounts.TryGetValue(id, out var r) ? r.Account.Clone() : null;
    }

    /// <summary>
    /// The account an incoming call was made to: the one whose user and domain are in its To
    /// header (what a registrar forwards to us), or null for a direct call.
    /// </summary>
    private SipAccount? AccountForIncoming(SIPRequest req)
    {
        var to = req.Header.To?.ToURI;
        if (to == null) return null;
        lock (_accounts)
        {
            var enabled = _accounts.Values.Select(r => r.Account).Where(a => a.Enabled).ToList();
            return (enabled.FirstOrDefault(a => string.Equals(a.Username, to.User, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Domain, to.HostAddress, StringComparison.OrdinalIgnoreCase))
                ?? enabled.FirstOrDefault(a => string.Equals(a.Username, to.User, StringComparison.OrdinalIgnoreCase)))
                ?.Clone();
        }
    }

    /// <summary>
    /// Turns what was typed into a SIP URI: a full sip: URI as is, user@host as a sip: URI, and a
    /// bare number or name as a user at the account's server.
    /// </summary>
    public static string ResolveAddress(string typed, SipAccount? account)
    {
        typed = typed.Trim();
        if (typed.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) || typed.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) return typed;
        if (typed.Contains('@')) return "sip:" + typed;
        if (account != null) return $"sip:{typed}@{account.Host}";
        return "sip:" + typed;   // e.g. an IP address or host name
    }

    /// <summary>
    /// Places a call: a video call offers audio and video, an audio call leaves video out (it can
    /// be added later). If they turn the offer down because of its media profile (see
    /// <see cref="WantsOtherMedia"/>), it's placed once more straight away with the other kind:
    /// WebRTC instead of plain RTP, or the other way round.
    /// </summary>
    public async Task DialAsync(string typed, bool video, string? accountId)
    {
        SIPUserAgent ua;
        RTPSession rtp;
        lock (_lock)
        {
            if (State is CallState.Calling or CallState.Ringing or CallState.Connected) return;
            // The chosen account, if it's enabled; with none, a direct call with no credentials.
            _callAccount = AccountById(accountId) is { Enabled: true } acct ? acct : null;
            Remote = ResolveAddress(typed, _callAccount);
            // WebRTC media for an address that says transport=ws (a browser phone), or for every
            // call with --webrtc. Otherwise plain RTP, and a browser behind a server says so with
            // a 488 (see WantsOtherMedia).
            WebRtc = IsWebSocketUri(Remote) || _settings.WebRtcCalls;
            ua = NewUserAgent();
            rtp = NewSession(video && _settings.OfferVideo);
            (_ua, _rtp) = (ua, rtp);
            SetState(CallState.Calling);
        }
        _outgoing = true;

        for (int attempt = 1; ; attempt++)
        {
            var (ok, failure, response) = await CallOnceAsync(ua, rtp);
            if (ok) { await ConnectedAsync(rtp); return; }

            lock (_lock)
            {
                // Still ours to retry: nobody hung up or started another call meanwhile.
                if (attempt == 1 && _ua == ua && State == CallState.Calling && WantsOtherMedia(response, WebRtc))
                {
                    WebRtc = !WebRtc;
                    Log?.Invoke($"they can't take {(WebRtc ? "plain RTP" : "WebRTC")} media (488, {response!.Header.Warning ?? (string.IsNullOrWhiteSpace(response.Body) ? "no reason given" : "their SDP")}); calling again with {(WebRtc ? "WebRTC" : "plain RTP")}");
                    rtp.Close("retrying with other media");
                    ua = NewUserAgent();
                    rtp = NewSession(video && _settings.OfferVideo);
                    (_ua, _rtp) = (ua, rtp);
                    continue;
                }
            }
            if (_ua == ua) End(failure ?? "Call failed");
            return;
        }
    }

    /// <summary>One INVITE: whether it was answered, and if not, why and the response that said so.</summary>
    private async Task<(bool Ok, string? Failure, SIPResponse? Response)> CallOnceAsync(SIPUserAgent ua, RTPSession rtp)
    {
        string? failure = null;
        SIPResponse? response = null;
        // A failure with a 2xx is their answer turned down here: media we can't use (e.g. plain RTP
        // to our WebRTC offer, from a SIP phone with --webrtc).
        // When it started ringing: a failure after that, other than a refusal, is the far end (or a
        // server on the way) giving up on an unanswered call, whatever its reason phrase says.
        DateTime? ringing = null;
        ua.ClientCallRinging += (_, _) => ringing ??= DateTime.Now;
        ua.ClientCallFailed += (_, err, resp) =>
        {
            response = resp;
            string said = resp != null ? $"{resp.StatusCode} {resp.ReasonPhrase}" : "";
            failure = resp is { StatusCode: >= 200 and < 300 } ? "Their answer's media doesn't match ours"
                : resp is { Status: SIPResponseStatusCodesEnum.NotAcceptableHere } ? "They can't take this call's media (488)"
                : resp != null && ringing is { } r && resp.StatusCode is 408 or 480 or 487
                    ? $"Not answered: rang for {(DateTime.Now - r).TotalSeconds:0} s, then the far end said {said}"
                : resp != null ? $"The far end said {said}" : err;
        };
        ua.ClientCallAnswered += (_, resp) => _theirPhone = resp?.Header.Server ?? resp?.Header.UserAgent;
        var a = _callAccount;
        try
        {
            if (a == null) return (await ua.Call(Remote, null, null, rtp), failure, response);
            // From the account's address and display name, with its credentials if challenged.
            var dst = SIPURI.ParseSIPURIRelaxed(Remote);
            var from = new SIPFromHeader(a.DisplayName, SIPURI.ParseSIPURIRelaxed($"sip:{a.Username}@{a.Host}"), null).ToString();
            var descriptor = new SIPCallDescriptor(a.AuthUser, a.Password, dst.ToString(), from, dst.CanonicalAddress,
                null, null, null, SIPCallDirection.Out, SDP.SDP_MIME_CONTENTTYPE, null, null);
            return (await ua.Call(descriptor, rtp), failure, response);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, response);
        }
    }

    /// <summary>
    /// A 488 that says our media profile was the problem, not the codecs, so the other kind of
    /// media might work: Warning 302 "Incompatible transport protocol" (RFC 3261 20.43; codecs
    /// would be 305), or an SDP of what they can take (21.4.26) on the other kind of profile. The
    /// web softphone sends both when it gets a plain RTP offer. A bare 488, with neither (a server
    /// on the way may drop them), is retried too: one more INVITE is all it costs if it was the
    /// codecs. Any other 488 isn't retried.
    /// </summary>
    private static bool WantsOtherMedia(SIPResponse? resp, bool offeredWebRtc)
    {
        if (resp is not { Status: SIPResponseStatusCodesEnum.NotAcceptableHere }) return false;
        var warning = resp.Header.Warning;
        if (warning is { } w && System.Text.RegularExpressions.Regex.IsMatch(w, @"(^|,)\s*302\s")) return true;
        bool sdp = !string.IsNullOrWhiteSpace(resp.Body) && resp.Header.ContentType?.Contains("sdp", StringComparison.OrdinalIgnoreCase) == true;
        if (!sdp) return string.IsNullOrWhiteSpace(warning);
        return IsWebRtcOffer(resp.Body) != offeredWebRtc;
    }

    public async Task AnswerAsync()
    {
        SIPUserAgent? ua;
        SIPServerUserAgent? uas;
        RTPSession rtp;
        lock (_lock)
        {
            // Once: a second answer while the first is under way (a double click) would make its
            // own media session, fail, and end the call.
            if (State != CallState.Ringing || _incoming == null || _answering == _incoming) return;
            _answering = _incoming;
            (ua, uas) = (_ua, _incoming);
            // A WebRTC answer can't add a stream the offer didn't have (RTCPeerConnection would
            // put our video in as an extra m-line, and the browser rejects the answer), so video
            // only if they offered it.
            rtp = NewSession(WebRtc ? IncomingVideo : _settings.OfferVideo, _incomingOffer);
            _rtp = rtp;
        }
        if (ua != null && uas != null && await ua.Answer(uas, rtp)) await ConnectedAsync(rtp);
        else End("Couldn't answer");
    }

    public void Decline()
    {
        lock (_lock)
        {
            if (State != CallState.Ringing) return;
            _incoming?.Reject(SIPResponseStatusCodesEnum.BusyHere, null);
        }
        End("Declined");
    }

    public void Hangup()
    {
        lock (_lock)
        {
            if (State == CallState.Ringing) { Decline(); return; }
            if (State is not (CallState.Calling or CallState.Connected)) return;
            // SIPUserAgent raises OnCallHungup from inside its own Hangup, which would record
            // this as "They hung up". (Ending the call here first instead stops the BYE: the
            // user agent only sends one while the call is still up.)
            _hangingUp = _ua;
            if (State == CallState.Calling) _ua?.Cancel();
            else _ua?.Hangup();
        }
        End("You hung up");
    }

    public void SetPageAnswer(string sdp) => _relay?.SetAnswer(sdp);

    /// <summary>The far end turned down our request to add video (m=video 0, an error, or no answer).</summary>
    public event Action? VideoRefused;
    private System.Threading.Timer? _addVideoTimer;

    /// <summary>
    /// Adds video to an audio call: our video codecs go on the session and a re-INVITE offers
    /// them. If they accept, the relay is rebuilt with video (see <see cref="OnRemoteDescriptionChanged"/>);
    /// if they refuse, or don't answer within 8 s, <see cref="VideoRefused"/> is raised.
    /// </summary>
    public void AddVideo()
    {
        lock (_lock)
        {
            if (State != CallState.Connected || _ua == null || _rtp == null || !_settings.OfferVideo) { VideoRefused?.Invoke(); return; }
            if (MediaRelay.HasNegotiatedVideo(_rtp)) return;
            MediaRelay.AddVideoTrack(_rtp);
            _addVideoTimer?.Dispose();
            _addVideoTimer = new System.Threading.Timer(_ => AddVideoDone(false), null, 8000, Timeout.Infinite);
            Log?.Invoke("adding video: re-INVITE with m=video");
            _ua.AddVideo();
        }
    }

    /// <summary>
    /// The SIP side's media session for a new call: WebRTC for a browser phone, with real-time
    /// text on a data channel, or plain RTP with an m=text stream. <paramref name="theirOffer"/>
    /// is the SDP of a call we're answering, or null if we make the offer.
    /// </summary>
    private RTPSession NewSession(bool video, string? theirOffer = null)
    {
        var rtp = MediaRelay.CreateSipSession(video, WebRtc);
        if (rtp is TextPeerConnection pc)
        {
            _rtt?.Dispose();
            _rtt = null;
            AttachDataChannelText(pc, theirOffer);
            pc.onconnectionstatechange += state =>
            {
                Log?.Invoke($"WebRTC media (ICE, DTLS) {state}");
                if (state == RTCPeerConnectionState.failed && _rtp == pc) { _ua?.Hangup(); End("Media connection failed"); }
                // The far end closed its media without a BYE: a browser phone's page reloaded or
                // closed, say (DTLS close_notify, with no BYE sent). The call would otherwise stay
                // up with no media, and every call to us meanwhile would be turned down as busy.
                // But the media closes on every hang up too, ours and theirs (the user agent closes
                // it before it reports a BYE), so give those a moment to end the call first.
                if (state == RTCPeerConnectionState.closed && _rtp == pc && State == CallState.Connected)
                {
                    var ua = _ua;
                    _ = Task.Delay(1000).ContinueWith(_ =>
                    {
                        if (_rtp != pc || _ua != ua || State != CallState.Connected) return;
                        Log?.Invoke("WebRTC media closed without a BYE: hanging up");
                        _hangingUp = ua;
                        ua?.Hangup();
                        End("Media connection closed");
                    });
                }
            };
        }
        else AttachText(rtp);
        return rtp;
    }

    private static bool IsWebSocketUri(string uri) =>
        SIPURI.TryParse(uri, out var u) && u!.Protocol is SIPProtocolsEnum.ws or SIPProtocolsEnum.wss;

    /// <summary>
    /// Gives a new SIP session a text track (RED with T.140 inside, and plain T.140) and an
    /// <see cref="RttEndPoint"/> to send and receive on it, wired the way VoIPMediaSession does.
    /// It's offered on every call (unless --no-text), and only started once it's agreed.
    /// </summary>
    private void AttachText(RTPSession rtp)
    {
        _rtt?.Dispose();
        _rtt = null;
        TextState = _settings.OfferText ? "off" : "disabled";
        if (!_settings.OfferText) return;
        var rtt = new RttEndPoint();
        var red = rtt.GetTextSourceFormat();
        var formats = new List<TextFormat> { red };
        if (T140RedPayload.TryParseFmtp(red.Parameters, out int t140, out _) && t140 != red.FormatID)
            formats.Add(new TextFormat(TextCodecsEnum.T140, t140));
        rtp.addTrack(new MediaStreamTrack(formats));
        rtt.OnTextSourceEncodedSample += rtp.SendText;
        rtp.OnTextFormatsNegotiated += agreed =>
        {
            var send = agreed.First();
            rtt.SetTextSourceFormat(send);
            // They can send any agreed format; with RED the sink knows both payload types.
            rtt.SetTextSinkFormat(agreed.Where(f => f.Codec == TextCodecsEnum.RED).DefaultIfEmpty(send).First());
        };
        rtp.OnRtpPacketReceived += (from, kind, pkt) =>
        {
            if (kind != SDPMediaTypesEnum.text) return;
            var h = pkt.Header;
            rtt.GotTextRtp(from, h.SyncSource, h.SequenceNumber, h.Timestamp, h.PayloadType, h.MarkerBit, pkt.GetPayloadBytes());
        };
        rtt.OnTextReceived += e => TextReceived?.Invoke(e.Text);
        _rtt = rtt;
    }

    /// <summary>
    /// Real-time text on a WebRTC call: a T.140 data channel (RFC 8865), in our offer on the
    /// stream we pick, or in our answer on the stream their offer has. An offer without one
    /// (a browser phone that doesn't do it) means no text, unless they open one in band.
    /// </summary>
    private void AttachDataChannelText(TextPeerConnection pc, string? theirOffer)
    {
        TextState = _settings.OfferText ? "off" : "disabled";
        if (!_settings.OfferText) return;
        pc.TextReceived += text => { if (_rtp == pc) TextReceived?.Invoke(text); };
        pc.TextOpened += () =>
        {
            if (Interlocked.Exchange(ref _addTextTimer, null) is { } t) t.Dispose();
            if (_rtp != pc || TextState == "on") return;
            // A negotiated channel opens here once SCTP is up, whether or not they have one on
            // that stream: it only counts if their SDP agreed to it. One they opened in band does.
            if (pc.T140?.negotiated == true && TextPeerConnection.FindT140(pc.RemoteDescription) == null) return;
            TextState = "on";
            Log?.Invoke($"real-time text on (RFC 8865, T.140 data channel on stream {pc.T140?.id})");
            Changed?.Invoke();
        };
        ushort? stream = string.IsNullOrWhiteSpace(theirOffer) ? TextPeerConnection.OfferStreamId : TextPeerConnection.FindT140(theirOffer);
        if (stream is ushort id) { pc.AddT140(id); TextState = "adding"; }
        else TextState = "refused";
    }

    /// <summary>
    /// A connected WebRTC call's text: on if the channel's open already; refused if their SDP has
    /// no T.140 channel; otherwise it's opening, and refused if it isn't open within 10 s.
    /// </summary>
    private void StartDataChannelText(TextPeerConnection pc)
    {
        if (TextState != "adding" || pc.TextOpen) return;
        if (TextPeerConnection.FindT140(pc.RemoteDescription) == null)
        {
            TextState = "refused";
            Log?.Invoke("they didn't take text (no T.140 data channel in their SDP)");
            Changed?.Invoke();
            return;
        }
        Log?.Invoke("real-time text agreed: opening the T.140 data channel");
        _addTextTimer?.Dispose();
        _addTextTimer = new System.Threading.Timer(_ =>
        {
            if (Interlocked.Exchange(ref _addTextTimer, null) is not { } t) return;
            t.Dispose();
            if (_rtp != pc || TextState != "adding") return;
            TextState = "refused";
            Log?.Invoke("the T.140 data channel didn't open: no text on this call");
            Changed?.Invoke();
        }, null, 10000, Timeout.Infinite);
    }

    /// <summary>Text is agreed on the session: their SDP has a text line with a port, and it isn't inactive.</summary>
    private static bool HasNegotiatedText(RTPSession rtp) =>
        rtp.RemoteDescription?.Media.Any(m => m.Media == SDPMediaTypesEnum.text && m.Port != 0) == true &&
        rtp.TextStream?.RemoteTrack != null && rtp.TextStream.RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;

    /// <summary>Starts sending and receiving text if it's agreed and not already on.</summary>
    private async Task StartTextIfAgreed(RTPSession rtp)
    {
        if (rtp is TextPeerConnection pc) { StartDataChannelText(pc); return; }
        if (_rtt == null || TextState == "on" || !HasNegotiatedText(rtp)) return;
        await _rtt.Start();
        if (Interlocked.Exchange(ref _addTextTimer, null) is { } t) t.Dispose();
        TextState = "on";
        Log?.Invoke("real-time text on (RFC 4103)");
        Changed?.Invoke();
    }

    /// <summary>Sends what was typed (letters, backspaces, line separators) as real-time text.</summary>
    public void SendText(string text)
    {
        if (TextState != "on") return;
        if (_rtp is TextPeerConnection pc) pc.SendText(text);
        else if (_rtt?.CanSendText == true) _rtt.SendText(text);
    }

    /// <summary>
    /// Adds text to a call that doesn't have it: a re-INVITE with m=text. If they accept, text
    /// starts (see <see cref="OnRemoteDescriptionChanged"/>); if not within 8 s, it's refused.
    /// </summary>
    public void AddText()
    {
        lock (_lock)
        {
            if (State != CallState.Connected || _ua == null || _rtp == null || _rtt == null || TextState is "on" or "adding") return;
            TextState = "adding";
            _addTextTimer?.Dispose();
            _addTextTimer = new System.Threading.Timer(_ =>
            {
                if (Interlocked.Exchange(ref _addTextTimer, null) is not { } t) return;
                t.Dispose();
                if (TextState != "adding") return;
                TextState = "refused";
                Log?.Invoke("they didn't take text");
                Changed?.Invoke();
            }, null, 8000, Timeout.Infinite);
            Log?.Invoke("adding text: re-INVITE with m=text");
            _ua.AddText();
        }
        Changed?.Invoke();
    }

    private void AddVideoDone(bool ok)
    {
        if (Interlocked.Exchange(ref _addVideoTimer, null) is not { } timer) return;
        timer.Dispose();
        if (!ok)
        {
            Log?.Invoke("they didn't take video; the call carries on as audio");
            VideoRefused?.Invoke();
        }
    }

    /// <summary>
    /// Any new SDP from the far end: an answer to our re-INVITE, or their re-INVITE. If it adds
    /// video to a call that didn't have it, the relay is rebuilt so the page gets a video line.
    /// </summary>
    private void OnRemoteDescriptionChanged(RTPSession rtp)
    {
        bool video = MediaRelay.HasNegotiatedVideo(rtp);
        if (_addVideoTimer != null) AddVideoDone(video);
        // Text added by either side: start it once the user agent has finished with the SDP.
        if (_rtp == rtp && TextState != "on" && HasNegotiatedText(rtp))
            _ = Task.Run(async () => { await Task.Delay(200); if (_rtp == rtp) await StartTextIfAgreed(rtp); });
        // Their answer to our adding text turned it down (m=text with port 0): say so now, not at the timeout.
        else if (_rtp == rtp && rtp is not TextPeerConnection && TextState == "adding" && Interlocked.Exchange(ref _addTextTimer, null) is { } t)
        {
            t.Dispose();
            TextState = "refused";
            Log?.Invoke("they turned down text");
            Changed?.Invoke();
        }
        if (!video || _rtp != rtp || _relay == null || _relay.Video.HasValue) return;
        // Let the SIP user agent finish (send its answer, start the stream) before rebuilding.
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            lock (_lock)
            {
                if (_rtp != rtp || State != CallState.Connected) return;
                _relay?.Dispose();
                _relay = null;
            }
            Log?.Invoke("video added to the call");
            await StartRelayAsync(rtp);
        });
    }

    private async Task ConnectedAsync(RTPSession rtp)
    {
        rtp.OnRemoteDescriptionChanged += _ => OnRemoteDescriptionChanged(rtp);
        SetState(CallState.Connected);
        await StartTextIfAgreed(rtp);
        await StartRelayAsync(rtp);
    }

    private async Task StartRelayAsync(RTPSession rtp)
    {
        var relay = new MediaRelay(rtp, s => Log?.Invoke(s)) { AudioLossPercent = AudioLossPercent, VideoLossPercent = VideoLossPercent };
        relay.TxLostChanged += lost => TxLostChanged?.Invoke(lost);
        relay.Quality += sample => Quality?.Invoke(sample);
        _relay = relay;
        Log?.Invoke($"call connected: {relay.Describe()}");
        Details?.Invoke(DescribeCall(rtp, relay));
        MediaOffer?.Invoke(await relay.CreateOfferAsync());
    }

    private CallDetails DescribeCall(RTPSession rtp, MediaRelay relay)
    {
        var dialogue = _ua?.Dialogue;
        var ep = dialogue?.RemoteSIPEndPoint;
        var a = _callAccount;
        var media = rtp.RemoteDescription?.Media.Where(m => m.Port != 0).ToList() ?? new();
        static string? Codec(SDPAudioVideoMediaFormat? f) =>
            f is { } x ? $"{x.Name()} {(x.ClockRate() % 1000 == 0 ? $"{x.ClockRate() / 1000} kHz" : $"{x.ClockRate()} Hz")}" : null;
        return new CallDetails(
            Direction: _outgoing ? "outgoing" : "incoming",
            Account: a?.Aor,
            Signalling: ep == null ? null : $"{ep.Protocol.ToString().ToUpperInvariant()} {ep.Address}:{ep.Port}",
            TheirPhone: string.IsNullOrWhiteSpace(_theirPhone) ? null : _theirPhone,
            CallId: dialogue?.CallId,
            TheirMedia: rtp.HasAudio ? rtp.AudioStream.DestinationEndPoint?.ToString() : null,
            // SRTP shows as RTP/SAVP (or UDP/TLS/RTP/SAVP) on their media lines; SIP over TLS as tls.
            MediaEncrypted: media.Count > 0 && media.All(m => m.Transport.Contains("SAVP", StringComparison.OrdinalIgnoreCase)),
            SignallingEncrypted: ep?.Protocol is SIPProtocolsEnum.tls or SIPProtocolsEnum.wss,
            Audio: Codec(relay.Audio),
            Video: relay.Video is { } v ? v.Name() : null);
    }

    private async Task OnRequest(SIPEndPoint local, SIPEndPoint remote, SIPRequest req)
    {
        // Not a registrar: browser phones register with a SIP server (sipsorcery.com) and can
        // call this app directly over its WebSocket without registering.
        if (req.Method == SIPMethodsEnum.REGISTER)
        {
            await _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.MethodNotAllowed, "Not a registrar"));
            return;
        }
        if (req.Method != SIPMethodsEnum.INVITE || !string.IsNullOrEmpty(req.Header.To.ToTag)) return;

        lock (_lock)
        {
            if (State is CallState.Calling or CallState.Ringing or CallState.Connected)
            {
                _ = _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BusyHere, null));
                return;
            }
            // A browser phone's Contact is an address nothing can reach (sip:x@y.invalid;transport=ws,
            // RFC 7118): our requests in the call (BYE, re-INVITEs) only get to it over the
            // WebSocket connection its INVITE came on. As the outbound proxy, they go there.
            var ua = NewUserAgent(remote.Protocol is SIPProtocolsEnum.ws or SIPProtocolsEnum.wss ? remote : null);
            _ua = ua;
            _incoming = ua.AcceptCall(req);
            ua.ServerCallCancelled += (_, _) => End("Missed call");
            Remote = req.Header.From.FromURI.ToString();
            _outgoing = false;
            _callAccount = AccountForIncoming(req);
            _theirPhone = req.Header.UserAgent;
            _incomingOffer = req.Body;
            // A call from a browser phone is answered with WebRTC media: one over WebSocket, or
            // one whose offer is WebRTC's (it came through a server, so over UDP, TCP or TLS).
            WebRtc = remote.Protocol is SIPProtocolsEnum.ws or SIPProtocolsEnum.wss || IsWebRtcOffer(req.Body);
            var name = req.Header.From.FromName;
            if (!string.IsNullOrWhiteSpace(name)) Remote = $"{name} <{Remote}>";
            IncomingVideo = _settings.OfferVideo && OffersVideo(req.Body);
            SetState(CallState.Ringing);
        }
        _incoming!.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
        await Task.CompletedTask;
    }

    /// <summary>
    /// The offer is a WebRTC one: its media is DTLS-SRTP (UDP/TLS/RTP/SAVPF from browsers,
    /// UDP/TLS/RTP/SAVP from SIPSorcery's RTCPeerConnection). A plain SIP phone offers RTP/AVP, or
    /// RTP/SAVP with SDES keys.
    /// </summary>
    private static bool IsWebRtcOffer(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp)) return false;
        try { return SDP.ParseSDPDescription(sdp).Media.Any(m => m.Port != 0 && m.Transport.Contains("TLS/RTP/SAVP", StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    private static bool OffersVideo(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp)) return true;
        try { return SDP.ParseSDPDescription(sdp).Media.Any(m => m.Media == SDPMediaTypesEnum.video && m.Port != 0); }
        catch { return true; }
    }

    /// <summary>A user agent for a call, sending its requests via <paramref name="outboundProxy"/> if given.</summary>
    private SIPUserAgent NewUserAgent(SIPEndPoint? outboundProxy = null)
    {
        var ua = new SIPUserAgent(_transport, outboundProxy);
        // Only the current, connected call's. The user agent raises this too when a call we place
        // fails with WebRTC media (closing the RTCPeerConnection), and for a turned-down attempt
        // DialAsync retries; there, DialAsync gives the real reason.
        ua.OnCallHungup += _ => { if (_hangingUp != ua && _ua == ua && State == CallState.Connected) End("They hung up"); };
        return ua;
    }

    private void End(string reason)
    {
        lock (_lock)
        {
            if (State is CallState.Idle or CallState.Ended) return;
            EndReason = reason;
            Interlocked.Exchange(ref _addVideoTimer, null)?.Dispose();
            Interlocked.Exchange(ref _addTextTimer, null)?.Dispose();
            _rtt?.Dispose();
            _rtt = null;
            TextState = _settings.OfferText ? "off" : "disabled";
            WebRtc = false;
            _relay?.Dispose();
            _relay = null;
            try { _rtp?.Close(reason); } catch { }
            _rtp = null;
            _ua = null;
            _incoming = null;
            SetState(CallState.Ended);
        }
    }

    private void SetState(CallState state)
    {
        State = state;
        if (state != CallState.Ended) EndReason = null;
        Log?.Invoke($"call {state.ToString().ToLowerInvariant()} {Remote} {EndReason}".TrimEnd());
        Changed?.Invoke();
    }

    private void SetRegistration(AccountRegistration reg, string state, string? detail)
    {
        if (reg.State == state && reg.Detail == detail) return;
        reg.State = state;
        reg.Detail = detail;
        Log?.Invoke($"{reg.Account.Label}: registration {state} {detail}".TrimEnd());
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (State == CallState.Connected) _ua?.Hangup();
        lock (_accounts) foreach (var reg in _accounts.Values) StopRegistration(reg);
        Thread.Sleep(300);
        _transport.Shutdown();
    }
}
