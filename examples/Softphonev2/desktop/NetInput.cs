using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Softphone;

/// <summary>
/// Network input: a WHIP endpoint (RFC 9725) on this PC that ffmpeg (<c>-f whip</c>), OBS or any
/// WHIP publisher can send video and sound to, for a call to use in place of your camera and
/// microphone.
///
/// The publisher's connection ends here, in C#, on a receive-only <see cref="RTCPeerConnection"/>,
/// and a <see cref="MediaRelay"/> passes what arrives on to the page, as it does a SIP call's media:
/// the page gets an offer (<see cref="Offer"/>), answers it (<see cref="Answer"/>), and has the
/// feed as one more source. It isn't answered by the page directly, because ffmpeg's WHIP muxer
/// (8.x) and the browser don't get on: the browser turns ffmpeg's own DTLS certificate down,
/// ffmpeg never answers the browser's consent checks once connected (so the browser drops it after
/// a few seconds), and it only sends as fast as packets come back. None of that matters to the
/// library, which takes ffmpeg as it is.
///
/// It listens on localhost only, so only programs on this PC can publish, and asks for a stream
/// key (a Bearer token, made up the first time it's turned on). One publisher at a time: a second
/// gets 409 until the first stops. A DELETE on the session's resource ends it, as does the
/// publisher's connection failing. Off until turned on in Settings; whether it's on, its port and
/// its key are kept per profile, the key encrypted by DPAPI as account passwords are.
/// </summary>
public sealed class NetInput : IDisposable
{
    public const int DefaultPort = 8097;
    private const int PortsToTry = 10;
    private const string SdpType = "application/sdp";

    private sealed class FileModel
    {
        public bool On { get; set; }
        public int Port { get; set; } = DefaultPort;
        public string? ProtectedToken { get; set; }
    }

    /// <summary>A publisher: its connection here, the relay on to the page, and its watchdog.</summary>
    private sealed class Publisher(string id, string from, RTCPeerConnection pc)
    {
        public string Id { get; } = id;
        public string From { get; } = from;
        public RTCPeerConnection Pc { get; } = pc;
        public MediaRelay? Relay { get; set; }
        public DateTime Started { get; } = DateTime.UtcNow;
        public long LastMediaTicks = DateTime.UtcNow.Ticks;   // the last RTP from it
        public bool Connected;
        public System.Threading.Timer? Watchdog { get; set; }
    }

    // A publisher killed without its DELETE (ffmpeg stopped with Ctrl+C twice, or crashed) leaves
    // the connection merely "disconnected", for a long time: the call would send its last frame,
    // frozen. So a publisher that's sent nothing for this long has gone, as has one that never connects.
    private static readonly TimeSpan SilentFor = TimeSpan.FromSeconds(4), ConnectWithin = TimeSpan.FromSeconds(20);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly object _lock = new();
    private readonly string _path;
    private readonly bool _save;   // false while a testing switch has set it up: nothing's written
    private HttpListener? _listener;
    private int _port = DefaultPort;
    private string _token = "";
    private Publisher? _pub;

    /// <summary>Turned on (in Settings, or by --whip-input).</summary>
    public bool On { get; private set; }
    /// <summary>The address publishers post to while listening, else null.</summary>
    public string? Address { get; private set; }
    /// <summary>The stream key publishers send as a Bearer token.</summary>
    public string Token => _token;
    /// <summary>Why it isn't listening, while it's on but couldn't start.</summary>
    public string? Error { get; private set; }
    /// <summary>The publisher's session, while one is connected or connecting.</summary>
    public string? Session { get { lock (_lock) return _pub?.Id; } }
    /// <summary>Where the publisher is.</summary>
    public string? From { get { lock (_lock) return _pub?.From; } }
    /// <summary>A note for the log if the file couldn't be read.</summary>
    public string? LoadNote { get; private set; }

    /// <summary>The relay's offer to the page for a publisher's media (session, SDP), answered with <see cref="Answer"/>.</summary>
    public event Action<string, string>? Offer;
    /// <summary>The publisher's gone (it stopped, or its connection failed): the page closes its connection.</summary>
    public event Action<string>? Ended;
    /// <summary>On, listening, a publisher: something the page shows has changed.</summary>
    public event Action? Changed;
    public event Action<string>? Log;

