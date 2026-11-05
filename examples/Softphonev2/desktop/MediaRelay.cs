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
        };
        _sip.OnRtpPacketReceived += _sipRtp;
        _sip.OnVideoFrameReceived += _sipFrame;
        _sip.OnReceiveReport += _sipReport;

        // Page -> SIP.
        pc.OnRtpPacketReceived += (_, kind, pkt) =>
        {
            if (kind == SDPMediaTypesEnum.video) _pageVideoSsrc = pkt.Header.SyncSource;
            if (kind == SDPMediaTypesEnum.audio && !_sip.IsClosed && ++_audioOut > 0)
                _sip.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType, pkt.Header.SequenceNumber);
        };
        pc.OnVideoFrameReceived += (_, ts, frame, _) =>
        {
            if (!_sip.IsClosed) { _videoOut++; _sip.SendVideoAt(ts, frame); }
        };
        pc.OnReceiveReport += (_, kind, report) =>
        {
            if (kind == SDPMediaTypesEnum.video && WantsKeyframe(report)) { _pliOut++; RequestKeyframe(_sip, _sipVideoSsrc); }
        };

        var offer = pc.createOffer();
        await pc.setLocalDescription(offer);
        return AddKeyframeFeedback(offer.sdp, Video);
    }

    /// <summary>
    /// SIPSorcery only offers transport-cc feedback, so without this the browser may not send
    /// keyframe requests (PLI/FIR) or NACKs at all, and a decoder that joins late waits for the
    /// sender's next keyframe, which a WebRTC encoder only makes when asked.
    /// </summary>
    private static string AddKeyframeFeedback(string sdp, SDPAudioVideoMediaFormat? video)
    {
        if (!video.HasValue) return sdp;
        var id = video.Value.ID;
        var anchor = $"a=rtpmap:{id} ";
        var lines = sdp.Split("\r\n").ToList();
        int at = lines.FindIndex(l => l.StartsWith(anchor));
        if (at < 0) return sdp;
        lines.InsertRange(at + 1, [$"a=rtcp-fb:{id} nack", $"a=rtcp-fb:{id} nack pli", $"a=rtcp-fb:{id} ccm fir"]);
        return string.Join("\r\n", lines);
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
        var samples = (report.ReceiverReport?.ReceptionReports ?? []).Concat(report.SenderReport?.ReceptionReports ?? []);
        var ours = samples.FirstOrDefault(r => r.SSRC == ourSsrc);
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
    /// feedback by its media SSRC, so that has to be the SSRC its video actually arrives with:
    /// the one learned from RTP, since the SDP often doesn't carry one (the track's would be 0
    /// or stale, and the request silently dropped).
    /// </summary>
    private static void RequestKeyframe(RTPSession session, uint learnedSsrc)
    {
        if (session.IsClosed || !session.HasVideo || session.VideoRemoteTrack == null) return;
        var mediaSsrc = learnedSsrc != 0 ? learnedSsrc : session.VideoRemoteTrack.Ssrc;
        if (mediaSsrc == 0) return;
        var ourSsrc = session.VideoLocalTrack.Ssrc;
        var pli = new RTCPFeedback(ourSsrc, mediaSsrc, PSFBFeedbackTypesEnum.PLI);
        // A PLI on its own is 12 bytes, and SIPSorcery's receive path drops any packet that isn't
        // longer than an RTP header (12), so another SIPSorcery end never sees it. An SDES (CNAME)
        // after it gets it through while keeping it routed by the PLI's media SSRC; a receiver
        // report wouldn't, as the far end can't match ours when our camera is off.
        var bytes = pli.GetBytes().Concat(new RTCPSDesReport(ourSsrc, "softphone").GetBytes()).ToArray();
        session.SendRtcpRaw(SDPMediaTypesEnum.video, bytes);
    }

    public void Dispose()
    {
        _up = false;
        if (_sipRtp != null) _sip.OnRtpPacketReceived -= _sipRtp;
        if (_sipFrame != null) _sip.OnVideoFrameReceived -= _sipFrame;
        if (_sipReport != null) _sip.OnReceiveReport -= _sipReport;
        _statsTimer?.Dispose();
        try { _pc?.close(); } catch { }
    }
}
