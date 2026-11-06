using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Softphone;

/// <summary>
/// Joins a call's SIP media (an <see cref="RTPSession"/>) to the page's WebRTC media (a local
/// <see cref="RTCPeerConnection"/>), so the browser engine does all the media work: microphone and
/// camera capture, echo cancellation, encoding, decoding and rendering. C# never decodes anything.
///
/// The peer connection offers exactly the codecs the SIP side negotiated, with the same payload
/// types, so media passes straight through:
///  - audio packet by packet, with its RTP timestamp and sequence number kept, so the browser's
///    jitter buffer sees the far end's real timing and losses;
///  - video frame by frame (depacketised, then packetised again for the other side) with its RTP
///    timestamp kept.
/// Keyframe requests (PLI/FIR) are forwarded in both directions, and each side is asked for a
/// keyframe once the relay is up, so video starts without waiting for the next periodic one.
/// </summary>
public sealed class MediaRelay : IDisposable
{
    private readonly RTPSession _sip;
    private readonly Action<string> _log;
    private RTCPeerConnection? _pc;
    private volatile bool _up;

    public SDPAudioVideoMediaFormat? Audio { get; }
    public SDPAudioVideoMediaFormat? Video { get; }

    /// <summary>
    /// Every audio codec both SIP ends can do, the one we send first. Each end of a SIP call may
    /// send a different one of the codecs both offered (here Opus one way and G722 the other is
    /// common), so the page must be ready to receive any of them, not just the one we send.
    /// </summary>
    private readonly List<SDPAudioVideoMediaFormat> _audioFormats = [];

    private long _audioIn, _audioOut, _videoIn, _videoOut, _pliIn, _pliOut;
    private volatile uint _sipVideoSsrc, _pageVideoSsrc;
    private Action<System.Net.IPEndPoint, SDPMediaTypesEnum, RTPPacket>? _sipRtp;
    private Action<System.Net.IPEndPoint, uint, byte[], VideoFormat>? _sipFrame;
    private Action<System.Net.IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket>? _sipReport;

    // One-way audio out: their RTCP receiver reports say how far into our audio they've got.
    private uint? _rrHighestSeq;
    private long _rrAudioOut;
    private bool _txLost;

    /// <summary>
    /// True when the far end's RTCP shows our audio isn't reaching them (one-way audio), false
    /// when it is again. Far ends that send no RTCP never raise it.
    /// </summary>
    public event Action<bool>? TxLostChanged;
    private System.Threading.Timer? _statsTimer;

    /// <summary>
    /// An RTCP reception report on the SIP leg, for the page's diagnostics. Dir "sent" is the far
    /// end's report about a stream we send; "received" is our own report about their video (the
    /// page measures received audio itself, but video is packetised again for it, so its loss
    /// there isn't the SIP leg's). Loss is the fraction lost since the previous report; Lost is
    /// the running total, worked out here from the fractions (some stacks, SIPSorcery among them,
    /// put the interval's loss rather than the cumulative one in the report). Round trip, for
    /// "sent", from the LSR/DLSR fields when they echo our sender reports.
    /// </summary>
    public sealed record QualitySample(string Dir, string Kind, double LossPct, long Lost, double JitterMs, double? RttMs);
    public event Action<QualitySample>? Quality;
    private Action<SDPMediaTypesEnum, RTCPCompoundPacket>? _sipSendReport;

    /// <summary>
    /// Simulated loss (the diagnostics sliders, or --loss-audio/--loss-video): the percentage of
    /// the audio and video packets we send on the SIP leg that are dropped instead, at random.
    /// Each dropped packet still uses up its sequence number, so the far end sees a real gap and
    /// counts it in its RTCP reports.
    /// </summary>
    public volatile int AudioLossPercent, VideoLossPercent;
    private readonly Dictionary<string, (uint Seq, double Lost)> _lossTotals = [];


