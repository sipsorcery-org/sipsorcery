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

    /// <summary>
    /// The SIP side can carry media: always for plain RTP, and once ICE and DTLS are done for a
    /// WebRTC leg (a browser phone). Until then what the page sends is dropped.
    /// </summary>
    private bool SipReady => !_sip.IsClosed && (_sip is not RTCPeerConnection p || p.connectionState == RTCPeerConnectionState.connected);
    private Action<RTCPeerConnectionState>? _sipPcState;

    public SDPAudioVideoMediaFormat? Audio { get; }
    public SDPAudioVideoMediaFormat? Video { get; }

    /// <summary>
    /// Every audio codec both SIP ends can do, the one we send first. Each end of a SIP call may
    /// send a different one of the codecs both offered (here Opus one way and G722 the other is
    /// common), so the page must be ready to receive any of them, not just the one we send.
    /// </summary>
    private readonly List<SDPAudioVideoMediaFormat> _audioFormats = [];

    private long _audioIn, _audioOut, _videoIn, _videoOut, _pliIn, _pliOut, _rtcpFromSip, _rtcpFromPage, _pliPageUnderAudio;
    private volatile uint _sipVideoSsrc, _pageVideoSsrc;
    // SIP audio to the page goes out as one stream, whoever sends it. When the far end changes (a
    // transfer, or a call taken over in place) the new one's sequence numbers and timestamps start
    // anywhere, and a page that sees them jump on the same stream drops them as late or duplicate
    // ("no audio arriving") until they happen to catch up. So they're carried on from ours: an
    // offset per source, set when its first packet arrives. The old source's stragglers are dropped.
    private uint _sipAudioSsrc, _oldSipAudioSsrc, _audioTs, _audioTsStep, _audioTsOffset;
    private ushort _audioSeq, _audioSeqOffset;
    private bool _audioSent;
    private Action<System.Net.IPEndPoint, SDPMediaTypesEnum, RTPPacket>? _sipRtp;
    private Action<System.Net.IPEndPoint, uint, byte[], VideoFormat>? _sipFrame;
    private Action<System.Net.IPEndPoint, SDPMediaTypesEnum, RTCPCompoundPacket>? _sipReport;

    // One-way audio out: their RTCP receiver reports say how far into our audio they've got.
    private uint? _rrHighestSeq;
    private long _rrAudioOut;
    private bool _txLost;

    /// <summary>
    /// They've put us on hold the SIP way, so our side of the audio is recvonly (or inactive) and
    /// the RTP session won't send. The page sends silence on a call held like that, and it goes
    /// anyway, straight onto the RTP socket: our NAT binding stays open, and when they resume
    /// there's a stream already flowing to latch onto. Not on an encrypted leg (a browser phone),
    /// which would need SRTP; there it's dropped as before. False if the session sends it itself.
    /// </summary>
    private bool SendPastTheirHold(RTPPacket pkt)
    {
        var stream = _sip.AudioStream;
        var track = stream?.LocalTrack;
        if (stream == null || track == null || track.StreamStatus is not (MediaStreamStatusEnum.RecvOnly or MediaStreamStatusEnum.Inactive)) return false;
        if (_sip is RTCPeerConnection || stream.GetSecurityContext() != null || stream.DestinationEndPoint == null || !stream.HasRtpChannel()) return true;
        var copy = new RTPPacket(pkt.Payload.Length);
        copy.Header.SyncSource = track.Ssrc;
        copy.Header.SequenceNumber = pkt.Header.SequenceNumber;
        copy.Header.Timestamp = pkt.Header.Timestamp;
        copy.Header.MarkerBit = pkt.Header.MarkerBit;
        copy.Header.PayloadType = pkt.Header.PayloadType;
        Buffer.BlockCopy(pkt.Payload, 0, copy.Payload, 0, pkt.Payload.Length);
        stream.GetRTPChannel().Send(RTPChannelSocketsEnum.RTP, stream.DestinationEndPoint, copy.GetBytes());
        return true;
    }

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

    // DTMF (RFC 4733): the last audio packet sent to the SIP side, and how far the page's audio
    // sequence numbers have been moved along to make room for the event packets put in between.
    private readonly object _audioOutLock = new();
    private ushort _seqShift, _lastAudioSeq;
    private uint _lastAudioTs;
    private bool _audioSeen;
    private readonly SemaphoreSlim _dtmfOne = new(1, 1);

    /// <summary>
    /// Sends a DTMF digit (0–15: 0–9, * is 10, # is 11) as an RFC 4733 telephone-event on the
    /// SIP leg, in the page's audio stream: the event's timestamp is the audio's at the moment the
    /// key was pressed, and its packets take the next sequence numbers, the audio after it moving
    /// along to match. (RTPSession.SendDtmf keeps a timestamp and sequence of its own, which here,
    /// with the page's audio relayed as it is, would jump about.) The audio goes on meanwhile.
    /// One digit at a time: presses queue. False if no audio has gone yet to put it in.
    /// </summary>
    public async Task<bool> SendDtmfAsync(byte digit, int payloadType, int clockRate, int toneMs = 160)
    {
        await _dtmfOne.WaitAsync();
        try
        {
            uint ts;
            lock (_audioOutLock)
            {
                if (!_audioSeen || !SipReady) return false;
                ts = _lastAudioTs;
            }
            const int packetMs = 20;
            ushort step = (ushort)(clockRate * packetMs / 1000), total = (ushort)(clockRate * toneMs / 1000);
            void Send(bool end, ushort duration, int marker)
            {
                ushort seq;
                lock (_audioOutLock) { _seqShift++; seq = ++_lastAudioSeq; }
                var payload = new RTPEvent(digit, end, RTPEvent.DEFAULT_VOLUME, duration, payloadType).GetEventPayload();
                _sip.SendRtpRaw(SDPMediaTypesEnum.audio, payload, ts, marker, payloadType, seq);
            }
            // The start (marker bit on the first), an update every 20 ms, then the end, three times
            // over as RFC 4733 suggests for the packets that matter most.
            Send(false, step, 1);
            for (ushort d = step; d < total; )
            {
                await Task.Delay(packetMs);
                d = (ushort)Math.Min(total, d + step);
                if (d < total) Send(false, d, 0);
            }
            for (int i = 0; i < 3; i++) Send(true, total, 0);
            // A short gap before the next digit, so two of the same aren't heard as one.
            await Task.Delay(60);
            return true;
        }
        finally { _dtmfOne.Release(); }
    }


    /// <summary>
    /// The audio and video codecs we offer on the SIP side, in order of preference. All are ones
    /// the browser can do. Video is offered for a video call, and when answering (so a video
    /// offer can be accepted, or video added later), unless the account has video turned off.
    ///
    /// Calls over SIP WebSocket are with browser phones, which only do WebRTC media (ICE,
    /// DTLS-SRTP, rtcp-mux, bundle), so for those the SIP side is a peer connection of its own;
    /// it's an RTPSession too, so the relay works the same either way. Everything else is plain
    /// RTP.
    /// </summary>
    public static RTPSession CreateSipSession(bool offerVideo = true, bool webRtc = false)
    {
        RTPSession rtp = webRtc ? new TextPeerConnection() : new RTPSession(false, false, false);
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
                _statsTimer ??= new System.Threading.Timer(_ => _log($"relay: audio sip->page {_audioIn} page->sip {_audioOut}, video frames sip->page {_videoIn} page->sip {_videoOut}, keyframe requests from sip {_pliIn} from page {_pliOut} (under audio {_pliPageUnderAudio}), rtcp reports from sip {_rtcpFromSip} from page {_rtcpFromPage}, sip video ssrc ours {_sip.VideoLocalTrack?.Ssrc} theirs {_sipVideoSsrc}"), null, 5000, 5000);
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
            if (kind == SDPMediaTypesEnum.video && _sipVideoSsrc != pkt.Header.SyncSource)
            {
                // Video from a new source (the far end changed: a transfer, or a call taken over
                // in place) starts mid-stream, and the page can't decode it until a keyframe: ask
                // for one now rather than wait for their next periodic one.
                bool changed = _sipVideoSsrc != 0;
                _sipVideoSsrc = pkt.Header.SyncSource;
                if (changed && _up)
                {
                    _log($"relay: their video is from a new source ({pkt.Header.SyncSource}): asking it for a keyframe");
                    RequestKeyframe(_sip, _sipVideoSsrc);
                }
            }
            if (_up && kind == SDPMediaTypesEnum.audio) SipAudioToPage(pc, pkt);
        };
        _sipFrame = (_, ts, frame, _) =>
        {
            if (_up) { _videoIn++; pc.SendVideoAt(ts, frame); }
        };
        _sipReport = (_, kind, report) =>
        {
            // A WebRTC leg bundles audio and video on one transport, so a report can arrive under
            // either kind: what it's about is found by our SSRCs, not by the kind it came with.
            bool bundled = _sip is RTCPeerConnection;
            _rtcpFromSip++;
            if ((kind == SDPMediaTypesEnum.video || bundled) && WantsKeyframe(report, _sip.VideoLocalTrack?.Ssrc)) { _pliIn++; RequestKeyframe(pc, _pageVideoSsrc); }
            var samples = Samples(report).ToList();
            var audio = samples.FirstOrDefault(r => r.SSRC == _sip.AudioLocalTrack?.Ssrc);
            var video = samples.FirstOrDefault(r => r.SSRC == _sip.VideoLocalTrack?.Ssrc);
            if (audio != null || (kind == SDPMediaTypesEnum.audio && !bundled)) CheckAudioReachingThem(audio);
            if (audio != null) RaiseQuality("sent", SDPMediaTypesEnum.audio, audio);
            if (video != null) RaiseQuality("sent", SDPMediaTypesEnum.video, video);
        };
        _sipSendReport = (kind, report) =>
        {
            if (kind == SDPMediaTypesEnum.video) RaiseQuality("received", kind, Samples(report).FirstOrDefault());
        };
        _sip.OnRtpPacketReceived += _sipRtp;
        _sip.OnVideoFrameReceived += _sipFrame;
        _sip.OnReceiveReport += _sipReport;
        _sip.OnSendReport += _sipSendReport;

        // A WebRTC SIP leg connects in its own time: once it does, each side is asked for a
        // keyframe, so the video starts without waiting for the next periodic one.
        if (_sip is RTCPeerConnection sipPc)
        {
            _sipPcState = state =>
            {
                if (state != RTCPeerConnectionState.connected || !_up) return;
                RequestKeyframe(_sip, _sipVideoSsrc);
                RequestKeyframe(pc, _pageVideoSsrc);
            };
            sipPc.onconnectionstatechange += _sipPcState;
        }

        // Page -> SIP. Audio goes on with the page's sequence numbers, moved along by one for each
        // DTMF packet put in between (see SendDtmfAsync), so the far end sees no gap or repeat.
        pc.OnRtpPacketReceived += (_, kind, pkt) =>
        {
            if (kind == SDPMediaTypesEnum.video) _pageVideoSsrc = pkt.Header.SyncSource;
            if (kind != SDPMediaTypesEnum.audio) return;
            lock (_audioOutLock)
            {
                pkt.Header.SequenceNumber = (ushort)(pkt.Header.SequenceNumber + _seqShift);
                _lastAudioSeq = pkt.Header.SequenceNumber;
                _lastAudioTs = pkt.Header.Timestamp;
                _audioSeen = true;
            }
            if (Lose(AudioLossPercent)) return;
            if (SipReady && ++_audioOut > 0 && !SendPastTheirHold(pkt))
                _sip.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, pkt.Header.Timestamp, pkt.Header.MarkerBit, pkt.Header.PayloadType, pkt.Header.SequenceNumber);
        };
        pc.OnVideoFrameReceived += (_, ts, frame, _) =>
        {
            if (!SipReady) return;
            _videoOut++;
            if (VideoLossPercent > 0) SendVideoLossy(ts, frame);
            else _sip.SendVideoAt(ts, frame);
        };
        pc.OnReceiveReport += (_, kind, report) =>
        {
            // The page's leg is bundled too: a keyframe request can come under either kind, so
            // it's found by our video SSRC. (Counted apart when it came under audio.)
            _rtcpFromPage++;
            if (WantsKeyframe(report, pc.VideoLocalTrack?.Ssrc))
            {
                _pliOut++;
                if (kind != SDPMediaTypesEnum.video) _pliPageUnderAudio++;
                RequestKeyframe(_sip, _sipVideoSsrc);
            }
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
    private void CheckAudioReachingThem(ReceptionReportSample? ours)
    {
        if (_sip.AudioLocalTrack == null || !_up) return;
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

    /// <summary>A PLI or FIR in the report; with mediaSsrc, only one about that stream (a bundled leg's feedback can arrive under audio).</summary>
    private static bool WantsKeyframe(RTCPCompoundPacket report, uint? mediaSsrc = null) =>
        report.FeedbackMessages.Any(f => f.Header.PacketType == RTCPReportTypesEnum.PSFB &&
            f.Header.PayloadFeedbackMessageType is PSFBFeedbackTypesEnum.PLI or PSFBFeedbackTypesEnum.FIR &&
            (mediaSsrc == null || f.MediaSSRC == mediaSsrc || f.MediaSSRC == 0));

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

    /// <summary>
    /// The call's far end has changed (a transfer, or a call taken over in place): our video
    /// reaches someone new mid-stream, so the page sends a keyframe now, and theirs is asked
    /// for one too (and again when their video's first packet arrives, see the SIP RTP handler).
    /// </summary>
    /// <summary>
    /// SIP audio to the page, its sequence numbers and timestamps carried on across a change of
    /// far end (see _sipAudioSsrc). Called on the SIP receive thread only.
    /// </summary>
    private void SipAudioToPage(RTCPeerConnection pc, RTPPacket pkt)
    {
        var h = pkt.Header;
        bool marker = h.MarkerBit == 1;
        if (h.SyncSource != _sipAudioSsrc)
        {
            if (h.SyncSource == _oldSipAudioSsrc) return;   // the one before, still sending: not the call any more
            if (_audioSent)
            {
                // Pick up one packet on from the last we sent, as the start of a talkspurt.
                _audioSeqOffset = (ushort)(_audioSeq + 1 - h.SequenceNumber);
                _audioTsOffset = _audioTs + (_audioTsStep == 0 ? 960u : _audioTsStep) - h.Timestamp;
                marker = true;
                _log($"relay: their audio is from a new source ({h.SyncSource}): carrying on our sequence and timestamps to the page");
            }
            _oldSipAudioSsrc = _sipAudioSsrc;
            _sipAudioSsrc = h.SyncSource;
        }
        ushort seq = (ushort)(h.SequenceNumber + _audioSeqOffset);
        uint ts = h.Timestamp + _audioTsOffset;
        // The usual step between packets (a frame's worth of samples), for the next change.
        uint step = ts - _audioTs;
        if (_audioSent && (ushort)(seq - _audioSeq) == 1 && step > 0 && step <= 48000) _audioTsStep = step;
        (_audioSeq, _audioTs, _audioSent) = (seq, ts, true);
        _audioIn++;
        pc.SendRtpRaw(SDPMediaTypesEnum.audio, pkt.Payload, ts, marker ? 1 : 0, h.PayloadType, seq);
    }

    public void FarEndChanged()
    {
        if (!_up || _pc is not { } pc) return;
        RequestKeyframe(pc, _pageVideoSsrc);
        RequestKeyframe(_sip, _sipVideoSsrc);
    }

    public void Dispose()
    {
        _up = false;
        if (_sipRtp != null) _sip.OnRtpPacketReceived -= _sipRtp;
        if (_sipFrame != null) _sip.OnVideoFrameReceived -= _sipFrame;
        if (_sipReport != null) _sip.OnReceiveReport -= _sipReport;
        if (_sipSendReport != null) _sip.OnSendReport -= _sipSendReport;
        if (_sipPcState != null && _sip is RTCPeerConnection sipPc) sipPc.onconnectionstatechange -= _sipPcState;
        _statsTimer?.Dispose();
        try { _pc?.close(); } catch { }
    }
}
