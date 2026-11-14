using System.Net;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Softphone;

/// <summary>
/// The SIP side of the prototype: one transport, a registration for each account that registers
/// (see <see cref="SetAccounts"/>), and up to <see cref="MaxCalls"/> calls at once, each a
/// <see cref="Call"/> with its own user agent, media session and relay to the page.
/// Everything the page needs to show is reported through <see cref="Changed"/> as a small state
/// object; each call's WebRTC side is handed over to its own <see cref="MediaRelay"/> once it
/// connects. Which call has the microphone and speaker is the page's business: it only sends
/// its microphone on one call at a time. Hold is a SIP re-INVITE (<see cref="Hold"/>).
/// An incoming call rings alongside the calls in progress; only one past <see cref="MaxCalls"/>
/// is answered 486 Busy Here.
/// </summary>
public sealed class Phone : IDisposable
{
    public enum CallState { Idle, Calling, Ringing, Connected, Ended }

    /// <summary>The most calls at once, as in the UX prototype's Active calls list.</summary>
    public const int MaxCalls = 8;
    /// <summary>How long an ended call stays in <see cref="Calls"/>, so the page can say how it ended.</summary>
    private const int EndedKeepMs = 4000;

    /// <summary>One call: its SIP side, media session, relay to the page and text.</summary>
    public sealed class Call
    {
        public required string Id { get; init; }
        public CallState State { get; internal set; } = CallState.Idle;
        public string Remote { get; internal set; } = "";
        /// <summary>
        /// Whether the ringing call offers video we'd take: an m=video line that isn't turned down
        /// (port 0), and video allowed here. An INVITE with no SDP counts as yes, as we'd make the
        /// offer, with video in it.
        /// </summary>
        public bool IncomingVideo { get; internal set; }
        public string? EndReason { get; internal set; }
        /// <summary>We placed it (rather than answered it).</summary>
        public bool Outgoing { get; internal set; }
        /// <summary>The account it's on: the one it was placed from, or the one it was made to.</summary>
        public SipAccount? Account { get; internal set; }
        /// <summary>We put it on hold (a re-INVITE with sendonly).</summary>
        public bool Held { get; internal set; }
        /// <summary>They put us on hold.</summary>
        public bool HeldByThem { get; internal set; }
        /// <summary>
        /// Text on the call: off (not set up), adding (re-INVITE out, or a WebRTC call's T.140 data
        /// channel opening), on, refused, or disabled (--no-text).
        /// </summary>
        public string TextState { get; internal set; } = "off";
        /// <summary>The call is with a browser phone (SIP over WebSocket), so its media is WebRTC.</summary>
        public bool WebRtc { get; internal set; }
        /// <summary>
        /// Our plain RTP offer requires SRTP (RTP/SAVP with SDES keys), rather than offering the keys
        /// on RTP/AVP: tried when a phone that requires SRTP turns the RTP/AVP offer down.
        /// </summary>
        internal bool RequireSrtp { get; set; }
        /// <summary>The media was set for the call (a contact's), so a refusal isn't retried with another kind.</summary>
        internal bool MediaSet { get; set; }
        /// <summary>When it was placed, or started ringing (or, after a transfer, joined to whoever it's with now).</summary>
        public DateTime StartedAt { get; internal set; } = DateTime.Now;
        /// <summary>When it was answered; null if it never was.</summary>
        public DateTime? ConnectedAt { get; internal set; }
        /// <summary>When it ended.</summary>
        public DateTime? EndedAt { get; internal set; }
        /// <summary>The final response that turned down a call we placed, e.g. "486 Busy Here".</summary>
        public string? FailureResponse { get; internal set; }
        /// <summary>We hung it up (or cancelled it) ourselves.</summary>
        public bool EndedByUs => HangingUp;

        internal SIPUserAgent? Ua;
        internal SIPServerUserAgent? Incoming;
        internal RTPSession? Rtp;
        internal MediaRelay? Relay;
        internal RttEndPoint? Rtt;
        internal System.Threading.Timer? AddTextTimer;
        internal System.Threading.Timer? AddVideoTimer;
        /// <summary>A hold or resume re-INVITE is out, waiting for its answer (or this timing out).</summary>
        internal System.Threading.Timer? HoldTimer;
        /// <summary>How the call is held, from its account as it was when the call started (see <see cref="SipAccount.Hold"/>).</summary>
        public string HoldMode => Account?.Hold ?? SipAccount.DefaultHold;
        /// <summary>A hold or resume re-INVITE is out.</summary>
        public bool HoldBusy => HoldTimer != null;
        /// <summary>What the user last asked for: held or not. Sent once the re-INVITE that's out is done.</summary>
        internal bool HoldWanted;
        internal string? TheirPhone;
        internal string? IncomingOffer;   // the ringing call's SDP, if its INVITE had one
        internal bool HangingUp;          // we're hanging it up ourselves
        internal bool Answering;          // Answer is under way (a double click mustn't answer twice)
        /// <summary>The SSRC each kind of media last arrived with on the SIP side.</summary>
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<SDPMediaTypesEnum, uint> Sources = new();
        public bool Live => State is CallState.Calling or CallState.Ringing or CallState.Connected;
        /// <summary>A transfer of this call, ours or theirs, while it's under way and for a moment after; null if none.</summary>
        public TransferInfo? Transfer { get; internal set; }
        /// <summary>How a transferred call ended, for the history: "Transferred to Dave".</summary>
        public string? TransferNote { get; internal set; }
        /// <summary>Our transfer you cancelled, to whom: if their phone goes ahead anyway and hangs up on us, that's why.</summary>
        internal string? CancelledTransferTo;
    }

    /// <summary>
    /// A transfer on a call. Role: "out" is ours (blind, or attended to <see cref="PartnerId"/>);
    /// "in" is the far end asking us to call <see cref="Target"/> in their place (a REFER);
    /// "replace" is <see cref="Target"/> taking this call over from <see cref="By"/> (an
    /// INVITE with Replaces: the last step of their attended transfer). Status: asking (our
    /// REFER is out), calling (it's under way), done or failed, with <see cref="Detail"/> in words.
    /// </summary>
    public sealed class TransferInfo
    {
        public required string Role { get; init; }
        /// <summary>Who the call is going to, as a short name ("Dave").</summary>
        public required string Target { get; init; }
        /// <summary>Who's transferring us ("in", "replace").</summary>
        public string? By { get; init; }
        public bool Attended { get; init; }
        /// <summary>Attended, ours: the other call, which ends with this one.</summary>
        public string? PartnerId { get; init; }
        public string Status { get; internal set; } = "asking";
        /// <summary>In full, for the tooltip and the log: "Couldn't transfer: Dave is busy (486 Busy Here). The call is still on hold."</summary>
        public string? Detail { get; internal set; }
        /// <summary>Failed: why, in a few words, for the call header's one line ("Dave is busy (486 Busy Here)").</summary>
        public string? Reason { get; internal set; }
        /// <summary>"in": the call with the transferor, for the NOTIFYs that tell them how it went.</summary>
        internal SIPDialogue? Dialogue;
        /// <summary>"in": why the call to the target failed, if it did (its response, or the error).</summary>
        internal SIPResponse? Failure;
        internal string? FailureError;
        /// <summary>"replace": who's taking over, in full ("Bob &lt;sip:bob@…&gt;").</summary>
        internal string? NewRemote;
        internal string? NewPhone;
        /// <summary>"in", "replace": the sources arriving on the call as it began, i.e. the transferor's (see <see cref="ForgetOldSources"/>).</summary>
        internal Dictionary<SDPMediaTypesEnum, uint>? OldSources;
        /// <summary>"in": the call to the target, once it's under way (its first response), for Cancel.</summary>
        internal ISIPClientUserAgent? TargetCall;
        /// <summary>"in": you cancelled the transfer (see <see cref="CancelTransfer"/>); set before the call to the target has answered anything, it's cancelled when it does.</summary>
        internal bool Cancelled;
        /// <summary>"out": their phone has said the target is ringing (a 100 Trying arriving after doesn't undo it). "in": we've sent NOTIFYs (the first 100 Trying is then late, and left out).</summary>
        internal bool Notified;
        /// <summary>The target has rung (ours: their phone said so; theirs: our call to it).</summary>
        public bool Rang { get; internal set; }
        /// <summary>"in": the transferor hung up on us while we were calling the target.</summary>
        public bool TransferorGone { get; internal set; }
        /// <summary>
        /// Whether the page can offer Cancel: ours while it's under way (asking or calling), or
        /// being transferred while the target hasn't answered and the transferor's still there.
        /// </summary>
        public bool CanCancel => Status is "asking" or "calling" && !Cancelled && (Role == "out" || (Role == "in" && Status == "calling" && !TransferorGone));
        /// <summary>Whether the page can offer to hang up and leave it to them: our blind transfer, accepted, under way on their phone.</summary>
        public bool CanLeave => Role == "out" && Status == "calling" && !Attended;
    }

    private readonly Settings _settings;
    private readonly SIPTransport _transport = new();
    /// <summary>The accounts, by id, each with its registration (if it registers).</summary>
    private readonly Dictionary<string, AccountRegistration> _accounts = new();
    /// <summary>The calls, oldest first, including ended ones for a few seconds.</summary>
    private readonly List<Call> _calls = new();
    private int _nextId;
    private readonly object _lock = new();

    /// <summary>A snapshot of the calls, oldest first.</summary>
    public IReadOnlyList<Call> Calls { get { lock (_lock) return _calls.ToList(); } }
    public Call? Find(string? id) { lock (_lock) return _calls.FirstOrDefault(c => c.Id == id); }

    /// <summary>Simulated loss on what we send, in percent (diagnostics); carries over to new calls and relays.</summary>
    public int AudioLossPercent { get; private set; }
    public int VideoLossPercent { get; private set; }

    public void SetLoss(int audio, int video)
    {
        AudioLossPercent = Math.Clamp(audio, 0, 100);
        VideoLossPercent = Math.Clamp(video, 0, 100);
        foreach (var c in Calls)
            if (c.Relay is { } relay) { relay.AudioLossPercent = AudioLossPercent; relay.VideoLossPercent = VideoLossPercent; }
        Log?.Invoke($"simulated loss: audio {AudioLossPercent}%, video {VideoLossPercent}%");
    }

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
    public event Action<Call, string>? MediaOffer;
    /// <summary>A call has ended, once: for the call history.</summary>
    public event Action<Call>? Ended;
    public event Action<string>? Log;
    /// <summary>The far end's RTCP says our audio is (true) or is again not (false) failing to reach them.</summary>
    public event Action<Call, bool>? TxLostChanged;
    /// <summary>An RTCP reception report on the SIP leg: theirs about what we send, or ours about their video.</summary>
    public event Action<Call, MediaRelay.QualitySample>? Quality;

    /// <summary>
    /// The facts about a connected call for the page's Call details panel: how it was set up,
    /// who answered it with what, and the codecs agreed on the SIP side. Raised when the call
    /// connects, and again if its media changes (video added).
    /// </summary>
    public sealed record CallDetails(
        string Direction, string? Account, string? Signalling, string? TheirPhone, string? CallId,
        string? TheirMedia, bool MediaEncrypted, bool SignallingEncrypted, string? Audio, string? Video,
        string? MediaSecurity = null);
    public event Action<Call, CallDetails>? Details;