    /// <summary>
    /// The audio and video codecs we offer on the SIP side, in order of preference. All are ones
    /// the browser can do. Video is offered for a video call, and when answering (so a video
    /// offer can be accepted, or video added later), unless the account has video turned off.
    /// </summary>
    public static RTPSession CreateSipSession(bool offerVideo = true)
    {
        var rtp = new RTPSession(false, false, false);
        rtp.addTrack(new MediaStreamTrack(new List<AudioFormat>
        {
            new(111, "OPUS", 48000, 2, "minptime=10;useinbandfec=1"),
            new(SDPWellKnownMediaFormatsEnum.G722),
            new(SDPWellKnownMediaFormatsEnum.PCMU),
            new(SDPWellKnownMediaFormatsEnum.PCMA),
        }));
        if (offerVideo) AddVideoTrack(rtp);
        return rtp;
    }

    /// <summary>Adds our video codecs to a session: at the start of a video call, or to add video to an audio one.</summary>
    public static void AddVideoTrack(RTPSession rtp)
    {
        if (rtp.HasVideo) return;
        rtp.addTrack(new MediaStreamTrack(new List<VideoFormat>
        {
            new(VideoCodecsEnum.VP8, 96),
            new(VideoCodecsEnum.H264, 102, 90000, "packetization-mode=1;profile-level-id=42e01f;level-asymmetry-allowed=1"),
        }));
        rtp.MaxReconstructedVideoFrameSize = 4 * 1024 * 1024;
    }

    /// <summary>
    /// True once both SIP ends have agreed on video: we have video codecs of our own, and their
    /// SDP has a video line that isn't turned down (port 0). A session can have a video stream
    /// without either: SIPSorcery makes one, with an inactive local track, for any m=video it's
    /// offered.
    /// </summary>
    public static bool HasNegotiatedVideo(RTPSession rtp) =>
        rtp.HasVideo && rtp.VideoLocalTrack is { StreamStatus: not MediaStreamStatusEnum.Inactive } &&
        rtp.RemoteDescription?.Media.Any(m => m.Media == SDPMediaTypesEnum.video && m.Port != 0) == true &&
        Negotiated(rtp.VideoStream).HasValue;

