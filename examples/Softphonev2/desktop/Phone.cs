using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Softphone;

/// <summary>
/// The SIP side of the prototype: one transport, an optional registration, and at most one call.
/// Everything the page needs to show is reported through <see cref="Changed"/> as a small state
/// object; the page's WebRTC side is handed over to <see cref="MediaRelay"/> once a call connects.
/// A second incoming call while one is in progress is answered 486 Busy Here.
/// </summary>
public sealed class Phone : IDisposable
{
    public enum CallState { Idle, Calling, Ringing, Connected, Ended }

    private readonly Settings _settings;
    private readonly SIPTransport _transport = new();
    private SIPRegistrationUserAgent? _registration;
    private SIPUserAgent? _ua;
    private SIPServerUserAgent? _incoming;
    private RTPSession? _rtp;
    private MediaRelay? _relay;
    private readonly object _lock = new();

    public CallState State { get; private set; } = CallState.Idle;
    public string Remote { get; private set; } = "";
    public string? EndReason { get; private set; }
    public string Registration { get; private set; } = "none";
    public string? RegistrationDetail { get; private set; }

    /// <summary>Raised on a SIP thread whenever anything the page shows changes.</summary>
    public event Action? Changed;
    /// <summary>A connected call's relay needs the page's answer to this offer.</summary>
    public event Action<string>? MediaOffer;
    public event Action<string>? Log;
    /// <summary>The far end's RTCP says our audio is (true) or is again not (false) failing to reach them.</summary>
    public event Action<bool>? TxLostChanged;
    /// <summary>Full SIP messages, for the log file only.</summary>
    public event Action<string>? Trace;

    public Phone(Settings settings)
    {
        _settings = settings;
        OpenChannels(settings.SipPort);
        _transport.SIPTransportRequestReceived += OnRequest;
        _transport.SIPRequestOutTraceEvent += (l, r, req) => { Log?.Invoke($"-> {r} {req.StatusLine}"); Trace?.Invoke($"-> {r}\n{req}"); };
        _transport.SIPRequestInTraceEvent += (l, r, req) => { Log?.Invoke($"<- {r} {req.StatusLine}"); Trace?.Invoke($"<- {r}\n{req}"); };
        _transport.SIPResponseInTraceEvent += (l, r, resp) => { Log?.Invoke($"<- {r} {resp.ShortDescription}"); Trace?.Invoke($"<- {r}\n{resp}"); };
        _transport.SIPResponseOutTraceEvent += (l, r, resp) => { Log?.Invoke($"-> {r} {resp.ShortDescription}"); Trace?.Invoke($"-> {r}\n{resp}"); };
    }

    /// <summary>Set when the configured SIP port was taken and a random one was used instead.</summary>
    public string? PortNote { get; private set; }