    // ---- Real-time text, handled here in C# ----
    // On plain RTP calls: an m=text stream, T.140 with RED redundancy (RFC 4103). On WebRTC calls:
    // a T.140 data channel (RFC 8865, see TextPeerConnection). Either way, text isn't relayed to
    // the page's peer connection: the page sends what's typed as host messages and gets what
    // arrives the same way.

    /// <summary>Characters from them, as they arrive: may include backspace (U+0008) and line separators.</summary>
    public event Action<Call, string>? TextReceived;

    // ---- Browser phones: SIP over WebSocket, with WebRTC media ----

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
        // TLS for accounts set to it: client only, as nothing calls this PC on TLS directly. The
        // connection it opens to the server is the one calls come back on. The server's
        // certificate is checked as for any TLS connection.
        _transport.AddSIPChannel(new SIPTLSChannel(new IPEndPoint(IPAddress.Any, 0)));
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
        // A new transport is a new contact, so the old binding goes.
        bool same = a.Enabled && a.Register && reg.RegisteredAs == $"{a.Username}\n{a.Server}\n{a.Transport}";
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

        if (!SIPURI.TryParse(a.ServerUri, out var serverUri) || serverUri == null)
        {
            SetRegistration(reg, "failed", $"\"{a.Server}\" isn't a server address");
            return;
        }
        var aor = serverUri.CopyOf();
        aor.User = a.Username;
        // The REGISTER goes to the server on the account's transport. The Contact names that
        // transport too: the library leaves it off a REGISTER's Contact, and a contact without
        // one means UDP, so a registrar that took it as given would call back on the wrong one.
        // It names the account's user too: two accounts on one server share this address, and a
        // server that calls us on the contact (rewriting To to it, as sipsorcery.com does) then
        // still says which account the call is for.
        var contact = new SIPURI(aor.Scheme, new SIPEndPoint(a.Protocol, IPAddress.Any, 0)) { User = a.Username };
        var agent = new SIPRegistrationUserAgent(_transport, null, aor, a.AuthUser, a.Password, null, a.RegistrarUri,
            contact, a.Expiry, null);
        reg.Agent = agent;
        reg.RegisteredAs = $"{a.Username}\n{a.Server}\n{a.Transport}";
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
        lock (_accounts)
        {
            var enabled = _accounts.Values.Select(r => r.Account).Where(a => a.Enabled).ToList();
            SipAccount? ByUser(SIPURI? uri) => uri == null || string.IsNullOrEmpty(uri.User) ? null
                : enabled.FirstOrDefault(a => string.Equals(a.Username, uri.User, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Domain, uri.HostAddress, StringComparison.OrdinalIgnoreCase))
                ?? enabled.FirstOrDefault(a => string.Equals(a.Username, uri.User, StringComparison.OrdinalIgnoreCase));
            // To names who they called; a server that calls the registered contact may rewrite To
            // to it (sipsorcery.com does), and then the user in our contact (the request URI)
            // says which account. With one account it can only be that one.
            return (ByUser(req.Header.To?.ToURI) ?? ByUser(req.URI) ?? (enabled.Count == 1 ? enabled[0] : null))?.Clone();
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
    /// A call to the account's own domain goes to its server, so on the account's transport
    /// (TLS to sipsorcery.com, say), unless the address names one.
    /// </summary>
    private static void OnAccountTransport(SIPURI dst, SipAccount a)
    {
        if (a.Protocol != SIPProtocolsEnum.udp && dst.Scheme == SIPSchemesEnum.sip
            && !dst.Parameters.Has("transport")
            && (string.Equals(dst.Host, a.Host, StringComparison.OrdinalIgnoreCase) || string.Equals(dst.HostAddress, a.Domain, StringComparison.OrdinalIgnoreCase)))
        {
            dst.Protocol = a.Protocol;
        }
    }

    /// <summary>Calls that are calling, ringing or connected.</summary>
    private int LiveCount => _calls.Count(c => c.Live);

    private Call NewCall()
    {
        var c = new Call { Id = (++_nextId).ToString(), TextState = _settings.OfferText ? "off" : "disabled" };
        _calls.Add(c);
        return c;
    }

    /// <summary>
    /// Places a call, alongside any others: a video call offers audio and video, an audio call
    /// leaves video out (it can be added later). Returns the new call at once, or null if there
    /// are already <see cref="MaxCalls"/>; the INVITE goes on in the background. If they turn the
    /// offer down because of its media profile (see <see cref="WantsOtherMedia"/>), it's placed
    /// again straight away with the other kind: WebRTC instead of plain RTP, or the other way round.
    /// A bare 488 to plain RTP with SDES keys on RTP/AVP is first tried once more with them on
    /// RTP/SAVP: a phone that requires SRTP (MicroSIP, pjsip, with SRTP mandatory) turns down
    /// RTP/AVP whatever keys it carries. That has to come before WebRTC for such a phone, as
    /// pjsip doesn't do BUNDLE: it answers a WebRTC offer with an ICE and DTLS session per m-line,
    /// of which our one bundled transport only ever completes the first, so there's no video.
    /// <paramref name="media"/> (a contact's, <see cref="ContactMedia"/>) skips all that: the
    /// first INVITE offers it, and a refusal ends the call.
    /// </summary>
    public Call? Dial(string typed, bool video, string? accountId, string? media = null)
    {
        Call c;
        lock (_lock)
        {
            if (LiveCount >= MaxCalls) { Log?.Invoke($"not calling {typed}: already {MaxCalls} calls"); return null; }
            c = NewCall();
            // The chosen account, if it's enabled; with none, a direct call with no credentials.
            c.Account = AccountById(accountId) is { Enabled: true } acct ? acct : null;
            c.Remote = ResolveAddress(typed, c.Account);
            c.Outgoing = true;
            // WebRTC media for an address that says transport=ws (a browser phone), or for every
            // call with --webrtc. Otherwise plain RTP, and a browser behind a server says so with
            // a 488 (see WantsOtherMedia).
            c.WebRtc = IsWebSocketUri(c.Remote) || _settings.WebRtcCalls;
            if (media != null)
            {
                c.MediaSet = true;
                c.WebRtc = media == ContactMedia.WebRtc;
                c.RequireSrtp = media == ContactMedia.Srtp;
                Log?.Invoke($"calling with {MediaKind(c)} media, as set for the contact");
            }
            c.Ua = NewUserAgent(c);
            c.Rtp = NewSession(c, video && _settings.OfferVideo);
            SetState(c, CallState.Calling);
        }
        _ = Task.Run(() => DialAsync(c, video));
        return c;
    }

    private async Task DialAsync(Call c, bool video)
    {
        for (int attempt = 1; ; attempt++)
        {
            var (ua, rtp) = (c.Ua!, c.Rtp!);
            var (ok, failure, response) = await CallOnceAsync(c, ua, rtp);
            if (ok) { await ConnectedAsync(c, rtp); return; }

            lock (_lock)
            {
                // Still ours to retry: nobody hung up meanwhile. At most once to RTP/SAVP, and once
                // to the other kind of media after the first offer or the RTP/SAVP one.
                bool toSavp = !c.WebRtc && !c.RequireSrtp && _settings.OfferSrtp && IsBare488(response);
                if (!c.MediaSet && (toSavp || attempt == 1 || (attempt == 2 && c.RequireSrtp)) &&
                    c.Ua == ua && c.State == CallState.Calling && WantsOtherMedia(response, c.WebRtc))
                {
                    string tried = MediaKind(c);
                    if (toSavp) c.RequireSrtp = true;
                    else c.WebRtc = !c.WebRtc;
                    var why = response!.StatusCode == 406 ? response.ReasonPhrase
                        : response.Header.Warning ?? (string.IsNullOrWhiteSpace(response.Body) ? "no reason given" : "their SDP");
                    Log?.Invoke($"they can't take {tried} media ({response.StatusCode}, {why}); calling again with {MediaKind(c)}");
                    rtp.Close("retrying with other media");
                    c.Ua = NewUserAgent(c);
                    c.Rtp = NewSession(c, video && _settings.OfferVideo);
                    continue;
                }
            }
            if (c.Ua == ua) End(c, failure ?? "Call failed");
            return;
        }
    }

    /// <summary>One INVITE: whether it was answered, and if not, why and the response that said so.</summary>
    private async Task<(bool Ok, string? Failure, SIPResponse? Response)> CallOnceAsync(Call c, SIPUserAgent ua, RTPSession rtp)
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
            // The same user agent places the call to a transfer's target (see TransferRequested):
            // that's the transfer's business, not this call's.
            if (c.State == CallState.Connected) return;
            response = resp;
            c.FailureResponse = resp is { StatusCode: >= 300 } ? $"{resp.StatusCode} {resp.ReasonPhrase}".TrimEnd() : null;
            string said = resp != null ? $"{resp.StatusCode} {resp.ReasonPhrase}" : "";
            failure = resp is { StatusCode: >= 200 and < 300 } ? "Their answer's media doesn't match ours"
                : resp is { Status: SIPResponseStatusCodesEnum.NotAcceptableHere } ? "They can't take this call's media (488)"
                : resp is { StatusCode: 406 } && string.Equals(resp.ReasonPhrase?.Trim(), FailedMediaTransport, StringComparison.OrdinalIgnoreCase)
                    ? "They can't take this call's media (406)"
                : resp != null && ringing is { } r && resp.StatusCode is 408 or 480 or 487
                    ? $"Not answered: rang for {(DateTime.Now - r).TotalSeconds:0} s, then the far end said {said}"
                : resp != null ? $"The far end said {said}" : err;
        };
        ua.ClientCallAnswered += (_, resp) => c.TheirPhone = resp?.Header.Server ?? resp?.Header.UserAgent;
        var a = c.Account;
        try
        {
            if (a == null) return (await ua.Call(c.Remote, null, null, rtp), failure, response);
            // From the account's address and display name, with its credentials if challenged.
            var dst = SIPURI.ParseSIPURIRelaxed(c.Remote);
            OnAccountTransport(dst, a);
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
    /// Also <c>406 Failed creating media transport</c>: what sipsorcery.com's server says when it
    /// can't set up the media our offer asked for (a browser's DTLS-SRTP, or plain RTP to one), so
    /// the other kind might work. Any other 406 isn't retried.
    /// </summary>
    private const string FailedMediaTransport = "Failed creating media transport";

    /// <summary>The kind of media a call offers, for the log.</summary>
    private static string MediaKind(Call c) => c.WebRtc ? "WebRTC" : c.RequireSrtp ? "SRTP (RTP/SAVP)" : "plain RTP";

    /// <summary>A 488 with no Warning and no SDP: no say in what was wrong with our offer.</summary>
    private static bool IsBare488(SIPResponse? resp) =>
        resp is { Status: SIPResponseStatusCodesEnum.NotAcceptableHere } &&
        string.IsNullOrWhiteSpace(resp.Header.Warning) && string.IsNullOrWhiteSpace(resp.Body);

    private static bool WantsOtherMedia(SIPResponse? resp, bool offeredWebRtc)
    {
        if (resp is { StatusCode: 406 } && string.Equals(resp.ReasonPhrase?.Trim(), FailedMediaTransport, StringComparison.OrdinalIgnoreCase)) return true;
        if (resp is not { Status: SIPResponseStatusCodesEnum.NotAcceptableHere }) return false;
        var warning = resp.Header.Warning;
        if (warning is { } w && System.Text.RegularExpressions.Regex.IsMatch(w, @"(^|,)\s*302\s")) return true;
        bool sdp = !string.IsNullOrWhiteSpace(resp.Body) && resp.Header.ContentType?.Contains("sdp", StringComparison.OrdinalIgnoreCase) == true;
        if (!sdp) return string.IsNullOrWhiteSpace(warning);
        return IsWebRtcOffer(resp.Body) != offeredWebRtc;
    }

    public async Task AnswerAsync(string id)
    {
        Call? c;
        SIPUserAgent? ua;
        SIPServerUserAgent? uas;
        RTPSession rtp;
        lock (_lock)
        {
            c = _calls.FirstOrDefault(x => x.Id == id);
            // Once: a second answer while the first is under way (a double click) would make its
            // own media session, fail, and end the call.
            if (c == null || c.State != CallState.Ringing || c.Incoming == null || c.Answering) return;
            c.Answering = true;
            (ua, uas) = (c.Ua, c.Incoming);
            // A WebRTC answer can't add a stream the offer didn't have (RTCPeerConnection would
            // put our video in as an extra m-line, and the browser rejects the answer), so video
            // only if they offered it.
            rtp = NewSession(c, c.WebRtc ? c.IncomingVideo : _settings.OfferVideo, c.IncomingOffer);
            c.Rtp = rtp;
        }
        if (ua != null && uas != null && await ua.Answer(uas, rtp)) await ConnectedAsync(c, rtp);
        else End(c, "Couldn't answer");
    }

    public void Decline(string id)
    {
        var c = Find(id);
        if (c == null) return;
        lock (_lock)
        {
            if (c.State != CallState.Ringing) return;
            c.Incoming?.Reject(SIPResponseStatusCodesEnum.BusyHere, null);
        }
        End(c, "Declined");
    }

    public void Hangup(string id)
    {
        var c = Find(id);
        if (c == null) return;
        lock (_lock)
        {
            if (c.State == CallState.Ringing) { Decline(id); return; }
            if (c.State is not (CallState.Calling or CallState.Connected)) return;
            // SIPUserAgent raises OnCallHungup from inside its own Hangup, which would record
            // this as "They hung up". (Ending the call here first instead stops the BYE: the
            // user agent only sends one while the call is still up.)
            c.HangingUp = true;
            if (c.State == CallState.Calling) c.Ua?.Cancel();
            // While we're calling a transfer's target, the user agent's call is that one: cancel
            // it, and hang up on the transferor ourselves (its Hangup would do neither).
            else if (c.Transfer is { Role: "in", Status: "calling" } t)
            {
                c.Ua?.Cancel();
                if (!t.TransferorGone) try { t.Dialogue?.Hangup(_transport, null); } catch { }
            }
            else c.Ua?.Hangup();
        }
        End(c, "You hung up");
    }

    /// <summary>
    /// Puts a connected call on hold, or takes it off. With music on hold (the account's Hold,
    /// and the default) nothing is signalled: the page sends them music instead of the microphone
    /// and stops playing theirs, and media keeps flowing both ways. With SIP hold it's a
    /// re-INVITE with our media sendonly (the far end's phone shows it's held), or sendrecv
    /// again, and the page sends silence. One re-INVITE at a time per call: switching calls
    /// quickly (hold, resume, hold) would otherwise send a second while the first is still out,
    /// and the far end answers that 491.
    /// A change asked for meanwhile waits, and only the last one counts.
    /// </summary>
    public void Hold(string id, bool hold)
    {
        var c = Find(id);
        if (c == null) return;
        lock (_lock)
        {
            if (c.State != CallState.Connected || c.Ua == null) return;
            // Resuming after a failed transfer is the answer to it: the note goes.
            bool cleared = !hold && c.Transfer is { Role: "out", Status: "failed" } && ClearTransfer(c);
            // (A cancelled one's note stays its few seconds: it's saying what may still happen.)
            if (c.HoldMode != "sip")
            {
                if (c.Held == hold) { if (cleared) Changed?.Invoke(); return; }
                c.Held = c.HoldWanted = hold;
                Log?.Invoke($"{(hold ? "holding" : "resuming")} {c.Remote}: music on hold, nothing signalled");
            }
            else if (c.HoldTimer != null)
            {
                c.HoldWanted = hold;
                Log?.Invoke($"{(hold ? "hold" : "resume")} {c.Remote}: after the re-INVITE that's out");
                return;
            }
            else
            {
                c.HoldWanted = hold;
                if (!SendHold(c)) return;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Sends the re-INVITE for <see cref="Call.HoldWanted"/>, if it isn't what we have. Under the lock.</summary>
    private bool SendHold(Call c)
    {
        if (c.Held == c.HoldWanted || c.Ua == null) return false;
        c.Held = c.HoldWanted;
        Log?.Invoke($"{(c.Held ? "holding" : "resuming")} {c.Remote}: re-INVITE");
        // Done when their answer sets the remote description (OnRemoteDescriptionChanged), or
        // after a few seconds if it doesn't come (turned down, or lost).
        c.HoldTimer = new System.Threading.Timer(_ => HoldDone(c), null, HoldAnswerMs, Timeout.Infinite);
        if (c.Held) c.Ua.PutOnHold(); else c.Ua.TakeOffHold();
        return true;
    }

    private const int HoldAnswerMs = 4000;
    private const int HoldSettleMs = 250;

    /// <summary>The hold or resume re-INVITE is answered (or gave up): send the next one, if asked for.</summary>
    private void HoldDone(Call c)
    {
        if (Interlocked.Exchange(ref c.HoldTimer, null) is not { } t) return;
        t.Dispose();
        bool sent;
        lock (_lock) sent = c.State == CallState.Connected && SendHold(c);
        if (sent) Changed?.Invoke();
    }

    public void SetPageAnswer(string id, string sdp) => Find(id)?.Relay?.SetAnswer(sdp);

    // ---- Transfer (RFC 3515 REFER, RFC 5589) ----
    // Ours: blind sends a REFER asking them to call an address; attended sends one asking them to
    // call the party on another of our calls, with Replaces (RFC 3891) so that call is taken over.
    // Either way the call is held first, and how it goes comes back in NOTIFYs with a sipfrag
    // ("SIP/2.0 180 Ringing", then 200 OK or why not). Done, they hang up on us (we do, if they
    // don't); failed, the call stays held, for you to resume or end. Theirs: a REFER on one of
    // our calls is taken (see TransferRequested for why without asking), and the user agent
    // calls the target on the same media, hanging up on the transferor once it's answered. An
    // INVITE with Replaces for one of our calls is taken over in place by that call's user agent.

    /// <summary>How long our REFER waits for 202 Accepted.</summary>
    private const int ReferAnswerMs = 10000;
    /// <summary>How long a transfer they accepted can go without an outcome before we say we don't know.</summary>
    private const int TransferOutcomeMs = 60000;
    /// <summary>How long after a 200 OK NOTIFY we wait for their BYE before hanging up ourselves.</summary>
    private const int TransferByeMs = 3000;
    /// <summary>How long a transfer done to us (or failed) stays on the call's note.</summary>
    private const int TransferNoteMs = 8000;

    /// <summary>
    /// Transfers a connected call to an address (blind). <paramref name="label"/> is what to call
    /// the target ("Dave"); without one, their address's user part. A bare name or number goes to
    /// the call's account's server. No From account comes into it: the REFER goes on the call's
    /// dialog, and the far end places the new call as itself.
    /// </summary>
    public async Task BlindTransfer(string id, string typed, string? label)
    {
        var c = Find(id);
        SIPUserAgent ua;
        SIPURI? uri;
        SipAccount? a;
        lock (_lock)
        {
            if (c == null || c.State != CallState.Connected || c.Ua == null || TransferBusy(c)) return;
            var address = ResolveAddress(typed, c.Account);
            var target = string.IsNullOrWhiteSpace(label) ? ShortName(address) : label.Trim();
            if (string.IsNullOrWhiteSpace(typed) || !SIPURI.TryParse(address, out uri) || uri == null)
            {
                c.Transfer = new TransferInfo { Role = "out", Target = target, Status = "failed", Detail = $"\"{typed}\" isn't an address to transfer to", Reason = $"\"{typed}\" isn't an address" };
                Changed?.Invoke();
                return;
            }
            // On the call's account's own server, its transport goes in the Refer-To (TLS, say), so
            // their phone reaches it the way we would.
            if (c.Account is { } acct) OnAccountTransport(uri, acct);
            c.Transfer = new TransferInfo { Role = "out", Target = target, Status = "asking", Detail = $"Asking {ShortName(c.Remote)}'s phone to call {target}…" };
            (ua, a) = (c.Ua, c.Account);
            Log?.Invoke($"call {c.Id}: blind transfer of {c.Remote} to {uri}: REFER");
        }
        Hold(id, true);
        Changed?.Invoke();
        var asked = DateTime.Now;
        bool ok = await ua.BlindTransfer(uri, TimeSpan.FromMilliseconds(ReferAnswerMs), CancellationToken.None, null, a?.AuthUser, a?.Password);
        ReferAnswered(c, ua, ok, DateTime.Now - asked);
    }

    /// <summary>
    /// Joins a connected call to another (attended): <paramref name="id"/>'s far end is asked to
    /// call <paramref name="targetId"/>'s, replacing our call with them. Done, both calls end.
    /// </summary>
    public async Task AttendedTransfer(string id, string targetId)
    {
        Call? c = Find(id), t = Find(targetId);
        SIPUserAgent ua;
        SIPDialogue dialogue;
        SipAccount? a;
        lock (_lock)
        {
            if (c == null || t == null || c == t || c.State != CallState.Connected || t.State != CallState.Connected
                || c.Ua == null || t.Ua?.Dialogue == null || TransferBusy(c) || TransferBusy(t)) return;
            string cName = ShortName(c.Remote), tName = ShortName(t.Remote);
            c.Transfer = new TransferInfo { Role = "out", Target = tName, Attended = true, PartnerId = t.Id, Detail = $"Asking {cName}'s phone to join {tName}…" };
            t.Transfer = new TransferInfo { Role = "out", Target = cName, Attended = true, PartnerId = c.Id, Detail = $"Joining {cName} with {tName}…" };
            (ua, dialogue, a) = (c.Ua, t.Ua.Dialogue, c.Account);
            Log?.Invoke($"call {c.Id}: attended transfer of {c.Remote} to call {t.Id} ({t.Remote}): REFER with Replaces");
        }
        Hold(id, true);
        Hold(targetId, true);
        Changed?.Invoke();
        var asked = DateTime.Now;
        bool ok = await ua.AttendedTransfer(dialogue, TimeSpan.FromMilliseconds(ReferAnswerMs), CancellationToken.None, null, a?.AuthUser, a?.Password);
        ReferAnswered(c, ua, ok, DateTime.Now - asked);
    }

    /// <summary>
    /// Cancel on a transfer under way. Ours: SIP has no way to call off a REFER they've accepted
    /// (RFC 3515), so this is the most we can do: forget the transfer, take the call (both,
    /// attended) off hold and carry on. If their phone still gets through, it hangs up on us as
    /// a transferee does, and the call ends "They hung up"; the note says so for a few seconds.
    /// Theirs (being transferred): see below.
    /// </summary>
    public void CancelTransfer(string id)
    {
        var c = Find(id);
        if (c == null) return;
        if (c.Transfer?.Role == "in") { CancelTransferToUs(c); return; }
        Call? partner;
        lock (_lock)
        {
            if (c.Transfer is not { CanCancel: true, Role: "out" } t) return;
            var them = ShortName(c.Remote);
            t.Cancelled = true;
            c.CancelledTransferTo = t.Target;
            partner = t.PartnerId == null ? null : _calls.FirstOrDefault(x => x.Id == t.PartnerId);
            if (partner != null) partner.CancelledTransferTo = ShortName(c.Remote);
            SetTransfer(c, "cancelled", $"Transfer to {t.Target} cancelled. If {them}'s phone still goes ahead with it, it will leave this call.");
            Log?.Invoke($"call {c.Id}: you cancelled the transfer to {t.Target}: back on the call (their phone may still go ahead: SIP can't call back a REFER)");
            _ = ClearTransferLater(c, t);
            if (partner?.Transfer is { } pt) _ = ClearTransferLater(partner, pt);
        }
        Hold(id, false);
        if (partner != null) Hold(partner.Id, false);
        Changed?.Invoke();
    }

    /// <summary>
    /// Being transferred: you'd rather stay with the transferor. The call to the target is
    /// cancelled (CANCEL), which the user agent takes as the transfer failing: it takes the
    /// transferor off hold and the call carries on with them, who hear "487 Request Terminated".
    /// Before the target's phone has answered anything there's nothing to CANCEL yet: it goes
    /// when the first response comes (see NewUserAgent). Not once the transferor has hung up
    /// (there's no one to go back to: Hang up is the way out then).
    /// </summary>
    private void CancelTransferToUs(Call c)
    {
        ISIPClientUserAgent? uac;
        lock (_lock)
        {
            if (c.Transfer is not { CanCancel: true, Role: "in" } t) return;
            t.Cancelled = true;
            t.Detail = $"Cancelling the transfer to {t.Target}, back to {t.By}…";
            uac = t.TargetCall;
            Log?.Invoke($"call {c.Id}: you cancelled {t.By}'s transfer to {t.Target}{(uac == null ? " (the CANCEL goes once it's answered anything)" : "")}");
        }
        Changed?.Invoke();
        try { uac?.Cancel(); } catch (Exception ex) { Log?.Invoke($"call {c.Id}: cancelling the call to the transfer's target: {ex.Message}"); }
    }

    /// <summary>
    /// Our blind transfer, accepted and under way on their phone: you hang up and leave it to
    /// them, as a blind transfer usually goes (RFC 5589 lets the transferor hang up once it has
    /// its 202). SIP has no way to call the transfer off once it's accepted, so this is the only
    /// choice besides waiting for how it goes, with the call on hold.
    /// </summary>
    public void LeaveTransfer(string id)
    {
        var c = Find(id);
        if (c == null) return;
        string target;
        lock (_lock)
        {
            if (c.State != CallState.Connected || c.Ua == null || c.Transfer is not { CanLeave: true } t) return;
            target = t.Target;
            Log?.Invoke($"call {c.Id}: hanging up, leaving the transfer to {target} to {c.Remote}'s phone");
            c.HangingUp = true;
            c.Ua.Hangup();
        }
        c.TransferNote = $"Transferred to {target} (you left it to their phone)";
        End(c, c.TransferNote);
    }

    /// <summary>Drops a transfer's note (a failed one, once you've seen it).</summary>
    public void DismissTransfer(string id)
    {
        var c = Find(id);
        if (c == null) return;
        bool cleared;
        lock (_lock) cleared = c.Transfer is { Status: "failed" or "done" or "cancelled" } && ClearTransfer(c);
        if (cleared) Changed?.Invoke();
    }

    /// <summary>A transfer is under way on the call (ours or theirs). Under the lock.</summary>
    private static bool TransferBusy(Call c) => c.Transfer is { Status: "asking" or "calling" };

    /// <summary>Removes a call's transfer, and its partner's if it's the same one. Under the lock.</summary>
    private bool ClearTransfer(Call c)
    {
        if (c.Transfer is not { } t) return false;
        c.Transfer = null;
        if (t.PartnerId != null && _calls.FirstOrDefault(x => x.Id == t.PartnerId) is { Transfer.PartnerId: var p } partner && p == c.Id) partner.Transfer = null;
        return true;
    }

    /// <summary>Sets our transfer's status on the call, and on the other call of an attended one. Under the lock.</summary>
    private void SetTransfer(Call c, string status, string? detail, string? reason = null)
    {
        if (c.Transfer is not { } t) return;
        t.Status = status;
        t.Detail = detail;
        t.Reason = reason;
        if (t.PartnerId != null && _calls.FirstOrDefault(x => x.Id == t.PartnerId) is { Transfer: { } pt } partner && pt.PartnerId == c.Id)
        {
            pt.Status = status;
            pt.Detail = detail;
            pt.Reason = reason;
            pt.Rang = t.Rang;
        }
    }

    /// <summary>Our REFER's answer: 202 Accepted (true), or turned down or unanswered.</summary>
    private void ReferAnswered(Call c, SIPUserAgent ua, bool accepted, TimeSpan took)
    {
        lock (_lock)
        {
            if (c.Ua != ua || c.Transfer is not { Role: "out", Status: "asking" } t) return;
            var them = ShortName(c.Remote);
            if (accepted)
            {
                Log?.Invoke($"call {c.Id}: transfer accepted (202): {them} is calling {t.Target}");
                SetTransfer(c, "calling", t.Attended ? $"Joining {them} with {t.Target}…" : $"Transferring to {t.Target}…");
                // A phone that sends no NOTIFYs (norefersub) still hangs up on us when it's done;
                // one that does neither leaves us not knowing.
                _ = Task.Delay(TransferOutcomeMs).ContinueWith(_ =>
                {
                    lock (_lock)
                    {
                        if (c.Ua != ua || c.Transfer != t || t.Status != "calling") return;
                        SetTransfer(c, "failed", $"No word from {them}'s phone on how the transfer went. The call is still on hold.", $"no word from {them}'s phone");
                    }
                    Changed?.Invoke();
                });
            }
            else
            {
                bool quick = took.TotalMilliseconds < ReferAnswerMs - 500;
                Log?.Invoke($"call {c.Id}: transfer {(quick ? "turned down" : "not answered")} by {them}");
                SetTransfer(c, "failed", quick ? $"{them}'s phone turned the transfer down. The call is still on hold."
                    : $"{them}'s phone didn't answer the transfer request. The call is still on hold.",
                    quick ? $"{them}'s phone turned it down" : $"no answer from {them}'s phone");
            }
        }
        Changed?.Invoke();
    }

    /// <summary>A NOTIFY on our transfer: the sipfrag of their call to the target, e.g. "SIP/2.0 180 Ringing".</summary>
    private void OnTransferNotify(Call c, SIPUserAgent ua, string sipfrag)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sipfrag ?? "", @"^SIP/2\.0\s+(?<code>\d{3})\s*(?<reason>[^\r\n]*)");
        if (!m.Success) return;
        int code = int.Parse(m.Groups["code"].Value);
        string reason = m.Groups["reason"].Value.Trim();
        lock (_lock)
        {
            // An outcome after a failure we'd already given (the timeout, say) still counts.
            if (c.Ua != ua || c.State != CallState.Connected || c.Transfer is not { Role: "out", Status: "asking" or "calling" or "failed", Cancelled: false } t) return;
            Log?.Invoke($"call {c.Id}: transfer NOTIFY {code} {reason}");
            var them = ShortName(c.Remote);
            if (code < 200)
            {
                // NOTIFYs can arrive out of order (a phone that sends 100 Trying late): ringing stays.
                if (t.Status == "failed" || (t.Rang && code is not (180 or 183))) return;
                if (code is 180 or 183) t.Rang = true;
                SetTransfer(c, "calling", code is 180 or 183 ? $"{t.Target} is ringing… {them} is on hold." : t.Attended ? $"Joining {them} with {t.Target}…" : $"Transferring to {t.Target}…");
            }
            else if (code < 300)
            {
                SetTransfer(c, "done", t.Attended ? $"{them} and {t.Target} are talking" : $"Transferred to {t.Target}");
                // They hang up on us now (both of them, attended); if not, we do.
                var partner = t.PartnerId == null ? null : _calls.FirstOrDefault(x => x.Id == t.PartnerId);
                foreach (var leg in new[] { c, partner })
                {
                    if (leg?.Ua is not { } legUa || leg.Transfer is not { } lt) continue;
                    _ = Task.Delay(TransferByeMs).ContinueWith(_ =>
                    {
                        lock (_lock)
                        {
                            if (leg.Ua != legUa || leg.State != CallState.Connected) return;
                            Log?.Invoke($"call {leg.Id}: transferred, and they haven't hung up: hanging up");
                            leg.HangingUp = true;
                            legUa.Hangup();
                        }
                        EndTransferred(leg, lt.Target);
                    });
                }
            }
            else SetTransfer(c, "failed", code == 487 ? $"{them} stopped the transfer before {t.Target} answered. The call is still on hold."
                : $"Couldn't transfer: {TransferFailure(code, reason, t.Target)}. The call is still on hold.",
                code == 487 ? $"{them} stopped it" : TransferFailure(code, reason, t.Target));
        }
        Changed?.Invoke();
    }

    /// <summary>A call that's been transferred ends: "Transferred to Dave", in its end reason and the history.</summary>
    private void EndTransferred(Call c, string target)
    {
        c.TransferNote = $"Transferred to {target}";
        End(c, c.TransferNote);
    }

    /// <summary>Why the call to a transfer's target failed, in words, from its response.</summary>
    private static string TransferFailure(int code, string reason, string who) => code switch
    {
        404 or 604 => $"{who} wasn't found",
        486 or 600 => $"{who} is busy",
        // 408: nothing replied at all (no such host, or nothing listening there); 480, 487: it rang out.
        408 => $"nothing answered at {who}'s address",
        480 or 487 => $"{who} didn't answer",
        603 => $"{who} declined the call",
        401 or 403 or 407 => $"the call to {who} wasn't allowed",
        488 or 606 => $"{who}'s phone couldn't take the call",
        502 or 503 or 504 => $"{who} couldn't be reached",
        _ => $"the call to {who} failed",
    } + $" ({code} {reason})".Replace(" )", ")");

    /// <summary>
    /// The far end asks us to call someone in their place (a REFER on our call with them). The
    /// user agent asks this on its SIP receive thread and needs the answer straight away (202 or
    /// 603), so there's no time to ask the user: it's taken, unless the call can't be moved.
    /// That's the safer of the two defaults the user agent allows: refusing every transfer would
    /// break the ordinary case (a receptionist putting you through), while what's taken is
    /// narrow and shown. It must come inside a connected call, from the person you're talking to;
    /// the call goes out with your account's password only to that account's own server (so a
    /// transfer can't use your account to reach an outside number), and the stage says who's
    /// transferring you to whom while it rings, so you can hang up.
    /// </summary>
    private bool TransferRequested(Call c, SIPUserAgent ua, SIPUserField referTo, string? referredBy)
    {
        bool attended = referTo.URI.Headers.Has(SIPHeaderAncillary.SIP_REFER_REPLACES);
        string to = !string.IsNullOrWhiteSpace(referTo.Name) ? referTo.Name.Trim('"', ' ') : ShortName(referTo.URI.ToParameterlessString());
        TransferInfo t;
        lock (_lock)
        {
            string by = ShortName(c.Remote);
            string? why = c.Ua != ua || c.State != CallState.Connected ? "the call isn't connected"
                : c.WebRtc ? "a browser phone's call can't be moved to another call"
                : TransferBusy(c) ? "a transfer is already under way on it"
                : referTo.URI.Scheme is not (SIPSchemesEnum.sip or SIPSchemesEnum.sips) ? $"{referTo.URI} isn't a SIP address"
                : null;
            if (why != null)
            {
                Log?.Invoke($"call {c.Id}: {by} asked to transfer us to {referTo.URI}: turned down (603), {why}");
                c.Transfer = new TransferInfo { Role = "in", Target = to, By = by, Attended = attended, Status = "failed", Detail = $"{by} tried to transfer you to {to}, but {why}.", Reason = why };
                _ = ClearTransferLater(c, c.Transfer);
                Changed?.Invoke();
                return false;
            }
            t = c.Transfer = new TransferInfo
            {
                Role = "in", Target = to, By = by, Attended = attended, Status = "calling", Dialogue = ua.Dialogue, OldSources = new(c.Sources),
                Detail = $"{by} is transferring you to {to}…",
            };
            Log?.Invoke($"call {c.Id}: {by} is transferring us to {referTo.URI.ToParameterlessString()}{(attended ? " (attended: replacing their call with them)" : "")}{(referredBy != null ? $", referred by {referredBy}" : "")}: accepted (202)");
        }
        Changed?.Invoke();
        // The first NOTIFY once the 202 is out (the user agent sends it when this returns).
        // Not if the target's already ringing (NotifyTransferor has sent 180): it'd go after it.
        _ = Task.Delay(200).ContinueWith(_ => { if (!t.Notified) NotifyTransferor(c, t, "100 Trying", false); });
        return true;
    }

    /// <summary>
    /// The call to a transfer's target, before it goes: from our account (with its password if
    /// the target is on that account's server, see <see cref="TransferRequested"/>), and with
    /// the transferor named in Referred-By (RFC 3892) so the target can show who sent us.
    /// </summary>
    private void TransferCallDescriptor(Call c, SIPCallDescriptor d, SIPRequest refer)
    {
        // The user agent calls the Refer-To address without its parameters, so a transport it
        // named is lost, and a call to the account's server would go over UDP however the account
        // reaches it (a TLS-only server never answers). Routed as when we dial it.
        if (SIPURI.TryParse(d.Uri, out var dst) && dst != null)
        {
            try
            {
                var named = SIPUserField.ParseSIPUserField(refer.Header.ReferTo).URI;
                if (named.Parameters.Has("transport")) dst.Protocol = named.Protocol;
            }
            catch { }
            if (c.Account is { } acct) OnAccountTransport(dst, acct);
            d.Uri = dst.ToString();
        }
        if (c.Account is { } a)
        {
            d.From = new SIPFromHeader(a.DisplayName, SIPURI.ParseSIPURIRelaxed($"sip:{a.Username}@{a.Host}"), null).ToString();
            if (SIPURI.TryParse(d.Uri, out var uri) && uri != null
                && (string.Equals(uri.HostAddress, a.Host, StringComparison.OrdinalIgnoreCase) || string.Equals(uri.HostAddress, a.Domain, StringComparison.OrdinalIgnoreCase)))
            {
                (d.Username, d.AuthUsername, d.Password) = (a.Username, a.AuthUser, a.Password);
            }
        }
        // A direct call: from the address the transferor reached us at (sip:vtO@host:port, say),
        // so the target sees who's calling rather than the user agent's anonymous default.
        else if (c.Transfer?.Dialogue?.LocalUserField is { URI.User.Length: > 0 } me)
        {
            d.From = new SIPFromHeader(me.Name, me.URI.CopyOf(), null).ToString();
        }
        if (!string.IsNullOrWhiteSpace(refer.Header.ReferredBy))
        {
            d.CustomHeaders ??= new List<string>();
            d.CustomHeaders.Add($"{SIPHeaders.SIP_HEADER_REFERREDBY}: {refer.Header.ReferredBy}");
        }
    }

    /// <summary>
    /// The call to a transfer's target is answered (or not). Answered, the user agent hangs up on
    /// the transferor; this call is now with the target: same id and media, new far end. The
    /// call with the transferor goes into the history as transferred. Not answered, the user
    /// agent takes the transferor off hold, and the call carries on with them.
    /// </summary>
    private void TransferOutcome(Call c, SIPUserAgent ua, bool ok)
    {
        TransferInfo t;
        Call? before = null;
        lock (_lock)
        {
            if (c.Ua != ua || c.Transfer is not { Role: "in", Status: "calling" } tt) return;
            t = tt;
            if (ok)
            {
                // Before the user agent's BYE: the transferor hears how it went first.
                NotifyTransferor(c, t, "200 OK", true);
                before = Snapshot(c, $"{t.By} transferred you to {t.Target}", $"Transferred to {t.Target}");
                var dialogue = ua.Dialogue;
                var them = dialogue?.RemoteUserField;
                c.Remote = them == null ? t.Target : !string.IsNullOrWhiteSpace(them.Name) ? $"{them.Name} <{them.URI.ToParameterlessString()}>"
                    : t.Target != ShortName(them.URI.ToParameterlessString()) ? $"{t.Target} <{them.URI.ToParameterlessString()}>" : them.URI.ToParameterlessString();
                c.Outgoing = true;
                c.StartedAt = DateTime.Now;
                c.ConnectedAt = DateTime.Now;
                c.HeldByThem = false;
                t.Status = "done";
                t.Detail = $"{t.By} transferred you to {t.Target}";
                ForgetOldSources(c, t);
                Log?.Invoke($"call {c.Id}: transferred: now with {c.Remote}");
            }
            else
            {
                var r = t.Failure;
                var (code, reason) = r != null && r.StatusCode >= 300 ? (r.StatusCode, r.ReasonPhrase)
                    : t.FailureError?.Contains("resolve", StringComparison.OrdinalIgnoreCase) == true ? (404, "Not Found")
                    : (408, "Request Timeout");
                if (t.TransferorGone)
                {
                    // No one to go back to (the user agent's re-INVITE to them gets a 481).
                    t.Status = "failed";
                    Log?.Invoke($"call {c.Id}: transfer to {t.Target} failed ({code} {reason}), and {t.By} had hung up");
                    var why = $"Couldn't reach {t.Target}: {TransferFailure(code, reason, t.Target)}, and {t.By} had hung up";
                    _ = Task.Run(() => End(c, why));
                    return;
                }
                if (t.Cancelled) (code, reason) = (487, "Request Terminated");
                NotifyTransferor(c, t, $"{code} {reason}", true);
                t.Status = "failed";
                t.Detail = t.Cancelled ? $"You cancelled the transfer to {t.Target}. You're back with {t.By}."
                    : $"{t.By} tried to transfer you to {t.Target}, but {TransferFailure(code, reason, t.Target)}. You're still with {t.By}.";
                t.Reason = t.Cancelled ? "you cancelled it" : TransferFailure(code, reason, t.Target);
                Log?.Invoke($"call {c.Id}: transfer to {t.Target} failed ({code} {reason}{(t.FailureError != null ? $", {t.FailureError}" : "")}); back with {t.By}");
            }
            _ = ClearTransferLater(c, t);
        }
        if (before != null) RecordEnded(before);
        Changed?.Invoke();
        if (ok && c.Rtp is { } rtp && c.Relay is { } relay) Details?.Invoke(c, DescribeCall(c, rtp, relay));
    }

    /// <summary>
    /// An INVITE with Replaces has arrived for this call (their attended transfer, with us as the
    /// target): the user agent answers it, and once it's up, hangs up the call it replaces.
    /// Nothing to decide: the person we're talking to arranged it, and it's the same call to us.
    /// </summary>
    private void ReplacementArriving(Call c, SIPUserAgent ua, SIPRequest invite)
    {
        lock (_lock)
        {
            if (c.Ua != ua || c.State != CallState.Connected) return;
            var from = invite.Header.From;
            var newRemote = from.FromURI.ToParameterlessString();
            if (!string.IsNullOrWhiteSpace(from.FromName)) newRemote = $"{from.FromName} <{newRemote}>";
            string by = ShortName(c.Remote), to = ShortName(newRemote);
            c.Transfer = new TransferInfo
            {
                Role = "replace", Target = to, By = by, Status = "calling", NewRemote = newRemote, NewPhone = invite.Header.UserAgent, OldSources = new(c.Sources),
                Detail = $"{by} is handing you over to {to}…",
            };
            Log?.Invoke($"call {c.Id}: INVITE with Replaces from {newRemote}{(invite.Header.ReferredBy != null ? $" (referred by {invite.Header.ReferredBy})" : "")}: taking over the call with {c.Remote}");
        }
        Changed?.Invoke();
    }

    /// <summary>The call replacing this one is answered: it's now with them, and the old one goes into the history.</summary>
    private void ReplacementDone(Call c, SIPUserAgent ua)
    {
        Call before;
        lock (_lock)
        {
            if (c.Ua != ua || c.Transfer is not { Role: "replace" } t) return;
            before = Snapshot(c, $"{t.By} handed you over to {t.Target}", $"Transferred to {t.Target}");
            c.Remote = t.NewRemote ?? t.Target;
            c.TheirPhone = t.NewPhone;
            c.Outgoing = false;
            c.Incoming = null;
            c.StartedAt = DateTime.Now;
            c.ConnectedAt = DateTime.Now;
            c.HeldByThem = false;
            t.Status = "done";
            t.Detail = $"{t.By} handed you over to {t.Target}";
            ForgetOldSources(c, t);
            Log?.Invoke($"call {c.Id}: replaced: now with {c.Remote}");
            _ = ClearTransferLater(c, t);
        }
        RecordEnded(before);
        Changed?.Invoke();
        if (c.Rtp is { } rtp && c.Relay is { } relay) Details?.Invoke(c, DescribeCall(c, rtp, relay));
    }

    /// <summary>
    /// After a transfer the user agent carries on with the same media session, and each stream's
    /// RTCP keeps its reception report on the first source it heard: the transferor's. Our
    /// reports would then go on describing a stream that's stopped and never list the new far
    /// end's, which reads to them as their media not reaching us ("isn't getting your audio").
    /// Dropping the old source's report makes the next packet, from the new far end, start a
    /// fresh one; done again a second later in case a late packet from the old one brought it back.
    /// </summary>
    private void ForgetOldSources(Call c, TransferInfo t)
    {
        // Video to and from someone new starts mid-stream: keyframes both ways, now.
        c.Relay?.FarEndChanged();
        if (t.OldSources is not { Count: > 0 } old || c.Rtp is not { } rtp) return;
        void Forget()
        {
            if (c.Rtp != rtp) return;
            foreach (var (kind, ssrc) in old)
            {
                MediaStream? stream = kind switch
                {
                    SDPMediaTypesEnum.audio => rtp.AudioStream,
                    SDPMediaTypesEnum.video => rtp.VideoStream,
                    SDPMediaTypesEnum.text => rtp.TextStream,
                    _ => null,
                };
                stream?.RtcpSession?.RemoveReceptionReport(ssrc);
            }
        }
        Forget();
        Log?.Invoke($"call {c.Id}: RTCP: dropped the reports on the transferor's media ({string.Join(", ", old.Select(o => $"{o.Key} {o.Value}"))})");
        _ = Task.Delay(1000).ContinueWith(_ => Forget());
    }

    /// <summary>The part of a call before a transfer, as an ended call of its own, for the history.</summary>
    private static Call Snapshot(Call c, string reason, string note) => new()
    {
        Id = c.Id, State = CallState.Ended, Remote = c.Remote, Outgoing = c.Outgoing, Account = c.Account, WebRtc = c.WebRtc,
        StartedAt = c.StartedAt, ConnectedAt = c.ConnectedAt, EndedAt = DateTime.Now, EndReason = reason, TransferNote = note,
    };

    private void RecordEnded(Call c)
    {
        try { Ended?.Invoke(c); } catch (Exception ex) { Log?.Invoke($"recording call {c.Id} in history failed: {ex.Message}"); }
    }

    /// <summary>A transfer done to us, or a failed one, stays on the call's note for a few seconds.</summary>
    /// <summary>The call to a transfer's target has answered something: kept for Cancel, and cancelled now if you already have.</summary>
    private void TargetCallStarted(Call c, TransferInfo t, ISIPClientUserAgent uac)
    {
        bool cancelNow;
        lock (_lock)
        {
            if (t.TargetCall == uac) return;
            t.TargetCall = uac;
            cancelNow = t.Cancelled;
        }
        if (cancelNow) try { uac.Cancel(); } catch (Exception ex) { Log?.Invoke($"call {c.Id}: cancelling the call to the transfer's target: {ex.Message}"); }
    }

    private async Task ClearTransferLater(Call c, TransferInfo t)
    {
        await Task.Delay(TransferNoteMs);
        lock (_lock) { if (c.Transfer != t || t.Status is "asking" or "calling") return; c.Transfer = null; }
        Changed?.Invoke();
    }

    /// <summary>
    /// Tells the transferor how their transfer's going: a NOTIFY (Event: refer) in our call with
    /// them, with the call to the target's status line as a sipfrag. The user agent sends none.
    /// </summary>
    /// <summary>
    /// A BYE from the person transferring us, while we call the target. The user agent answers it
    /// but, busy with the new call, doesn't end anything, and that's right: a transferor may
    /// hang up as soon as we've accepted (RFC 5589). So the call to the target goes on; if it
    /// fails there's no one to go back to, and the call ends (see <see cref="TransferOutcome"/>).
    /// </summary>
    private void TransferorHungUp(SIPRequest bye)
    {
        lock (_lock)
        {
            var c = _calls.FirstOrDefault(x => x.Transfer is { Role: "in", Status: "calling", TransferorGone: false } t && t.Dialogue?.CallId == bye.Header.CallId);
            if (c?.Transfer is not { } t) return;
            t.TransferorGone = true;
            t.Detail = $"{t.By} hung up. Still calling {t.Target}…";
            Log?.Invoke($"call {c.Id}: {t.By} hung up during the transfer; still calling {t.Target}");
        }
        Changed?.Invoke();
    }

    private void NotifyTransferor(Call c, TransferInfo t, string status, bool final)
    {
        if (t.Dialogue is not { } dialogue || t.TransferorGone) return;
        t.Notified = true;
        try
        {
            var notify = dialogue.GetInDialogRequest(SIPMethodsEnum.NOTIFY);
            notify.Header.Event = "refer";
            notify.Header.SubscriptionState = final ? "terminated;reason=noresource" : "active;expires=60";
            notify.Header.ContentType = SIPMIMETypes.REFER_CONTENT_TYPE + ";version=2.0";
            notify.Body = $"SIP/2.0 {status}\r\n";
            notify.Header.ContentLength = notify.Body.Length;
            new SIPNonInviteTransaction(_transport, notify, null).SendRequest();
            Log?.Invoke($"call {c.Id}: NOTIFY to {t.By}: {status}");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"call {c.Id}: NOTIFY to {t.By} failed: {ex.Message}");
        }
    }

    /// <summary>Who a call's with, short: the display name, else the address's user part ("dave"), else its host.</summary>
    public static string ShortName(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)) return "them";
        var m = System.Text.RegularExpressions.Regex.Match(remote, @"^\s*""?(?<name>[^""<]*?)""?\s*<(?<uri>[^>]*)>");
        if (m.Success && m.Groups["name"].Value.Trim().Length > 0) return m.Groups["name"].Value.Trim();
        var uri = System.Text.RegularExpressions.Regex.Replace(m.Success ? m.Groups["uri"].Value : remote.Trim(), "^(sips?|tel):", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var user = uri.Split('@', ';', '>')[0];
        if (user.Length == 0) return "them";
        return user.All(ch => char.IsDigit(ch) || ch is '+' or '*' or '#' or '.' or ':') ? user : char.ToUpperInvariant(user[0]) + user[1..];
    }

    /// <summary>The far end turned down our request to add video (m=video 0, an error, or no answer).</summary>
    public event Action<Call>? VideoRefused;

    /// <summary>
    /// Adds video to an audio call: our video codecs go on the session and a re-INVITE offers
    /// them. If they accept, the relay is rebuilt with video (see <see cref="OnRemoteDescriptionChanged"/>);
    /// if they refuse, or don't answer within 8 s, <see cref="VideoRefused"/> is raised.
    /// </summary>
    public void AddVideo(string id)
    {
        var c = Find(id);
        if (c == null) return;
        lock (_lock)
        {
            if (c.State != CallState.Connected || c.Ua == null || c.Rtp == null || !_settings.OfferVideo) { VideoRefused?.Invoke(c); return; }
            if (MediaRelay.HasNegotiatedVideo(c.Rtp)) return;
            MediaRelay.AddVideoTrack(c.Rtp);
            c.AddVideoTimer?.Dispose();
            c.AddVideoTimer = new System.Threading.Timer(_ => AddVideoDone(c, false), null, 8000, Timeout.Infinite);
            Log?.Invoke("adding video: re-INVITE with m=video");
            c.Ua.AddVideo();
        }
    }

    /// <summary>
    /// The SIP side's media session for a call: WebRTC for a browser phone, with real-time
    /// text on a data channel, or plain RTP with an m=text stream. <paramref name="theirOffer"/>
    /// is the SDP of a call we're answering, or null if we make the offer.
    /// </summary>
    private RTPSession NewSession(Call c, bool video, string? theirOffer = null)
    {
        // SRTP whenever we can: a phone that requires it offers RTP/SAVP, which plain RTP can't
        // answer, so take it even with --no-srtp.
        bool srtp = _settings.OfferSrtp || (theirOffer != null && OffersSavp(theirOffer));
        var rtp = MediaRelay.CreateSipSession(video, c.WebRtc, srtp, requireSrtp: theirOffer == null && c.RequireSrtp);
        // Who's sending, per kind: a transfer needs the old far end's sources (ForgetOldSources).
        rtp.OnRtpPacketReceived += (_, kind, pkt) => { if (c.Rtp == rtp) c.Sources[kind] = pkt.Header.SyncSource; };
        if (rtp is TextPeerConnection pc)
        {
            c.Rtt?.Dispose();
            c.Rtt = null;
            AttachDataChannelText(c, pc, theirOffer);
            pc.onconnectionstatechange += state =>
            {
                Log?.Invoke($"WebRTC media (ICE, DTLS) {state}");
                if (state == RTCPeerConnectionState.failed && c.Rtp == pc) { c.Ua?.Hangup(); End(c, "Media connection failed"); }
                // The far end closed its media without a BYE: a browser phone's page reloaded or
                // closed, say (DTLS close_notify, with no BYE sent). The call would otherwise stay
                // up with no media. But the media closes on every hang up too, ours and theirs (the
                // user agent closes it before it reports a BYE), so give those a moment to end the
                // call first.
                if (state == RTCPeerConnectionState.closed && c.Rtp == pc && c.State == CallState.Connected)
                {
                    var ua = c.Ua;
                    _ = Task.Delay(1000).ContinueWith(_ =>
                    {
                        if (c.Rtp != pc || c.Ua != ua || c.State != CallState.Connected) return;
                        Log?.Invoke("WebRTC media closed without a BYE: hanging up");
                        c.HangingUp = true;
                        ua?.Hangup();
                        End(c, "Media connection closed");
                    });
                }
            };
        }
        else AttachText(c, rtp);
        return rtp;
    }

    private static bool OffersSavp(string sdp)
    {
        try { return SDP.ParseSDPDescription(sdp).Media.Any(m => m.Port != 0 && m.Transport == "RTP/SAVP"); }
        catch { return false; }
    }

    private static bool IsWebSocketUri(string uri) =>
        SIPURI.TryParse(uri, out var u) && u!.Protocol is SIPProtocolsEnum.ws or SIPProtocolsEnum.wss;

    /// <summary>
    /// Gives a new SIP session a text track (RED with T.140 inside, and plain T.140) and an
    /// <see cref="RttEndPoint"/> to send and receive on it, wired the way VoIPMediaSession does.
    /// It's offered on every call (unless --no-text), and only started once it's agreed.
    /// </summary>
    private void AttachText(Call c, RTPSession rtp)
    {
        c.Rtt?.Dispose();
        c.Rtt = null;
        c.TextState = _settings.OfferText ? "off" : "disabled";
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
        rtt.OnTextReceived += e => TextReceived?.Invoke(c, e.Text);
        c.Rtt = rtt;
    }

    /// <summary>
    /// Real-time text on a WebRTC call: a T.140 data channel (RFC 8865), in our offer on the
    /// stream we pick, or in our answer on the stream their offer has. An offer without one
    /// (a browser phone that doesn't do it) means no text, unless they open one in band.
    /// </summary>
    private void AttachDataChannelText(Call c, TextPeerConnection pc, string? theirOffer)
    {
        c.TextState = _settings.OfferText ? "off" : "disabled";
        if (!_settings.OfferText) return;
        pc.TextReceived += text => { if (c.Rtp == pc) TextReceived?.Invoke(c, text); };
        pc.TextOpened += () =>
        {
            if (Interlocked.Exchange(ref c.AddTextTimer, null) is { } t) t.Dispose();
            if (c.Rtp != pc || c.TextState == "on") return;
            // A negotiated channel opens here once SCTP is up, whether or not they have one on
            // that stream: it only counts if their SDP agreed to it. One they opened in band does.
            if (pc.T140?.negotiated == true && TextPeerConnection.FindT140(pc.RemoteDescription) == null) return;
            c.TextState = "on";
            Log?.Invoke($"real-time text on (RFC 8865, T.140 data channel on stream {pc.T140?.id})");
            Changed?.Invoke();
        };
        ushort? stream = string.IsNullOrWhiteSpace(theirOffer) ? TextPeerConnection.OfferStreamId : TextPeerConnection.FindT140(theirOffer);
        if (stream is ushort id) { pc.AddT140(id); c.TextState = "adding"; }
        else c.TextState = "refused";
    }

    /// <summary>
    /// A connected WebRTC call's text: on if the channel's open already; refused if their SDP has
    /// no T.140 channel; otherwise it's opening, and refused if it isn't open within 10 s.
    /// </summary>
    private void StartDataChannelText(Call c, TextPeerConnection pc)
    {
        if (c.TextState != "adding" || pc.TextOpen) return;
        if (TextPeerConnection.FindT140(pc.RemoteDescription) == null)
        {
            c.TextState = "refused";
            Log?.Invoke("they didn't take text (no T.140 data channel in their SDP)");
            Changed?.Invoke();
            return;
        }
        Log?.Invoke("real-time text agreed: opening the T.140 data channel");
        c.AddTextTimer?.Dispose();
        c.AddTextTimer = new System.Threading.Timer(_ =>
        {
            if (Interlocked.Exchange(ref c.AddTextTimer, null) is not { } t) return;
            t.Dispose();
            if (c.Rtp != pc || c.TextState != "adding") return;
            c.TextState = "refused";
            Log?.Invoke("the T.140 data channel didn't open: no text on this call");
            Changed?.Invoke();
        }, null, 10000, Timeout.Infinite);
    }

    /// <summary>Text is agreed on the session: their SDP has a text line with a port, and it isn't inactive.</summary>
    private static bool HasNegotiatedText(RTPSession rtp) =>
        rtp.RemoteDescription?.Media.Any(m => m.Media == SDPMediaTypesEnum.text && m.Port != 0) == true &&
        rtp.TextStream?.RemoteTrack != null && rtp.TextStream.RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;

    /// <summary>Starts sending and receiving text if it's agreed and not already on.</summary>
    private async Task StartTextIfAgreed(Call c, RTPSession rtp)
    {
        if (rtp is TextPeerConnection pc) { StartDataChannelText(c, pc); return; }
        if (c.Rtt == null || c.TextState == "on" || !HasNegotiatedText(rtp)) return;
        await c.Rtt.Start();
        if (Interlocked.Exchange(ref c.AddTextTimer, null) is { } t) t.Dispose();
        c.TextState = "on";
        Log?.Invoke("real-time text on (RFC 4103)");
        Changed?.Invoke();
    }

    /// <summary>Sends what was typed (letters, backspaces, line separators) as real-time text.</summary>
    public void SendText(string id, string text)
    {
        var c = Find(id);
        if (c == null || c.TextState != "on") return;
        if (c.Rtp is TextPeerConnection pc) pc.SendText(text);
        else if (c.Rtt?.CanSendText == true) c.Rtt.SendText(text);
    }

    // ---- DTMF: the keypad ----
    // Sent as RFC 4733 telephone-events in the call's audio when the far end's SDP has
    // telephone-event (we always offer it: SIPSorcery adds it to every audio track), otherwise
    // as SIP INFO (application/dtmf-relay), which many PBXs and IVRs also take. Received either
    // way, and reported through DtmfReceived for the call's log.

    /// <summary>A keypad digit they sent on a call, and how: "RFC 4733" or "SIP INFO".</summary>
    public event Action<Call, string, string>? DtmfReceived;

    /// <summary>The DTMF event codes, in order (RFC 4733 section 3.2): 0–9, then *, #, A–D.</summary>
    private const string DtmfDigits = "0123456789*#ABCD";

    /// <summary>The far end's telephone-event format (e.g. 101 telephone-event/8000), or null if their SDP has none.</summary>
    private static SDPAudioVideoMediaFormat? NegotiatedDtmf(RTPSession rtp)
    {
        var audio = rtp.RemoteDescription?.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio && m.Port != 0);
        // As nullable: FirstOrDefault on the struct itself gives a default format, not "none".
        return audio?.MediaFormats.Values
            .Where(f => string.Equals(f.Name(), SDP.TELEPHONE_EVENT_ATTRIBUTE, StringComparison.OrdinalIgnoreCase))
            .Select(f => (SDPAudioVideoMediaFormat?)f).FirstOrDefault();
    }

    /// <summary>
    /// Sends a keypad digit (0–9, *, #) on a connected call that isn't on hold either way:
    /// RFC 4733 if they agreed telephone-event, otherwise SIP INFO. Presses queue, one tone at a time.
    /// </summary>
    public async Task SendDtmf(string id, string digit)
    {
        var c = Find(id);
        int code = digit?.Length == 1 ? DtmfDigits.IndexOf(char.ToUpperInvariant(digit[0])) : -1;
        if (c == null || code < 0) return;
        if (c.State != CallState.Connected || c.Held || c.HeldByThem)
        {
            Log?.Invoke($"call {c.Id}: DTMF '{digit}' not sent: the call is {(c.State != CallState.Connected ? "not connected" : "on hold")}");
            return;
        }
        var (rtp, relay, ua) = (c.Rtp, c.Relay, c.Ua);
        if (rtp != null && NegotiatedDtmf(rtp) is { } ev)
        {
            bool sent = relay != null && await relay.SendDtmfAsync((byte)code, ev.ID, ev.ClockRate());
            Log?.Invoke(sent ? $"call {c.Id}: DTMF '{digit}' sent (RFC 4733, telephone-event/{ev.ClockRate()} pt {ev.ID})"
                : $"call {c.Id}: DTMF '{digit}' not sent: no audio going to them yet");
            return;
        }
        if (ua?.Dialogue is not { } dialogue)
        {
            Log?.Invoke($"call {c.Id}: DTMF '{digit}' not sent: no SIP dialog");
            return;
        }
        var info = dialogue.GetInDialogRequest(SIPMethodsEnum.INFO);
        info.Body = $"Signal={digit}\r\nDuration=160\r\n";
        info.Header.ContentType = "application/dtmf-relay";
        info.Header.ContentLength = info.Body.Length;
        var tx = new SIPNonInviteTransaction(_transport, info, null);
        tx.NonInviteTransactionFinalResponseReceived += (_, _, _, resp) =>
        {
            Log?.Invoke($"call {c.Id}: DTMF '{digit}' by SIP INFO: {resp.ShortDescription}");
            return Task.FromResult(System.Net.Sockets.SocketError.Success);
        };
        Log?.Invoke($"call {c.Id}: DTMF '{digit}': their SDP has no telephone-event, sending SIP INFO");
        tx.SendRequest();
    }

    /// <summary>
    /// A SIP INFO in one of our calls. SIPSorcery's user agent leaves INFO alone (it doesn't own
    /// the transport), so it's answered here: 200 for DTMF (application/dtmf-relay "Signal=5", or
    /// application/dtmf "5"), 415 for anything else, 481 if it isn't in a call of ours.
    /// </summary>
    private async Task OnInfo(SIPRequest req)
    {
        var c = Calls.FirstOrDefault(x => x.Live && x.Ua?.Dialogue?.CallId == req.Header.CallId);
        string? digit = null;
        var type = req.Header.ContentType ?? "";
        if (type.StartsWith("application/dtmf-relay", StringComparison.OrdinalIgnoreCase))
        {
            var line = (req.Body ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Signal", StringComparison.OrdinalIgnoreCase));
            digit = line?.Split('=', 2).ElementAtOrDefault(1)?.Trim();
        }
        else if (type.StartsWith("application/dtmf", StringComparison.OrdinalIgnoreCase)) digit = req.Body?.Trim();
        bool ok = digit?.Length == 1 && DtmfDigits.Contains(char.ToUpperInvariant(digit[0]));
        var status = c == null ? SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist
            : ok ? SIPResponseStatusCodesEnum.Ok : SIPResponseStatusCodesEnum.UnsupportedMediaType;
        await _transport.SendResponseAsync(SIPResponse.GetResponse(req, status, null));
        if (c != null && ok) OnDtmfIn(c, char.ToUpperInvariant(digit![0]).ToString(), "SIP INFO");
    }

    private void OnDtmfIn(Call c, string digit, string via)
    {
        Log?.Invoke($"call {c.Id}: DTMF '{digit}' received ({via})");
        DtmfReceived?.Invoke(c, digit, via);
    }

    /// <summary>
    /// Adds text to a call that doesn't have it: a re-INVITE with m=text. If they accept, text
    /// starts (see <see cref="OnRemoteDescriptionChanged"/>); if not within 8 s, it's refused.
    /// </summary>
    public void AddText(string id)
    {
        var c = Find(id);
        if (c == null) return;
        lock (_lock)
        {
            if (c.State != CallState.Connected || c.Ua == null || c.Rtp == null || c.Rtt == null || c.TextState is "on" or "adding") return;
            c.TextState = "adding";
            c.AddTextTimer?.Dispose();
            c.AddTextTimer = new System.Threading.Timer(_ =>
            {
                if (Interlocked.Exchange(ref c.AddTextTimer, null) is not { } t) return;
                t.Dispose();
                if (c.TextState != "adding") return;
                c.TextState = "refused";
                Log?.Invoke("they didn't take text");
                Changed?.Invoke();
            }, null, 8000, Timeout.Infinite);
            Log?.Invoke("adding text: re-INVITE with m=text");
            c.Ua.AddText();
        }
        Changed?.Invoke();
    }

    private void AddVideoDone(Call c, bool ok)
    {
        if (Interlocked.Exchange(ref c.AddVideoTimer, null) is not { } timer) return;
        timer.Dispose();
        if (!ok)
        {
            Log?.Invoke("they didn't take video; the call carries on as audio");
            VideoRefused?.Invoke(c);
        }
    }

    /// <summary>
    /// Any new SDP from the far end: an answer to our re-INVITE, or their re-INVITE. If it adds
    /// video to a call that didn't have it, the relay is rebuilt so the page gets a video line.
    /// </summary>
    private void OnRemoteDescriptionChanged(Call c, RTPSession rtp)
    {
        bool video = MediaRelay.HasNegotiatedVideo(rtp);
        // Our hold or resume is answered: the next one (if asked for) goes shortly, once this
        // re-INVITE is ACKed, not while it's still open.
        if (c.Rtp == rtp) try { c.HoldTimer?.Change(HoldSettleMs, Timeout.Infinite); } catch (ObjectDisposedException) { }
        if (c.AddVideoTimer != null) AddVideoDone(c, video);
        // Text added by either side: start it once the user agent has finished with the SDP.
        if (c.Rtp == rtp && c.TextState != "on" && HasNegotiatedText(rtp))
            _ = Task.Run(async () => { await Task.Delay(200); if (c.Rtp == rtp) await StartTextIfAgreed(c, rtp); });
        // Their answer to our adding text turned it down (m=text with port 0): say so now, not at the timeout.
        else if (c.Rtp == rtp && rtp is not TextPeerConnection && c.TextState == "adding" && Interlocked.Exchange(ref c.AddTextTimer, null) is { } t)
        {
            t.Dispose();
            c.TextState = "refused";
            Log?.Invoke("they turned down text");
            Changed?.Invoke();
        }
        if (!video || c.Rtp != rtp || c.Relay == null || c.Relay.Video.HasValue) return;
        // Let the SIP user agent finish (send its answer, start the stream) before rebuilding.
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            lock (_lock)
            {
                if (c.Rtp != rtp || c.State != CallState.Connected) return;
                c.Relay?.Dispose();
                c.Relay = null;
            }
            Log?.Invoke("video added to the call");
            await StartRelayAsync(c, rtp);
        });
    }

