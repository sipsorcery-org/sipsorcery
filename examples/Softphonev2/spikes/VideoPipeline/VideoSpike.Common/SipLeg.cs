using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace VideoSpike;

/// <summary>One near-end call to the far end: a SIP user agent and a video-only RTP session.</summary>
public sealed class SipLeg
{
    public required int Index { get; init; }
    public required SIPUserAgent UserAgent { get; init; }
    public required RTPSession Rtp { get; init; }

    public static VideoFormat FormatFor(VideoCodecsEnum codec) => codec == VideoCodecsEnum.H264
        ? new VideoFormat(VideoCodecsEnum.H264, 102, 90000, "packetization-mode=1;profile-level-id=42e01f;level-asymmetry-allowed=1")
        : new VideoFormat(VideoCodecsEnum.VP8, 96);

    public static SIPTransport CreateTransport()
    {
        var transport = new SIPTransport();
        transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 0)));
        return transport;
    }

    public static async Task<SipLeg> CallAsync(SIPTransport transport, RunConfig cfg, int index)
    {
        var rtp = new RTPSession(false, false, false, IPAddress.Loopback);
        rtp.AcceptRtpFromAny = true;
        rtp.addTrack(new MediaStreamTrack(FormatFor(cfg.Codec), MediaStreamStatusEnum.SendRecv));
        // A 1080p keyframe at 4 Mbit/s can exceed the default reassembly limit.
        rtp.MaxReconstructedVideoFrameSize = 4 * 1024 * 1024;

        var ua = new SIPUserAgent(transport, null);
        var answered = await ua.Call(cfg.CallUri, null, null, rtp);
        if (!answered)
        {
            throw new ApplicationException($"Call {index} to {cfg.CallUri} was not answered.");
        }
        return new SipLeg { Index = index, UserAgent = ua, Rtp = rtp };
    }

    public static async Task<List<SipLeg>> CallAllAsync(SIPTransport transport, RunConfig cfg)
    {
        var legs = new List<SipLeg>();
        for (int i = 0; i < cfg.Streams; i++)
        {
            legs.Add(await CallAsync(transport, cfg, i));
        }
        return legs;
    }

    public void Hangup()
    {
        try { if (UserAgent.IsCallActive) UserAgent.Hangup(); } catch { }
        try { Rtp.Close("spike done"); } catch { }
    }
}