    public NetInput(string profile, int? testPort, string? testToken)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"netinput-{profile}.json");
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), JsonOpts) is { } f)
            {
                On = f.On;
                _port = f.Port is > 0 and < 65536 ? f.Port : DefaultPort;
                _token = f.ProtectedToken is { Length: > 0 } p ? AccountStore.Unprotect(p) : "";
            }
        }
        catch (Exception ex)
        {
            LoadNote = $"network input: couldn't read {Path.GetFileName(_path)} ({ex.Message}); it starts off";
        }
        if (testPort != null)
        {
            // --whip-input <port> [--whip-token <key>]: on for this run, nothing saved.
            _save = false;
            On = true;
            _port = testPort.Value;
            if (testToken != null) _token = testToken;
        }
        else _save = true;
        if (_token.Length == 0) _token = NewToken();
    }

    /// <summary>Starts listening if it's on. Called once the page is ready for a publisher's media.</summary>
    public void Start()
    {
        if (On) Listen();
    }

    /// <summary>Turns it on (listening) or off (a publisher dropped).</summary>
    public void SetOn(bool on)
    {
        if (on == On && (on ? _listener != null : _listener == null)) return;
        On = on;
        Save();
        if (on) Listen();
        else StopListening();
        Log?.Invoke($"network input {(on ? Address != null ? $"on: {Address}" : $"on, not listening: {Error}" : "off")}");
        Changed?.Invoke();
    }

    /// <summary>A new stream key: a publisher using the old one is dropped, and has to use this.</summary>
    public void NewKey()
    {
        _token = NewToken();
        Save();
        Drop(null, "the stream key changed");
        Log?.Invoke("network input: new stream key");
        Changed?.Invoke();
    }

    /// <summary>The page's answer to the relay's offer.</summary>
    public void Answer(string session, string sdp)
    {
        MediaRelay? relay;
        lock (_lock) relay = _pub?.Id == session ? _pub.Relay : null;
        if (relay?.SetAnswer(sdp) == false) Log?.Invoke("network input: the page's answer couldn't be applied");
    }

    /// <summary>The page has closed its end (it couldn't take the media, or its connection failed): the publisher goes too.</summary>
    public void Closed(string session) => Drop(session, "the page's connection closed");

    // The publisher (that one, or whichever if null) goes: its connection closed, the page told.
    private void Drop(string? session, string why)
    {
        Publisher? gone;
        lock (_lock)
        {
            if (_pub == null || (session != null && _pub.Id != session)) return;
            gone = _pub;
            _pub = null;
        }
        Log?.Invoke($"network input: publisher at {gone.From} gone ({why})");
        gone.Watchdog?.Dispose();
        try { gone.Relay?.Dispose(); } catch (Exception ex) { Log?.Invoke($"network input: relay: {ex.Message}"); }
        try { gone.Pc.close(); } catch (Exception ex) { Log?.Invoke($"network input: close: {ex.Message}"); }
        Ended?.Invoke(gone.Id);
        Changed?.Invoke();
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    private void Save()
    {
        if (!_save) return;
        try
        {
            var f = new FileModel { On = On, Port = _port, ProtectedToken = AccountStore.Protect(_token) };
            File.WriteAllText(_path, JsonSerializer.Serialize(f, JsonOpts));
        }
        catch (Exception ex)
        {
            Log?.Invoke($"network input: couldn't save {Path.GetFileName(_path)}: {ex.Message}");
        }
    }

    // localhost only: http://localhost:<port>/ needs no administrator rights (http://+:<port>/ would),
    // and nothing off this PC can reach it. The next port up if it's taken (another copy, say).
    private void Listen()
    {
        StopListening();
        Error = null;
        for (int p = _port; p < _port + PortsToTry && p < 65536; p++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{p}/");
            try
            {
                listener.Start();
            }
            catch (HttpListenerException)
            {
                listener.Close();
                continue;
            }
            _listener = listener;
            Address = $"http://localhost:{p}/whip";
            _ = AcceptLoop(listener);
            return;
        }
        Error = $"ports {_port} to {_port + PortsToTry - 1} are all in use";
        Address = null;
    }

    private void StopListening()
    {
        Drop(null, "network input turned off");
        var l = _listener;
        _listener = null;
        Address = null;
        if (l == null) return;
        try { l.Stop(); l.Close(); } catch (ObjectDisposedException) { }
    }

    private async Task AcceptLoop(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception) { return; }   // stopped
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (req.HttpMethod == "OPTIONS")
            {
                res.AddHeader("Accept-Post", SdpType);
                res.AddHeader("Allow", "OPTIONS, POST, DELETE");
                Reply(res, 204);
            }
            else if (path == "/whip" && req.HttpMethod == "POST") await Publish(req, res);
            else if (path.StartsWith("/whip/", StringComparison.Ordinal))
            {
                var session = path["/whip/".Length..];
                if (Session != session) Reply(res, 404, "No such session.");
                else if (!Authorised(req)) Unauthorised(res);
                else if (req.HttpMethod == "DELETE")
                {
                    Drop(session, "it stopped");
                    Reply(res, 200);
                }
                // No trickle ICE or ICE restarts: the answer carries every candidate.
                else if (req.HttpMethod == "PATCH") Reply(res, 405, "Trickle ICE isn't supported: the answer has every candidate.");
                else Reply(res, 405);
            }
            else Reply(res, 404, "The WHIP endpoint is /whip.");
        }
        catch (Exception ex)
        {
            Log?.Invoke($"network input: {req.HttpMethod} {req.Url?.AbsolutePath} failed: {ex.Message}");
            try { Reply(res, 500, ex.Message); } catch { /* the response is gone */ }
        }
    }

    private async Task Publish(HttpListenerRequest req, HttpListenerResponse res)
    {
        var who = req.RemoteEndPoint?.ToString() ?? "?";
        if (!Authorised(req))
        {
            Log?.Invoke($"network input: a publisher at {who} sent the wrong stream key");
            Unauthorised(res);
            return;
        }
        if (!(req.ContentType ?? "").StartsWith(SdpType, StringComparison.OrdinalIgnoreCase))
        {
            Reply(res, 415, "The offer has to be application/sdp.");
            return;
        }
        string offer;
        using (var r = new StreamReader(req.InputStream, Encoding.UTF8)) offer = await r.ReadToEndAsync();
        if (!offer.StartsWith("v=", StringComparison.Ordinal))
        {
            Reply(res, 400, "The body isn't an SDP offer.");
            return;
        }

        // Receive only: Opus, and the video codecs the page can decode. Bound to this PC's loopback
        // address, so the answer's one candidate is 127.0.0.1: the publisher is on this PC, and
        // a VPN's or virtual switch's interface never comes into it.
        var pc = new RTCPeerConnection(new RTCConfiguration { X_BindAddress = IPAddress.Loopback });
        pc.addTrack(new MediaStreamTrack(new List<AudioFormat> { AudioCommonlyUsedFormats.OpusWebRTC }, MediaStreamStatusEnum.RecvOnly));
        pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { new(VideoCodecsEnum.H264, 102, 90000, "packetization-mode=1"), new(VideoCodecsEnum.VP8, 96) }, MediaStreamStatusEnum.RecvOnly));
        var pub = new Publisher(Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(), who, pc);
        lock (_lock)
        {
            if (_pub != null)
            {
                pc.close();
                Reply(res, 409, "Already receiving a stream: stop that one first.");
                Log?.Invoke($"network input: a second publisher at {who} turned away (409)");
                return;
            }
            _pub = pub;
        }

        string answer;
        try
        {
            answer = await WhipWhepServer.AnswerAsync(pc, offer);
        }
        catch (Exception ex)
        {
            Drop(pub.Id, $"its offer couldn't be answered: {ex.Message}");
            Reply(res, 422, $"The softphone couldn't take this stream: {ex.Message}");
            return;
        }
        pc.onconnectionstatechange += state =>
        {
            Log?.Invoke($"network input: publisher {state}");
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed) Drop(pub.Id, $"its connection {state}");
            else if (state == RTCPeerConnectionState.connected) { pub.Connected = true; Changed?.Invoke(); }
        };
        pc.OnRtpPacketReceived += (_, _, _) => Interlocked.Exchange(ref pub.LastMediaTicks, DateTime.UtcNow.Ticks);
        pub.Watchdog = new System.Threading.Timer(_ =>
        {
            var now = DateTime.UtcNow;
            if (!pub.Connected && now - pub.Started > ConnectWithin) Drop(pub.Id, $"it didn't connect within {ConnectWithin.TotalSeconds:0} s");
            else if (pub.Connected && now - new DateTime(Interlocked.Read(ref pub.LastMediaTicks), DateTimeKind.Utc) > SilentFor) Drop(pub.Id, $"nothing from it for {SilentFor.TotalSeconds:0} s");
        }, null, 1000, 1000);

        res.StatusCode = 201;
        res.ContentType = SdpType;
        res.AddHeader("Location", $"/whip/{pub.Id}");
        var body = Encoding.UTF8.GetBytes(answer);
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
        res.Close();
        Log?.Invoke($"network input: answered {who} (session {pub.Id})");

        // On to the page, as a call's media goes: the relay's offer, for the page to answer.
        try
        {
            var relay = new MediaRelay(pc, s => Log?.Invoke($"network input {s}"));
            lock (_lock)
            {
                if (_pub != pub) { relay.Dispose(); return; }
                pub.Relay = relay;
            }
            Log?.Invoke($"network input: {relay.Describe()}");
            Offer?.Invoke(pub.Id, await relay.CreateOfferAsync());
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Drop(pub.Id, $"the relay to the page couldn't start: {ex.Message}");
        }
    }

    private bool Authorised(HttpListenerRequest req)
    {
        var auth = req.Headers["Authorization"] ?? "";
        const string bearer = "Bearer ";
        if (!auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(auth[bearer.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(_token));
    }

    private static void Unauthorised(HttpListenerResponse res)
    {
        res.AddHeader("WWW-Authenticate", "Bearer");
        Reply(res, 401, "Wrong or missing stream key (a Bearer token).");
    }

    private static void Reply(HttpListenerResponse res, int status, string? text = null)
    {
        res.StatusCode = status;
        if (text != null)
        {
            var body = Encoding.UTF8.GetBytes(text + "\n");
            res.ContentType = "text/plain; charset=utf-8";
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body);
        }
        res.Close();
    }

    public void Dispose() => StopListening();
}