    private async Task ConnectedAsync(Call c, RTPSession rtp)
    {
        rtp.OnRemoteDescriptionChanged += _ => OnRemoteDescriptionChanged(c, rtp);
        c.ConnectedAt ??= DateTime.Now;
        SetState(c, CallState.Connected);
        await StartTextIfAgreed(c, rtp);
        await StartRelayAsync(c, rtp);
    }

    private async Task StartRelayAsync(Call c, RTPSession rtp)
    {
        var relay = new MediaRelay(rtp, s => Log?.Invoke(s)) { AudioLossPercent = AudioLossPercent, VideoLossPercent = VideoLossPercent };
        relay.TxLostChanged += lost => TxLostChanged?.Invoke(c, lost);
        relay.Quality += sample => Quality?.Invoke(c, sample);
        c.Relay = relay;
        Log?.Invoke($"call connected: {relay.Describe()}");
        Details?.Invoke(c, DescribeCall(c, rtp, relay));
        MediaOffer?.Invoke(c, await relay.CreateOfferAsync());
    }

    private static MediaStream? StreamFor(RTPSession rtp, SDPMediaTypesEnum kind) => kind switch
    {
        SDPMediaTypesEnum.audio => rtp.AudioStream,
        SDPMediaTypesEnum.video => rtp.VideoStream,
        SDPMediaTypesEnum.text => rtp.TextStream,
        _ => null,
    };

