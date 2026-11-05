// The spike's far end: a SIP server on 127.0.0.1:5090 that answers any number of video calls and
// streams a pre-encoded clip on each, paced at 30 fps. The request URI's user part names the clip
// (sip:vp8-720p30@127.0.0.1:5090 plays media/vp8-720p30.ivf). Each call starts at a different
// point in the clip so the near end's decoders don't all hit keyframes on the same tick.
//
// It also counts the video the near end sends back (its camera) and prints rates every 5 seconds.
// A call with no RTCP from the near end for 20 seconds is hung up, so a crashed near end doesn't
// leave streams running into a closed port.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using VideoSpike;

int port = 5090;
string mediaDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "media");
for (int i = 0; i + 1 < args.Length; i += 2)
{
    if (args[i] == "--port") port = int.Parse(args[i + 1]);
    if (args[i] == "--media") mediaDir = args[i + 1];
}
bool trace = args.Contains("--trace");
// --any: RTP on all interfaces, for near ends that offer a LAN address (e.g. the desktop softphone).
var rtpBind = args.Contains("--any") ? IPAddress.Any : IPAddress.Loopback;
mediaDir = Path.GetFullPath(mediaDir);

var clips = Directory.GetFiles(mediaDir).Where(f => f.EndsWith(".ivf") || f.EndsWith(".h264"))
    .Select(Clip.Load).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
if (clips.Count == 0)
{
    Console.Error.WriteLine($"No clips in {mediaDir}. Run make-clips.ps1 first.");
    return 1;
}
foreach (var c in clips.Values)
{
    Console.WriteLine($"clip {c.Name}: {c.Frames.Count} frames, avg {c.Frames.Average(f => f.Length) / 1024:0.0} KB, max {c.Frames.Max(f => f.Length) / 1024:0.0} KB");
}

Clock.UseHighResolutionTimer();
var transport = new SIPTransport();
transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, port)));
if (trace)
{
    transport.SIPRequestInTraceEvent += (l, r, req) => Console.WriteLine($"<- {r} {req.StatusLine}");
    transport.SIPResponseOutTraceEvent += (l, r, resp) => Console.WriteLine($"-> {r} {resp.ShortDescription}");
    transport.SIPRequestOutTraceEvent += (l, r, req) => Console.WriteLine($"-> {r} {req.StatusLine}");
}
var calls = new ConcurrentDictionary<string, FarCall>();
int callCounter = 0;

transport.SIPTransportRequestReceived += async (localEP, remoteEP, req) =>
{
    try { await OnRequest(req); }
    catch (Exception ex) { Console.WriteLine($"request handling failed: {ex}"); }
};

async Task OnRequest(SIPRequest req)
{
    if (req.Method == SIPMethodsEnum.INVITE && string.IsNullOrEmpty(req.Header.To.ToTag))
    {
        var user = req.URI.User ?? "";
        if (!clips.TryGetValue(user, out var clip))
        {
            var notFound = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.NotFound, $"No clip {user}");
            await transport.SendResponseAsync(notFound);
            return;
        }

        var rtp = new RTPSession(false, false, false, rtpBind);
        rtp.AcceptRtpFromAny = true;
        rtp.addTrack(new MediaStreamTrack(SipLeg.FormatFor(clip.Codec), MediaStreamStatusEnum.SendRecv));
        rtp.MaxReconstructedVideoFrameSize = 4 * 1024 * 1024;

        var ua = new SIPUserAgent(transport, null);
        var uas = ua.AcceptCall(req);
        var n = Interlocked.Increment(ref callCounter);
        var call = new FarCall(ua, rtp, clip, offset: n * 7 % clip.Frames.Count);
        rtp.OnVideoFrameReceived += (_, _, frame, _) => { Interlocked.Increment(ref call.FramesIn); Interlocked.Add(ref call.BytesIn, frame.Length); };
        rtp.OnReceiveReport += (_, _, _) => call.LastRtcp = Stopwatch.GetTimestamp();
        ua.OnCallHungup += dlg => { calls.TryRemove(call.Id, out _); rtp.Close("hungup"); };

        if (await ua.Answer(uas, rtp))
        {
            calls[call.Id] = call;
            Console.WriteLine($"call {call.Id} answered: {clip.Name} to {rtp.VideoDestinationEndPoint}");
        }
        else
        {
            Console.WriteLine($"call {call.Id}: answer failed");
        }
    }
}

// One pacing thread for every call: frame k of each call goes out at start + k/30 s.
var sender = new Thread(() =>
{
    var start = Stopwatch.GetTimestamp();
    for (long tick = 0; ; tick++)
    {
        foreach (var call in calls.Values)
        {
            if (call.Rtp.IsClosed) continue;
            var frame = call.Clip.Frames[(int)((tick + call.Offset) % call.Clip.Frames.Count)];
            try
            {
                call.Rtp.SendVideoAt(call.TsBase + (uint)(tick * 3000), frame);
                call.FramesOut++;
            }
            catch { }
        }
        var due = start + (long)((tick + 1) * Stopwatch.Frequency / 30.0);
        while (Stopwatch.GetTimestamp() < due) Thread.Sleep(1);
    }
}) { IsBackground = true, Name = "sender", Priority = ThreadPriority.AboveNormal };
sender.Start();

Console.WriteLine($"Far end listening on sip:*@127.0.0.1:{port}. Ctrl-C to quit.");
var report = Stopwatch.StartNew();
long lastOut = 0, lastIn = 0;
while (true)
{
    Thread.Sleep(5000);
    var now = Stopwatch.GetTimestamp();
    foreach (var call in calls.Values.Where(c => (now - c.LastRtcp) > 20 * Stopwatch.Frequency))
    {
        Console.WriteLine($"call {call.Id}: no RTCP for 20 s, hanging up.");
        calls.TryRemove(call.Id, out _);
        try { call.UserAgent.Hangup(); } catch { }
        call.Rtp.Close("timeout");
    }

    long totalOut = FarCall.TotalOut(calls.Values), totalIn = FarCall.TotalIn(calls.Values);
    double secs = report.Elapsed.TotalSeconds;
    report.Restart();
    if (calls.Count > 0)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} calls={calls.Count} sending {(totalOut - lastOut) / secs:0} fps total, " +
            $"receiving {(totalIn - lastIn) / secs:0} fps total ({string.Join(" ", calls.Values.Select(c => c.Clip.Name).Distinct())})");
    }
    lastOut = totalOut;
    lastIn = totalIn;
}

sealed class FarCall(SIPUserAgent ua, RTPSession rtp, Clip clip, int offset)
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public SIPUserAgent UserAgent { get; } = ua;
    public RTPSession Rtp { get; } = rtp;
    public Clip Clip { get; } = clip;
    public int Offset { get; } = offset;
    public uint TsBase { get; } = (uint)Random.Shared.Next();
    public long FramesOut;
    public long FramesIn;
    public long BytesIn;
    public long LastRtcp = Stopwatch.GetTimestamp();

    // Cumulative totals survive calls being removed only approximately; they're for a console readout.
    public static long TotalOut(IEnumerable<FarCall> c) => c.Sum(x => x.FramesOut);
    public static long TotalIn(IEnumerable<FarCall> c) => c.Sum(x => Interlocked.Read(ref x.FramesIn));
}