    public MediaRelay(RTPSession sip, Action<string> log)
    {
        _sip = sip;
        _log = log;
        Audio = Negotiated(sip.HasAudio ? sip.AudioStream : null);
        Video = HasNegotiatedVideo(sip) ? Negotiated(sip.VideoStream) : null;
        if (Audio.HasValue)
        {
            _audioFormats.Add(Audio.Value);
            var both = SDPAudioVideoMediaFormat.GetCompatibleFormats(sip.AudioStream.LocalTrack.Capabilities, sip.AudioStream.RemoteTrack.Capabilities);
            _audioFormats.AddRange(both.Where(f => f.ID != Audio.Value.ID && !f.Name().Equals("telephone-event", StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>The codec agreed on the SIP side for a stream, or null if the far end turned the stream down.</summary>
    private static SDPAudioVideoMediaFormat? Negotiated(MediaStream? stream)
    {
        if (stream?.RemoteTrack == null || stream.RemoteTrack.StreamStatus == MediaStreamStatusEnum.Inactive) return null;
        try { return stream.GetSendingFormat(); }
        catch { return null; }
    }

    public string Describe() =>
        $"audio {(Audio.HasValue ? string.Join(" ", _audioFormats.Select(f => $"{f.Name()}/{f.ClockRate()}")) : "none")}, " +
        $"video {(Video.HasValue ? Video.Value.Name() : "none")}";

    /// <summary>Creates the peer connection and its offer for the page.</summary>
    public async Task<string> CreateOfferAsync()
    {
        var pc = new RTCPeerConnection(new RTCConfiguration { X_UseRtpFeedbackProfile = true });
        if (Audio.HasValue) pc.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.audio, false, _audioFormats));
        if (Video.HasValue) pc.addTrack(new MediaStreamTrack(SDPMediaTypesEnum.video, false, [Video.Value]));
        _pc = pc;

        pc.onconnectionstatechange += state =>
        {
            _log($"page media {state}");
            if (state == RTCPeerConnectionState.connected)
            {
                _up = true;
                RequestKeyframe(_sip, _sipVideoSsrc);
                _statsTimer ??= new System.Threading.Timer(_ => _log($"relay: audio sip->page {_audioIn} page->sip {_audioOut}, video frames sip->page {_videoIn} page->sip {_videoOut}, keyframe requests from sip {_pliIn} from page {_pliOut}"), null, 5000, 5000);
            }
            else if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
            {
                _up = false;
            }
        };

        // SIP -> page. Kept in fields so Dispose can unhook them: adding video to a call builds a
        // new relay on the same SIP session.
        _sipRtp = (_, kind, pkt) =>
        {
            if (kind == SDPMediaTypesEnum.video) _sipVideoSsrc = pkt.Header.SyncSource;
            if (_up && kind == SDPMediaTypesEnum.audio && ++_audioIn > 0)
                pc.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType, pkt.Header.SequenceNumber);
        };
        _sipFrame = (_, ts, frame, _) =>
        {
            if (_up) { _videoIn++; pc.SendVideoAt(ts, frame); }
        };
        _sipReport = (_, kind, report) =>
        {
            if (kind == SDPMediaTypesEnum.video && WantsKeyframe(report)) { _pliIn++; RequestKeyframe(pc, _pageVideoSsrc); }
            if (kind == SDPMediaTypesEnum.audio) CheckAudioReachingThem(report);
            var ours = kind == SDPMediaTypesEnum.audio ? _sip.AudioLocalTrack?.Ssrc : _sip.VideoLocalTrack?.Ssrc;
            if (ours != null) RaiseQuality("sent", kind, Samples(report).FirstOrDefault(r => r.SSRC == ours));
        };
        _sipSendReport = (kind, report) =>
        {
            if (kind == SDPMediaTypesEnum.video) RaiseQuality("received", kind, Samples(report).FirstOrDefault());
        };
        _sip.OnRtpPacketReceived += _sipRtp;
        _sip.OnVideoFrameReceived += _sipFrame;
        _sip.OnReceiveReport += _sipReport;
        _sip.OnSendReport += _sipSendReport;

        // Page -> SIP.
        pc.OnRtpPacketReceived += (_, kind, pkt) =>
        {
            if (kind == SDPMediaTypesEnum.video) _pageVideoSsrc = pkt.Header.SyncSource;
            if (kind == SDPMediaTypesEnum.audio && Lose(AudioLossPercent)) return;
            if (kind == SDPMediaTypesEnum.audio && !_sip.IsClosed && ++_audioOut > 0)
                _sip.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType, pkt.Header.SequenceNumber);
        };
        pc.OnVideoFrameReceived += (_, ts, frame, _) =>
        {
            if (_sip.IsClosed) return;
            _videoOut++;
            if (VideoLossPercent > 0) SendVideoLossy(ts, frame);
            else _sip.SendVideoAt(ts, frame);
        };
        pc.OnReceiveReport += (_, kind, report) =>
        {
            if (kind == SDPMediaTypesEnum.video && WantsKeyframe(report)) { _pliOut++; RequestKeyframe(_sip, _sipVideoSsrc); }
        };

        var offer = pc.createOffer();
        await pc.setLocalDescription(offer);
        // SIPSorcery's video formats offer "nack pli" and "ccm fir", so the browser asks for
        // keyframes when it needs one.
        return offer.sdp;
    }

    /// <summary>
    /// Each RTCP report from the far end lists, for every source it hears, the highest sequence
    /// number it has received. If we've sent a good number of packets since the last report and
    /// that number hasn't moved, or our source isn't listed at all, our audio isn't arriving.
    /// </summary>
    private void CheckAudioReachingThem(RTCPCompoundPacket report)
    {
        var ourSsrc = _sip.AudioLocalTrack?.Ssrc;
        if (ourSsrc == null || !_up) return;
        var ours = Samples(report).FirstOrDefault(r => r.SSRC == ourSsrc);
        bool lost;
        if (ours == null)
        {
            // Not listed: they've heard nothing from us, once we've had time to send (2 s of audio).
            lost = _audioOut > 100;
        }
        else
        {
            lost = _rrHighestSeq == ours.ExtendedHighestSequenceNumber && _audioOut - _rrAudioOut > 50;
            _rrHighestSeq = ours.ExtendedHighestSequenceNumber;
        }
        _rrAudioOut = _audioOut;
        if (lost == _txLost) return;
        _txLost = lost;
        _log(lost ? "their RTCP says our audio isn't reaching them" : "their RTCP says our audio is reaching them again");
        TxLostChanged?.Invoke(lost);
    }

    private static IEnumerable<ReceptionReportSample> Samples(RTCPCompoundPacket report) =>
        (report.ReceiverReport?.ReceptionReports ?? []).Concat(report.SenderReport?.ReceptionReports ?? []);

    /// <summary>Turns a reception report into a <see cref="QualitySample"/>: percent, milliseconds and a running loss total.</summary>
    private void RaiseQuality(string dir, SDPMediaTypesEnum kind, ReceptionReportSample? rr)
    {
        if (rr == null || !_up || Quality == null) return;
        bool audio = kind == SDPMediaTypesEnum.audio;
        if (!audio && kind != SDPMediaTypesEnum.video) return;

        // Jitter is in RTP timestamp units: the clock of the codec (audio is only ever "sent").
        int clock = 90000;
        if (audio) clock = Audio is { } f && f.RtpClockRate() > 0 ? f.RtpClockRate() : 8000;
        double fraction = rr.FractionLost / 256.0;

        // Running total: the fraction lost times the packets expected since the last report
        // (the change in the highest sequence number).
        string key = dir + kind;
        uint seq = rr.ExtendedHighestSequenceNumber;
        double total = 0;
        lock (_lossTotals)
        {
            if (_lossTotals.TryGetValue(key, out var last))
            {
                uint expected = seq - last.Seq;
                total = last.Lost + (expected < 100_000 ? fraction * expected : 0);
            }
            _lossTotals[key] = (seq, total);
        }

        // Round trip (RFC 3550 6.4.1): now - LSR - DLSR, all in 1/65536 s.
        double? rtt = null;
        if (dir == "sent" && rr.LastSenderReportTimestamp != 0)
        {
            uint now = RTCPSession.DateTimeToNtpTimestamp32(DateTime.Now);
            double ms = (int)(now - rr.LastSenderReportTimestamp - rr.DelaySinceLastSenderReport) * 1000.0 / 65536;
            if (ms >= 0 && ms < 10_000) rtt = Math.Round(ms, 1);
        }

        Quality(new QualitySample(dir, audio ? "audio" : "video", Math.Round(fraction * 100, 1), (long)Math.Round(total),
            Math.Round(rr.Jitter * 1000.0 / clock, 1), rtt));
    }

    /// <summary>SIPSorcery's own packet size for video (its RTP_MAX_PAYLOAD, which is internal).</summary>
    private const int MaxVideoPayload = 1200;

    private static bool Lose(int percent) => percent > 0 && Random.Shared.Next(100) < percent;

    /// <summary>
    /// Sends a video frame as SIPSorcery would (the same packetisation for VP8 and H264), but
    /// drops packets at random. SIPSorcery packetises inside the library with no way to drop a
    /// packet, so with loss on the relay packetises here and sends each packet with the next
    /// sequence number from the track: lost ones leave a gap, and turning loss off goes back to
    /// SIPSorcery's own sending with the numbering carrying on.
    /// </summary>
    private void SendVideoLossy(uint ts, byte[] frame)
    {
        var track = _sip.VideoLocalTrack;
        if (track == null || Video is not { } format) return;
        int pt = format.ID, max = MaxVideoPayload;
        void Send(byte[] payload, int marker)
        {
            ushort seq = track.GetNextSeqNum();
            if (!Lose(VideoLossPercent)) _sip.SendRtpRaw(SDPMediaTypesEnum.video, payload, ts, marker, pt, seq);
        }

        if (format.Name().Equals("VP8", StringComparison.OrdinalIgnoreCase))
        {
            // RFC 7741: a one-byte descriptor, with the start-of-partition bit on the first packet.
            for (int offset = 0; offset < frame.Length; offset += max)
            {
                int len = Math.Min(max, frame.Length - offset);
                var payload = new byte[len + 1];
                payload[0] = offset == 0 ? (byte)0x10 : (byte)0x00;
                Buffer.BlockCopy(frame, offset, payload, 1, len);
                Send(payload, offset + len >= frame.Length ? 1 : 0);
            }
        }
        else
        {
            // H264 (RFC 6184): each NAL whole if it fits, else in FU-A fragments.
            foreach (var nal in H264Packetiser.ParseNals(frame))
            {
                var data = nal.NAL;
                if (data.Length <= max) { Send(data, nal.IsLast ? 1 : 0); continue; }
                for (int offset = 1; offset < data.Length; offset += max)
                {
                    int len = Math.Min(max, data.Length - offset);
                    bool first = offset == 1, last = offset + len >= data.Length;
                    var header = H264Packetiser.GetH264RtpHeader(data[0], first, last);
                    var payload = new byte[header.Length + len];
                    Buffer.BlockCopy(header, 0, payload, 0, header.Length);
                    Buffer.BlockCopy(data, offset, payload, header.Length, len);
                    Send(payload, nal.IsLast && last ? 1 : 0);
                }
            }
        }
    }

    public bool SetAnswer(string sdp)
    {
        var result = _pc!.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
        if (result != SetDescriptionResultEnum.OK) _log($"page answer rejected: {result}");
        return result == SetDescriptionResultEnum.OK;
    }

    private static bool WantsKeyframe(RTCPCompoundPacket report) =>
        report.FeedbackMessages.Any(f => f.Header.PacketType == RTCPReportTypesEnum.PSFB &&
            f.Header.PayloadFeedbackMessageType is PSFBFeedbackTypesEnum.PLI or PSFBFeedbackTypesEnum.FIR);

    /// <summary>
    /// Asks the sender of the video this session receives for a keyframe. The far end routes
    /// feedback by its media SSRC, so it's the SSRC their video actually arrives with, learned
    /// from RTP, falling back to the one in their SDP.
    /// </summary>
    private static void RequestKeyframe(RTPSession session, uint learnedSsrc)
    {
        if (session.IsClosed || !session.HasVideo || session.VideoRemoteTrack == null) return;
        var mediaSsrc = learnedSsrc != 0 ? learnedSsrc : session.VideoRemoteTrack.Ssrc;
        if (mediaSsrc == 0) return;
        var pli = new RTCPFeedback(session.VideoLocalTrack.Ssrc, mediaSsrc, PSFBFeedbackTypesEnum.PLI);
        session.SendRtcpFeedback(SDPMediaTypesEnum.video, pli);
    }

    public void Dispose()
    {
        _up = false;
        if (_sipRtp != null) _sip.OnRtpPacketReceived -= _sipRtp;
        if (_sipFrame != null) _sip.OnVideoFrameReceived -= _sipFrame;
        if (_sipReport != null) _sip.OnReceiveReport -= _sipReport;
        if (_sipSendReport != null) _sip.OnSendReport -= _sipSendReport;
        _statsTimer?.Dispose();
        try { _pc?.close(); } catch { }
    }
}