    private static CallDetails DescribeCall(Call c, RTPSession rtp, MediaRelay relay)
    {
        var dialogue = c.Ua?.Dialogue;
        var ep = dialogue?.RemoteSIPEndPoint;
        var a = c.Account;
        var media = rtp.RemoteDescription?.Media.Where(m => m.Port != 0).ToList() ?? new();
        bool mediaEncrypted = media.Count > 0 && media.All(m => m.Transport.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || StreamFor(rtp, m.Media)?.IsSecurityContextReady() == true);
        static string? Codec(SDPAudioVideoMediaFormat? f) =>
            f is { } x ? $"{x.Name()} {(x.ClockRate() % 1000 == 0 ? $"{x.ClockRate() / 1000} kHz" : $"{x.ClockRate()} Hz")}" : null;
        return new CallDetails(
            Direction: c.Outgoing ? "outgoing" : "incoming",
            Account: a?.Aor,
            Signalling: ep == null ? null : $"{ep.Protocol.ToString().ToUpperInvariant()} {ep.Address}:{ep.Port}",
            TheirPhone: string.IsNullOrWhiteSpace(c.TheirPhone) ? null : c.TheirPhone,
            CallId: dialogue?.CallId,
            TheirMedia: rtp.HasAudio ? rtp.AudioStream.DestinationEndPoint?.ToString() : null,
            // Every stream encrypted: WebRTC's DTLS (UDP/TLS/RTP/SAVP, and the data channel's
            // DTLS/SCTP), or SRTP with keys agreed in the SDP, which best effort does on RTP/AVP
            // lines too, so it's the stream that says. SIP over TLS shows as tls.
            MediaEncrypted: mediaEncrypted,
            SignallingEncrypted: ep?.Protocol is SIPProtocolsEnum.tls or SIPProtocolsEnum.wss,
            Audio: Codec(relay.Audio),
            Video: relay.Video is { } v ? v.Name() : null,
            // How: "DTLS + SRTP" when the keys came from a DTLS handshake on the media path
            // (WebRTC, a browser phone), "SRTP" when they were in the SDP (SDES), which is only as
            // private as the SIP.
            MediaSecurity: !mediaEncrypted ? null
                : media.All(m => m.Transport.Contains("TLS", StringComparison.OrdinalIgnoreCase)) ? "DTLS + SRTP" : "SRTP");
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
        if (req.Method == SIPMethodsEnum.INFO) { await OnInfo(req); return; }
        if (req.Method == SIPMethodsEnum.BYE) { TransferorHungUp(req); return; }
        if (req.Method != SIPMethodsEnum.INVITE || !string.IsNullOrEmpty(req.Header.To.ToTag)) return;
        // An INVITE with Replaces (RFC 3891) is someone's attended transfer taking over one of
        // our calls: that call's user agent answers it (ReplacementArriving). One for a call we
        // don't have is answered 481, as the RFC says.
        if (!string.IsNullOrWhiteSpace(req.Header.Replaces))
        {
            var replaces = SIPReplacesParameter.Parse(req.Header.Replaces);
            if (replaces != null && Calls.Any(x => x.State == CallState.Connected && x.Ua?.Dialogue?.CallId == replaces.CallID)) return;
            Log?.Invoke($"INVITE with Replaces for a call we don't have ({req.Header.Replaces}): 481");
            await _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, "No call to replace"));
            return;
        }

        Call c;
        lock (_lock)
        {
            if (LiveCount >= MaxCalls)
            {
                _ = _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BusyHere, null));
                return;
            }
            c = NewCall();
            // A browser phone's Contact is an address nothing can reach (sip:x@y.invalid;transport=ws,
            // RFC 7118): our requests in the call (BYE, re-INVITEs) only get to it over the
            // WebSocket connection its INVITE came on. As the outbound proxy, they go there.
            var ua = NewUserAgent(c, remote.Protocol is SIPProtocolsEnum.ws or SIPProtocolsEnum.wss ? remote : null);
            c.Ua = ua;
            c.Incoming = ua.AcceptCall(req);
            ua.ServerCallCancelled += (_, _) => End(c, "Missed call");
            c.Remote = req.Header.From.FromURI.ToString();
            c.Outgoing = false;
            c.Account = AccountForIncoming(req);
            c.TheirPhone = req.Header.UserAgent;
            c.IncomingOffer = req.Body;
            // A call from a browser phone is answered with WebRTC media: one over WebSocket, or
            // one whose offer is WebRTC's (it came through a server, so over UDP, TCP or TLS).
            c.WebRtc = remote.Protocol is SIPProtocolsEnum.ws or SIPProtocolsEnum.wss || IsWebRtcOffer(req.Body);
            var name = req.Header.From.FromName;
            if (!string.IsNullOrWhiteSpace(name)) c.Remote = $"{name} <{c.Remote}>";
            c.IncomingVideo = _settings.OfferVideo && OffersVideo(req.Body);
            SetState(c, CallState.Ringing);
        }
        c.Incoming!.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
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
    private SIPUserAgent NewUserAgent(Call c, SIPEndPoint? outboundProxy = null)
    {
        var ua = new SIPUserAgent(_transport, outboundProxy);
        // Only the call's current user agent, once connected. The user agent raises this too when
        // a call we place fails with WebRTC media (closing the RTCPeerConnection), and for a
        // turned-down attempt DialAsync retries; there, DialAsync gives the real reason.
        ua.OnCallHungup += _ =>
        {
            if (c.HangingUp || c.Ua != ua || c.State != CallState.Connected) return;
            // Hanging up on us while our transfer's going (and hasn't failed) is it done: the
            // transferee does when the target answers, and so does the target of an attended one.
            if (c.Transfer is { Role: "out", Status: "calling" or "done" } t) EndTransferred(c, t.Target);
            // A transfer you'd cancelled that their phone went ahead with (SIP can't call one back).
            else if (c.CancelledTransferTo is { } to)
            {
                c.TransferNote = $"Transferred to {to} (their phone went ahead after you cancelled)";
                End(c, c.TransferNote);
            }
            else End(c, "They hung up");
        };
        // Transfer (see BlindTransfer and TransferRequested).
        ua.OnTransferNotify += sipfrag => OnTransferNotify(c, ua, sipfrag);
        ua.OnTransferRequested += (referTo, referredBy) => TransferRequested(c, ua, referTo, referredBy);
        ua.OnTransferCallDescriptorCreated += (d, refer) => TransferCallDescriptor(c, d, refer);
        ua.OnTransferToTargetSuccessful += _ => TransferOutcome(c, ua, true);
        ua.OnTransferToTargetFailed += _ => TransferOutcome(c, ua, false);
        ua.ClientCallTrying += (uac, _) => { if (c.Transfer is { Role: "in", Status: "calling" } t && c.Ua == ua) TargetCallStarted(c, t, uac); };
        ua.ClientCallRinging += (uac, _) =>
        {
            if (c.Transfer is not { Role: "in", Status: "calling" } t || c.Ua != ua) return;
            TargetCallStarted(c, t, uac);
            if (t.Cancelled) return;
            t.Rang = true;
            NotifyTransferor(c, t, "180 Ringing", false);
            t.Detail = t.TransferorGone ? $"{t.By} hung up. {t.Target} is ringing…" : $"{t.By} is transferring you to {t.Target}: ringing…";
            Changed?.Invoke();
        };
        ua.ClientCallFailed += (_, err, resp) => { if (c.Transfer is { Role: "in", Status: "calling" } t && c.Ua == ua) (t.Failure, t.FailureError) = (resp, err); };
        ua.OnAttendedTransferRequested += invite => ReplacementArriving(c, ua, invite);
        ua.OnAttendedTransferAccepted += _ => ReplacementDone(c, ua);
        ua.RemotePutOnHold += () => { if (c.Ua != ua) return; c.HeldByThem = true; Log?.Invoke($"{c.Remote} put the call on hold"); Changed?.Invoke(); };
        ua.RemoteTookOffHold += () => { if (c.Ua != ua) return; c.HeldByThem = false; Log?.Invoke($"{c.Remote} took the call off hold"); Changed?.Invoke(); };
        // Keypad tones they send as RFC 4733 telephone-events: once per tone, at its start.
        ua.OnDtmfTone += (tone, _) => { if (c.Ua == ua && tone < DtmfDigits.Length) OnDtmfIn(c, DtmfDigits[tone].ToString(), "RFC 4733"); };
        // They add video to a call that has none of ours yet (an audio call we placed: its
        // session was made without video): put our video codecs on it before the answer is
        // written, or the answer turns video down (m=video 0). Video they add starts with our
        // camera off (see the page's onMediaOffer). Not on a WebRTC call, whose connection can't
        // take a track like this mid-call.
        ua.OnRemoteMediaStreamAdded += kind =>
        {
            if (kind != SDPMediaTypesEnum.video || c.Ua != ua || c.WebRtc || !_settings.OfferVideo || c.Rtp is not { } rtp || rtp is RTCPeerConnection) return false;
            MediaRelay.AddVideoTrack(rtp);
            Log?.Invoke($"{c.Remote} is adding video: taking it");
            return true;
        };
        return ua;
    }

    private void End(Call c, string reason)
    {
        lock (_lock)
        {
            if (c.State is CallState.Idle or CallState.Ended) return;
            c.EndReason = reason;
            Interlocked.Exchange(ref c.AddVideoTimer, null)?.Dispose();
            Interlocked.Exchange(ref c.AddTextTimer, null)?.Dispose();
            Interlocked.Exchange(ref c.HoldTimer, null)?.Dispose();
            c.Rtt?.Dispose();
            c.Rtt = null;
            c.Relay?.Dispose();
            c.Relay = null;
            try { c.Rtp?.Close(reason); } catch { }
            c.Rtp = null;
            c.Ua = null;
            c.Incoming = null;
            c.Held = c.HeldByThem = c.HoldWanted = false;
            c.EndedAt = DateTime.Now;
            SetState(c, CallState.Ended);
        }
        try { Ended?.Invoke(c); } catch (Exception ex) { Log?.Invoke($"recording call {c.Id} in history failed: {ex.Message}"); }
        // Kept for a moment, so the page can say how it ended, then dropped from the list.
        _ = Task.Delay(EndedKeepMs).ContinueWith(_ =>
        {
            lock (_lock) _calls.Remove(c);
            Changed?.Invoke();
        });
    }

    private void SetState(Call c, CallState state)
    {
        c.State = state;
        if (state != CallState.Ended) c.EndReason = null;
        Log?.Invoke($"call {c.Id} {state.ToString().ToLowerInvariant()} {c.Remote} {c.EndReason}".TrimEnd());
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
        foreach (var c in Calls.Where(c => c.State == CallState.Connected)) c.Ua?.Hangup();
        lock (_accounts) foreach (var reg in _accounts.Values) StopRegistration(reg);
        Thread.Sleep(300);
        _transport.Shutdown();
    }
}
