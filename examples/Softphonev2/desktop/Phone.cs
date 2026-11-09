using System.Net;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

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
    private SIPUserAgent? _hangingUp;
    private SIPServerUserAgent? _answering;   // the call we're hanging up ourselves
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

    // ---- Real-time text (RFC 4103): T.140 with RED redundancy, handled here in C# ----
    // Text isn't relayed to the page's peer connection: the page sends what's typed as host
    // messages and gets what arrives the same way.

    /// <summary>Text on the call: off (not set up), adding (re-INVITE out), on, refused, or disabled (--no-text).</summary>
    public string TextState { get; private set; } = "off";
    /// <summary>Characters from them, as they arrive: may include backspace (U+0008) and line separators.</summary>
    public event Action<string>? TextReceived;
    private RttEndPoint? _rtt;
    private System.Threading.Timer? _addTextTimer;
    private bool _outgoing;
    private string? _theirPhone;

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

    /// <summary>
    /// Places a call: a video call offers audio and video, an audio call leaves video out (it can
    /// be added later). If they turn the offer down because of its media profile (see
    /// <see cref="WantsOtherMedia"/>), it's placed once more straight away with the other kind:
    /// WebRTC instead of plain RTP, or the other way round.
    /// </summary>
    public async Task DialAsync(string typed, bool video)
    {
        SIPUserAgent ua;
        RTPSession rtp;
        lock (_lock)
        {
            if (State is CallState.Calling or CallState.Ringing or CallState.Connected) return;
            Remote = ResolveAddress(typed);
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
                    Log?.Invoke($"they can't take {(WebRtc ? "plain RTP" : "WebRTC")} media (488, {response!.Header.Warning ?? "their SDP"}); calling again with {(WebRtc ? "WebRTC" : "plain RTP")}");
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
        ua.ClientCallFailed += (_, err, resp) =>
        {
            response = resp;
            failure = resp is { StatusCode: >= 200 and < 300 } ? "Their answer's media doesn't match ours"
                : resp is { Status: SIPResponseStatusCodesEnum.NotAcceptableHere } ? "They can't take this call's media (488)"
                : resp != null ? $"{resp.StatusCode} {resp.ReasonPhrase}" : err;
        };
        ua.ClientCallAnswered += (_, resp) => _theirPhone = resp?.Header.Server ?? resp?.Header.UserAgent;
        var a = _settings.Account;
        try
        {
            return (await ua.Call(Remote, a?.Username, a?.Password, rtp), failure, response);
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
    /// web softphone sends both when it gets a plain RTP offer. Any other 488 isn't retried.
    /// </summary>
    private static bool WantsOtherMedia(SIPResponse? resp, bool offeredWebRtc)
    {
        if (resp is not { Status: SIPResponseStatusCodesEnum.NotAcceptableHere }) return false;
        if (resp.Header.Warning is { } w && System.Text.RegularExpressions.Regex.IsMatch(w, @"(^|,)\s*302\s")) return true;
        if (string.IsNullOrWhiteSpace(resp.Body) || resp.Header.ContentType?.Contains("sdp", StringComparison.OrdinalIgnoreCase) != true) return false;
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
            rtp = NewSession(WebRtc ? IncomingVideo : _settings.OfferVideo);
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
    /// The SIP side's media session for a new call: WebRTC for a browser phone, plain RTP with
    /// real-time text otherwise. Text isn't offered to browsers yet (it would go over a data
    /// channel, RFC 8865), so on those calls it's "unavailable".
    /// </summary>
    private RTPSession NewSession(bool video)
    {
        var rtp = MediaRelay.CreateSipSession(video, WebRtc);
        if (rtp is RTCPeerConnection pc)
        {
            _rtt?.Dispose();
            _rtt = null;
            TextState = "unavailable";
            pc.onconnectionstatechange += state =>
            {
                Log?.Invoke($"WebRTC media (ICE, DTLS) {state}");
                if (state == RTCPeerConnectionState.failed && _rtp == pc) { _ua?.Hangup(); End("Media connection failed"); }
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

    /// <summary>Text is agreed on the session: their SDP has a text line with a port, and it isn't inactive.</summary>
    private static bool HasNegotiatedText(RTPSession rtp) =>
        rtp.RemoteDescription?.Media.Any(m => m.Media == SDPMediaTypesEnum.text && m.Port != 0) == true &&
        rtp.TextStream?.RemoteTrack != null && rtp.TextStream.RemoteTrack.StreamStatus != MediaStreamStatusEnum.Inactive;

    /// <summary>Starts sending and receiving text if it's agreed and not already on.</summary>
    private async Task StartTextIfAgreed(RTPSession rtp)
    {
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
        if (TextState == "on" && _rtt?.CanSendText == true) _rtt.SendText(text);
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
        else if (_rtp == rtp && TextState == "adding" && Interlocked.Exchange(ref _addTextTimer, null) is { } t)
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
            var ua = NewUserAgent();
            _ua = ua;
            _incoming = ua.AcceptCall(req);
            ua.ServerCallCancelled += (_, _) => End("Missed call");
            Remote = req.Header.From.FromURI.ToString();
            _outgoing = false;
            _theirPhone = req.Header.UserAgent;
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

    /// <summary>A user agent for a call.</summary>
    private SIPUserAgent NewUserAgent()
    {
        var ua = new SIPUserAgent(_transport, null);
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