    /// <summary>
    /// Listens for SIP on UDP and TCP on the same port: the configured one if it's free, otherwise
    /// a random one (another softphone often has 5060). Incoming direct calls then need the new
    /// port, but registered calls and outgoing calls work as normal.
    /// </summary>
    private void OpenChannels(int port)
    {
        if (TryOpenChannels(port)) return;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (TryOpenChannels(0))
            {
                PortNote = $"SIP port {port} is in use, using {_transport.GetSIPChannels().First().Port} instead";
                return;
            }
        }
        throw new ApplicationException($"Unable to find a free port for SIP (port {port} is in use).");
    }

    /// <summary>Opens UDP then TCP on the port (0 = any free one, which TCP then shares), or neither.</summary>
    private bool TryOpenChannels(int port)
    {
        SIPUDPChannel? udp = null;
        try
        {
            udp = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, port));
            var tcp = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, udp.Port));
            _transport.AddSIPChannel(udp);
            _transport.AddSIPChannel(tcp);
            return true;
        }
        catch (Exception ex) when (ex is ApplicationException or System.Net.Sockets.SocketException)
        {
            udp?.Close();
            return false;
        }
    }

    public string ListeningOn =>string.Join(", ", _transport.GetSIPChannels().Select(c => c.ListeningSIPEndPoint.ToString()));

    public void Start()
    {
        if (!_settings.HasAccount) return;
        var a = _settings.Account!;
        _registration = new SIPRegistrationUserAgent(_transport, a.Username, a.Password, a.Server, 120);
        _registration.RegistrationSuccessful += (_, _) => SetRegistration("registered", null);
        _registration.RegistrationTemporaryFailure += (_, _, err) => SetRegistration("failed", err);
        _registration.RegistrationFailed += (_, _, err) => SetRegistration("failed", err);
        _registration.RegistrationRemoved += (_, _) => SetRegistration("none", null);
        SetRegistration("registering", null);
        _registration.Start();
    }

    /// <summary>
    /// Turns what was typed into a SIP URI: a full sip: URI as is, user@host as a sip: URI, and a
    /// bare number or name as a user at the account's server.
    /// </summary>
    public string ResolveAddress(string typed)
    {
        typed = typed.Trim();
        if (typed.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) || typed.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) return typed;
        if (typed.Contains('@')) return "sip:" + typed;
        if (_settings.HasAccount) return $"sip:{typed}@{_settings.Account!.Server}";
        return "sip:" + typed;   // e.g. an IP address or host name
    }

    /// <summary>Places a call: a video call offers audio and video, an audio call leaves video out (it can be added later).</summary>
    public async Task DialAsync(string typed, bool video)
    {
        SIPUserAgent ua;
        RTPSession rtp;
        lock (_lock)
        {
            if (State is CallState.Calling or CallState.Ringing or CallState.Connected) return;
            Remote = ResolveAddress(typed);
            ua = NewUserAgent();
            rtp = MediaRelay.CreateSipSession(video && _settings.OfferVideo);
            (_ua, _rtp) = (ua, rtp);
            SetState(CallState.Calling);
        }

        string? failure = null;
        ua.ClientCallFailed += (_, err, resp) => failure = resp != null ? $"{resp.StatusCode} {resp.ReasonPhrase}" : err;
        var a = _settings.Account;
        bool ok;
        try
        {
            ok = await ua.Call(Remote, a?.Username, a?.Password, rtp);
        }
        catch (Exception ex)
        {
            ok = false;
            failure = ex.Message;
        }

        if (ok) await ConnectedAsync(rtp);
        else if (_ua == ua) End(failure ?? "Call failed");
    }

    public async Task AnswerAsync()
    {
        SIPUserAgent? ua;
        SIPServerUserAgent? uas;
        RTPSession rtp;
        lock (_lock)
        {
            if (State != CallState.Ringing || _incoming == null) return;
            (ua, uas) = (_ua, _incoming);
            rtp = MediaRelay.CreateSipSession(_settings.OfferVideo);
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
            if (State == CallState.Calling) _ua?.Cancel();
            else if (State == CallState.Connected) _ua?.Hangup();
            else if (State == CallState.Ringing) { Decline(); return; }
            else return;
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
        await StartRelayAsync(rtp);
    }

    private async Task StartRelayAsync(RTPSession rtp)
    {
        var relay = new MediaRelay(rtp, s => Log?.Invoke(s));
        relay.TxLostChanged += lost => TxLostChanged?.Invoke(lost);
        _relay = relay;
        Log?.Invoke($"call connected: {relay.Describe()}");
        MediaOffer?.Invoke(await relay.CreateOfferAsync());
    }

    private async Task OnRequest(SIPEndPoint local, SIPEndPoint remote, SIPRequest req)
    {
        if (req.Method != SIPMethodsEnum.INVITE || !string.IsNullOrEmpty(req.Header.To.ToTag)) return;

        lock (_lock)
        {
            if (State is CallState.Calling or CallState.Ringing or CallState.Connected)
            {
                _ = _transport.SendResponseAsync(SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.BusyHere, null));
                return;
            }
            var ua = NewUserAgent();
            _ua = ua;
            _incoming = ua.AcceptCall(req);
            ua.ServerCallCancelled += (_, _) => End("Missed call");
            Remote = req.Header.From.FromURI.ToString();
            var name = req.Header.From.FromName;
            if (!string.IsNullOrWhiteSpace(name)) Remote = $"{name} <{Remote}>";
            SetState(CallState.Ringing);
        }
        _incoming!.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
        await Task.CompletedTask;
    }

    private SIPUserAgent NewUserAgent()
    {
        var ua = new SIPUserAgent(_transport, null);
        ua.OnCallHungup += _ => End("They hung up");
        return ua;
    }

    private void End(string reason)
    {
        lock (_lock)
        {
            if (State is CallState.Idle or CallState.Ended) return;
            EndReason = reason;
            Interlocked.Exchange(ref _addVideoTimer, null)?.Dispose();
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

    private void SetRegistration(string state, string? detail)
    {
        Registration = state;
        RegistrationDetail = detail;
        Log?.Invoke($"registration {state} {detail}".TrimEnd());
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (State == CallState.Connected) _ua?.Hangup();
        _registration?.Stop();
        Thread.Sleep(300);
        _transport.Shutdown();
    }
}
