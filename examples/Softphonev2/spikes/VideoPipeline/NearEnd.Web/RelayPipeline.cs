using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using SIPSorcery.Net;
using VideoSpike;

namespace NearEnd.Web;

/// <summary>
/// Path 1. Each SIP leg gets a local WebRTC connection to the page. Encoded frames from the SIP side
/// are re-sent on it with their original RTP timestamp (no decoding in C#), so the page can report
/// when each one reached the screen and C# can match it to when it arrived from SIP. The browser
/// sends the camera back the same way, and C# forwards those frames, still encoded, to the SIP legs:
/// Shared - only the first connection carries the camera and C# copies it to every leg;
/// PerCall - every connection carries the camera, so the browser runs one encoder per call.
/// </summary>
internal sealed class RelayPipeline(MainForm form, RunConfig cfg, List<SipLeg> legs) : IPipeline
{
    private sealed class Relay
    {
        public required int Index;
        public required SipLeg Leg;
        public required RTCPeerConnection Pc;
        public readonly ConcurrentDictionary<uint, double> RecvTimes = new();
        public volatile bool Connected;
        public long Received;
        public long CameraIn;
    }

    private readonly List<Relay> _relays = new();
    private long _cameraToSip;

    public async Task StartAsync()
    {
        var connected = new List<Task>();
        foreach (var leg in legs)
        {
            var pc = new RTCPeerConnection(new RTCConfiguration { X_UseRtpFeedbackProfile = true });
            pc.addTrack(new MediaStreamTrack(SipLeg.FormatFor(cfg.Codec), MediaStreamStatusEnum.SendRecv));
            var relay = new Relay { Index = leg.Index, Leg = leg, Pc = pc };
            _relays.Add(relay);

            var up = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connected.Add(up.Task);
            pc.onconnectionstatechange += s =>
            {
                if (s == RTCPeerConnectionState.connected) { relay.Connected = true; up.TrySetResult(); }
                else if (s is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed) relay.Connected = false;
            };

            leg.Rtp.OnVideoFrameReceived += (_, ts, frame, _) =>
            {
                relay.RecvTimes[ts] = Clock.NowMs;
                Interlocked.Increment(ref relay.Received);
                if (relay.Connected) pc.SendVideoAt(ts, frame);
            };

            pc.OnVideoFrameReceived += (_, ts, frame, _) =>
            {
                Interlocked.Increment(ref relay.CameraIn);
                IEnumerable<SipLeg> targets = cfg.CameraMode == CameraMode.Shared ? legs : [leg];
                foreach (var target in targets)
                {
                    if (target.Rtp.IsClosed) continue;
                    target.Rtp.SendVideoAt(ts, frame);
                    Interlocked.Increment(ref _cameraToSip);
                }
            };

            var offer = pc.createOffer();
            await pc.setLocalDescription(offer);
            var answer = await form.Request(new { type = "offer", tile = leg.Index, sdp = offer.sdp }, $"answer-{leg.Index}");
            var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answer["sdp"]!.GetValue<string>() });
            if (result != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException($"Tile {leg.Index}: the page's answer was rejected ({result}).");
            }
        }

        var all = Task.WhenAll(connected);
        if (await Task.WhenAny(all, Task.Delay(15000)) != all)
        {
            throw new ApplicationException("Not every local WebRTC connection came up within 15 s.");
        }
        BenchLink.Log("all local WebRTC connections up");
    }

    public void ResetCounters()
    {
        foreach (var r in _relays)
        {
            r.RecvTimes.Clear();
            Interlocked.Exchange(ref r.Received, 0);
            Interlocked.Exchange(ref r.CameraIn, 0);
        }
        Interlocked.Exchange(ref _cameraToSip, 0);
    }

    public void OnMessage(string type, JsonObject msg) { }

    public NearEndStats Collect(JsonObject page, double window)
    {
        var stats = new NearEndStats
        {
            LatencyMethod = "SIP receive in C# to the browser's expectedDisplayTime for that RTP timestamp " +
                            "(requestVideoFrameCallback), clocks aligned by ping calibration",
        };
        var offset = form.ClockOffsetMs;

        foreach (var t in page["tiles"]!.AsArray())
        {
            var tile = t!.AsObject();
            int index = tile["tile"]!.GetValue<int>();
            var ts = new TileStats
            {
                Tile = index,
                Kind = tile["kind"]!.GetValue<string>(),
                FramesPresented = tile["presented"]!.GetValue<long>(),
                Stalls = tile["stalls"]!.GetValue<int>(),
                LongestGapMs = Math.Round(tile["longestGap"]!.GetValue<double>(), 1),
                FramesDecoded = (long?)tile["framesDecoded"] ?? 0,
                AvgDecodeMs = Round((double?)tile["decodeMs"]),
                AvgJitterBufferMs = Round((double?)tile["jitterBufferMs"]),
                Decoder = (string?)tile["decoder"],
                HardwareDecode = (bool?)tile["powerEfficient"],
                Width = (int?)tile["width"] ?? 0,
                Height = (int?)tile["height"] ?? 0,
            };
            ts.PresentedFps = Math.Round(ts.FramesPresented / window, 1);

            if (ts.Kind == "remote" && index < _relays.Count)
            {
                var relay = _relays[index];
                ts.FramesReceived = Interlocked.Read(ref relay.Received);
                ts.FramesDropped = Math.Max(0, ts.FramesReceived - ts.FramesPresented);
                var display = new List<double>();
                var toBrowser = new List<double>();
                foreach (var s in tile["samples"]!.AsArray())
                {
                    var a = s!.AsArray();
                    uint rtpTs = (uint)a[0]!.GetValue<double>();
                    if (!relay.RecvTimes.TryGetValue(rtpTs, out var sipRecv)) continue;
                    toBrowser.Add(a[1]!.GetValue<double>() - offset - sipRecv);
                    display.Add(a[2]!.GetValue<double>() - offset - sipRecv);
                }
                ts.Latency = LatencySummary.From(display);
                ts.ToBrowserReceive = LatencySummary.From(toBrowser);
            }
            stats.Tiles.Add(ts);
        }

        if (page["camera"] is JsonObject cam)
        {
            long encoded = (long?)cam["framesEncoded"] ?? 0;
            int encoders = (int?)cam["encoders"] ?? 0;
            stats.Camera = new CameraStats
            {
                Source = (string?)cam["label"] ?? "",
                FramesCaptured = (long?)cam["framesCaptured"] ?? 0,
                Encoders = encoders,
                FramesEncoded = encoded,
                EncodeFpsPerEncoder = encoders > 0 ? Math.Round(encoded / (double)encoders / window, 1) : 0,
                AvgEncodeMs = Round((double?)cam["encodeMs"]),
                SentKbpsPerEncoder = Round((double?)cam["kbpsPerEncoder"]),
                Encoder = (string?)cam["encoder"],
                HardwareEncode = (bool?)cam["powerEfficient"],
                QualityLimitation = (string?)cam["qualityLimitation"],
                Width = (int?)cam["width"] ?? 0,
                Height = (int?)cam["height"] ?? 0,
                FramesSentToSip = Interlocked.Read(ref _cameraToSip),
            };
            stats.Camera.CaptureFps = Math.Round(stats.Camera.FramesCaptured / window, 1);
            stats.Camera.SentFpsPerLeg = Math.Round(stats.Camera.FramesSentToSip / (double)Math.Max(1, legs.Count) / window, 1);
        }
        return stats;
    }

    private static double? Round(double? v) => v.HasValue ? Math.Round(v.Value, 2) : null;

    public void Dispose()
    {
        foreach (var r in _relays) { try { r.Pc.close(); } catch { } }
    }
}
