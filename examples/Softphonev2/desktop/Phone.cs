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
    public string Registration { get; private set; } = "none";
    public string? RegistrationDetail { get; private set; }

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
    private bool _outgoing;
    private string? _theirPhone;
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
        _outgoing = true;
        ua.ClientCallAnswered += (_, resp) => _theirPhone = resp?.Header.Server ?? resp?.Header.UserAgent;
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
        var a = _settings.Account;
        var media = rtp.RemoteDescription?.Media.Where(m => m.Port != 0).ToList() ?? new();
        static string? Codec(SDPAudioVideoMediaFormat? f) =>
            f is { } x ? $"{x.Name()} {(x.ClockRate() % 1000 == 0 ? $"{x.ClockRate() / 1000} kHz" : $"{x.ClockRate()} Hz")}" : null;
        return new CallDetails(
            Direction: _outgoing ? "outgoing" : "incoming",
            Account: _settings.HasAccount ? $"{a!.Username}@{a.Server}" : null,
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
            _outgoing = false;
            _theirPhone = req.Header.UserAgent;
            var name = req.Header.From.FromName;
            if (!string.IsNullOrWhiteSpace(name)) Remote = $"{name} <{Remote}>";
            IncomingVideo = _settings.OfferVideo && OffersVideo(req.Body);
            SetState(CallState.Ringing);
        }
        _incoming!.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);
        await Task.CompletedTask;
    }

    private static bool OffersVideo(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp)) return true;
        try { return SDP.ParseSDPDescription(sdp).Media.Any(m => m.Media == SDPMediaTypesEnum.video && m.Port != 0); }
        catch { return true; }
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
