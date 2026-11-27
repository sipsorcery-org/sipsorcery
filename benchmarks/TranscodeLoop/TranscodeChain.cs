// One stream's trip through SIPSorcery:
//
//   page A (browser encodes) ══ leg A ══► RTCPeerConnection A (recvonly)
//        → depacketise → decode (vp8.net | h264.net | ffmpeg) → BGR frame
//        → encode (vp8.net | av1.net | h264.net | ffmpeg)
//   RTCPeerConnection B (sendonly) ══ leg B ══► page B (browser decodes and shows)
//
// Keyframe requests are part of what's tested: page B's PLI/FIR forces a keyframe from our
// encoder, and we ask page A for one when the stream starts, when our decoder fails, when frames
// are dropped (simulated, or because the decoder fell behind) and when asked to by hand.
//
// Threads: frames arrive on leg A's receive thread and queue for the decode thread, which must
// decode every frame (a skipped VP8 or H264 frame breaks the ones after it). Decoded frames queue
// for the encode thread, up to 3; beyond that the oldest is skipped (fewer frames out, but none
// broken). So a codec that can't keep up shows as a growing queue or skipped
// frames, not a stalled receive thread. If the queue reaches half a second the decoder drops it
// all and waits for a keyframe, as a real receiver would.
//
// Relay mode skips both codecs: frames go from leg A to leg B as they came, and keyframe
// requests from page B go on to page A. It's the baseline: the plumbing without the codecs.

using System.Collections.Concurrent;
using SIPSorcery.H264;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.TranscodeLoop;

internal sealed record ChainConfig(string Source, int Width, int Height, string Mode, string CodecIn, string Decoder, string CodecOut, string Encoder, int EncoderKbps)
{
    public bool Relay => Mode == "relay";
    public bool TestCard => Source == "card";
    public string Describe() => $"{Width}x{Height} " + (Relay
        ? $"relay {CodecIn} (no decode or encode)"
        : $"transcode {CodecIn} ({Codecs.Describe(Decoder, CodecIn, encode: false)}) → " +
          $"{CodecOut} ({Codecs.Describe(Encoder, CodecOut, encode: true)}{(EncoderKbps > 0 ? $", {EncoderKbps} kbit/s" : "")})");
}

internal sealed class TranscodeChain : IDisposable
{
    /// <summary>Frames waiting for the decoder at which it gives up on them (half a second at 30 fps).</summary>
    private const int QueueOverrun = 15;
    /// <summary>Automatic keyframe requests to page A no closer together than this.</summary>
    private const double PliMinGapMs = 250;

    private readonly ChainConfig _cfg;
    private readonly Action<string> _log;
    private readonly DecodedView? _view;
    private readonly RTCPeerConnection _a, _b;
    private readonly VideoFormat _formatIn, _formatOut;
    private readonly IVideoEncoder? _decoder, _encoder;
    private readonly AudioEncoder _audioDecoder = new(includeOpus: true), _audioEncoder = new(includeOpus: true);
    private readonly BlockingCollection<InFrame> _queue = new();
    private readonly AutoResetEvent _encodeSignal = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread? _decodeThread, _encodeThread;
    private readonly ConcurrentQueue<object> _events = new();
    /// <summary>Decoded frames waiting for the encoder: a few, so two arriving together aren't a skip.</summary>
    private readonly ConcurrentQueue<DecodedFrame> _toEncode = new();
    private const int EncodeQueueMax = 3;
    private bool _waitForKey;
    private double _lastPliA = double.MinValue;
    private uint _aVideoSsrc;
    private ushort _lastSeqA;
    private bool _haveSeqA;
    private readonly HashSet<ushort> _missingA = [];
    private long _reorderedA;
    private string? _encodeErrorLogged, _cropped;
    private bool _disposed;

    /// <summary>Leg B's stand-in network, which drops packets for the loss slider. Null if it couldn't be put in.</summary>
    private LossyLink? _linkB;

    /// <summary>Set from the page while the stream runs.</summary>
    public volatile int DropPercentA;
    public volatile bool PliOnDrop = true, ShowDecoded = true;

    /// <summary>Percent of leg B's video packets lost on the way to page B, after SIPSorcery sent them.</summary>
    public int LossPercentB
    {
        get => _lossPercentB;
        set
        {
            _lossPercentB = value;
            if (_linkB != null) _linkB.LossToBrowser = value;
        }
    }
    private volatile int _lossPercentB;

    // Leg A.
    private long _framesIn, _bytesIn, _keyIn, _pliToA, _droppedA, _rtpLostA, _audioIn;
    // Decode.
    private long _decoded, _decodeErrors, _overruns, _queueDropped, _skippedForKey, _gaps, _unreadable, _repeats;
    private int _decWidth, _decHeight, _lastNumber = -1, _unreadSince;
    // Ranges of frame numbers that didn't come out of the decoder readable, for the page, which
    // tells damage done before SIPSorcery decoded a frame from damage done after it.
    private readonly ConcurrentQueue<int[]> _lostAtDecode = new();
    private double _lastNumberAt;
    // Encode.
    private long _encoded, _encodeSkipped, _encodeErrors, _encodeNoOutput, _keyOut, _bytesOut, _forced;
    // Leg B.
    private long _framesOutB, _pliFromB, _firFromB, _nackFromB, _audioOut, _nackReported, _lossReported;
    private double? _rrLossB, _rrJitterB;

    private readonly Timings _decodeMs = new(), _encodeMs = new(), _queueWaitMs = new(), _inChainMs = new(), _audioMs = new();

    private readonly record struct InFrame(uint Ts, byte[] Data, bool Key, double ArrivedMs);
    private sealed record DecodedFrame(uint Ts, byte[] Bgr, int Width, int Height, int Number, double ArrivedMs);

    public RTCPeerConnectionState StateA => _a.connectionState;
    public RTCPeerConnectionState StateB => _b.connectionState;
    public event Action<string, RTCPeerConnectionState>? StateChanged;

    public TranscodeChain(ChainConfig cfg, DecodedView? view, Action<string> log)
    {
        _cfg = cfg;
        _view = view;
        _log = log;
        _formatIn = Codecs.Format(cfg.CodecIn, cfg.Width, cfg.Height);
        _formatOut = Codecs.Format(cfg.Relay ? cfg.CodecIn : cfg.CodecOut, cfg.Width, cfg.Height);
        if (!cfg.Relay)
        {
            _decoder = Codecs.Create(cfg.Decoder, 0);
            _encoder = Codecs.Create(cfg.Encoder, cfg.EncoderKbps);
        }

        _a = new RTCPeerConnection(new RTCConfiguration { X_UseRtpFeedbackProfile = true });
        _a.addTrack(new MediaStreamTrack(Codecs.Opus, MediaStreamStatusEnum.RecvOnly));
        _a.addTrack(new MediaStreamTrack(_formatIn, MediaStreamStatusEnum.RecvOnly));
        // SIPSorcery reassembles each frame from its packets in a buffer of this size (1 MB unless
        // told otherwise) and drops a frame that doesn't fit: too small for a 4K keyframe.
        _a.MaxReconstructedVideoFrameSize = Math.Max(1 << 20, cfg.Width * cfg.Height / 2);
        _b = new RTCPeerConnection(new RTCConfiguration { X_UseRtpFeedbackProfile = true });
        _b.addTrack(new MediaStreamTrack(Codecs.Opus, MediaStreamStatusEnum.SendOnly));
        _b.addTrack(new MediaStreamTrack(_formatOut, MediaStreamStatusEnum.SendOnly));

        _a.onconnectionstatechange += state =>
        {
            Event("a-state", note: state.ToString());
            StateChanged?.Invoke("a", state);
            if (state == RTCPeerConnectionState.connected) RequestKeyframeA("start", manual: true);
        };
        _b.onconnectionstatechange += state =>
        {
            Event("b-state", note: state.ToString());
            StateChanged?.Invoke("b", state);
            if (state == RTCPeerConnectionState.connected)
            {
                if (_cfg.Relay) RequestKeyframeA("leg B up", manual: true);
                else _encoder?.ForceKeyFrame();
            }
        };
        _a.OnVideoFrameReceived += (_, ts, frame, _) => OnFrameIn(ts, frame);
        _a.OnRtpPacketReceived += (_, kind, pkt) =>
        {
            if (kind == SDPMediaTypesEnum.audio) OnAudioIn(pkt);
            else if (kind == SDPMediaTypesEnum.video) OnVideoPacketIn(pkt);
        };
        _b.OnReceiveReport += (_, _, report) => OnReportFromB(report);

        if (!cfg.Relay)
        {
            _decodeThread = new Thread(DecodeLoop) { IsBackground = true, Name = "decode" };
            _encodeThread = new Thread(EncodeLoop) { IsBackground = true, Name = "encode" };
            _decodeThread.Start();
            _encodeThread.Start();
        }
        _log($"chain: {cfg.Describe()}");
    }

    /// <summary>
    /// SIPSorcery's offer for each leg. Each offers one video codec, so the browser has to use it.
    /// Leg B's goes through the loss relay: page B is told the relay is SIPSorcery.
    /// </summary>
    public async Task<(string A, string B)> CreateOffersAsync()
    {
        var a = _a.createOffer();
        await _a.setLocalDescription(a);
        var b = _b.createOffer();
        await _b.setLocalDescription(b);
        (_linkB, var sdpB) = LossyLink.InsertInOffer(b.sdp, _formatOut.FormatID, _log);
        if (_linkB != null) _linkB.LossToBrowser = _lossPercentB;
        return (a.sdp, sdpB);
    }

    public bool SetAnswer(string leg, string sdp)
    {
        if (leg == "b" && _linkB != null) sdp = _linkB.InsertInAnswer(sdp);
        var pc = leg == "a" ? _a : _b;
        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
        if (result != SetDescriptionResultEnum.OK) _log($"leg {leg}: the page's answer was rejected: {result}");
        return result == SetDescriptionResultEnum.OK;
    }

    // ---- Leg A in ----

    /// <summary>
    /// Sequence gaps on leg A's video. A packet that turns up late takes its gap back and counts
    /// as reordered instead, so what's left is what never arrived.
    /// </summary>
    private void OnVideoPacketIn(RTPPacket pkt)
    {
        _aVideoSsrc = pkt.Header.SyncSource;
        ushort seq = pkt.Header.SequenceNumber;
        if (_missingA.Remove(seq))
        {
            _rtpLostA--;
            _reorderedA++;
            return;
        }
        if (_haveSeqA)
        {
            ushort step = (ushort)(seq - _lastSeqA);
            if (step > 32768) return;   // older than the newest: a duplicate, or late from before we were counting
            if (step > 1 && step < 3000)
            {
                _rtpLostA += step - 1;
                for (int i = 1; i < step && _missingA.Count < 1000; i++) _missingA.Add((ushort)(_lastSeqA + i));
            }
        }
        (_lastSeqA, _haveSeqA) = (seq, true);
    }

    private void OnFrameIn(uint ts, byte[] frame)
    {
        if (_disposed) return;
        _framesIn++;
        _bytesIn += frame.Length;
        bool key = Codecs.IsKeyframe(_cfg.CodecIn, frame);
        if (key)
        {
            _keyIn++;
            Event("a-key");
        }

        if (DropPercentA > 0 && Random.Shared.Next(100) < DropPercentA)
        {
            // Lost on the way in, as far as the decoder knows. It goes on decoding what comes next,
            // so the picture breaks until a keyframe repairs it: ask for one, unless told not to.
            _droppedA++;
            Event("a-drop");
            if (PliOnDrop) RequestKeyframeA("frame dropped");
            return;
        }

        if (_cfg.Relay)
        {
            SendToB(ts, frame);
            return;
        }
        _queue.Add(new InFrame(ts, frame, key, Clock.NowMs));
    }

    private void OnAudioIn(RTPPacket pkt)
    {
        _audioIn++;
        if (_disposed || _b.connectionState != RTCPeerConnectionState.connected) return;
        if (_cfg.Relay)
        {
            _b.SendAudioAt(pkt.Header.Timestamp, 960, pkt.Payload);
            _audioOut++;
            return;
        }
        double t0 = Clock.NowMs;
        var pcm = _audioDecoder.DecodeAudio(pkt.Payload, Codecs.Opus);
        var opus = _audioEncoder.EncodeAudio(pcm, Codecs.Opus);
        _audioMs.Add(Clock.NowMs - t0);
        if (opus.Length == 0) return;
        _b.SendAudioAt(pkt.Header.Timestamp, (uint)pcm.Length, opus);
        _audioOut++;
    }

    /// <summary>Asks page A's encoder for a keyframe (a PLI on leg A).</summary>
    public void RequestKeyframeA(string reason, bool manual = false)
    {
        double now = Clock.NowMs;
        if (!manual && now - _lastPliA < PliMinGapMs) return;
        var local = _a.VideoLocalTrack;
        uint media = _aVideoSsrc != 0 ? _aVideoSsrc : _a.VideoRemoteTrack?.Ssrc ?? 0;
        if (_a.connectionState != RTCPeerConnectionState.connected || local == null || media == 0) return;
        _lastPliA = now;
        _a.SendRtcpFeedback(SDPMediaTypesEnum.video, new RTCPFeedback(local.Ssrc, media, PSFBFeedbackTypesEnum.PLI));
        Interlocked.Increment(ref _pliToA);
        Event("a-pli", note: reason);
    }

    // ---- Decode ----

    private void DecodeLoop()
    {
        try
        {
            foreach (var f in _queue.GetConsumingEnumerable(_cts.Token))
            {
                if (_queue.Count >= QueueOverrun)
                {
                    int dropped = 1;
                    while (_queue.TryTake(out _)) dropped++;
                    _overruns++;
                    _queueDropped += dropped;
                    _waitForKey = true;
                    Event("overrun", dropped);
                    RequestKeyframeA("decoder fell behind", manual: true);
                    continue;
                }
                if (_waitForKey)
                {
                    if (!f.Key) { _skippedForKey++; continue; }
                    _waitForKey = false;
                }

                _queueWaitMs.Add(Clock.NowMs - f.ArrivedMs);
                double t0 = Clock.NowMs;
                List<(byte[] Bgr, int W, int H)> pictures;
                try
                {
                    pictures = Decode(f.Data);
                }
                catch (Exception ex)
                {
                    _log($"decoder threw {ex.GetType().Name}: {ex.Message}");
                    pictures = [];
                }
                _decodeMs.Add(Clock.NowMs - t0);

                if (pictures.Count == 0)
                {
                    _decodeErrors++;
                    Event("dec-err");
                    RequestKeyframeA("decode failed");
                    continue;
                }
                foreach (var p in pictures) OnDecoded(f, p.Bgr, p.W, p.H);
                // h264.net outputs a concealed picture for damaged or missing data rather than
                // none, and says the stream needs a keyframe to recover.
                if (_decoder is H264Codec { KeyFrameRequired: true }) RequestKeyframeA("decoder concealed damage");
            }
        }
        catch (OperationCanceledException) { /* the chain stopped */ }
        catch (InvalidOperationException) { /* the queue was completed as the chain closed */ }
    }

    /// <summary>The decoder's pictures as tightly packed BGR (every implementation gives BGR).</summary>
    private unsafe List<(byte[] Bgr, int W, int H)> Decode(byte[] frame)
    {
        var codec = Codecs.Codec(_cfg.CodecIn);
        var list = new List<(byte[], int, int)>();
        if (_decoder is FFmpegVideoEncoder or H264Codec)
        {
            foreach (var raw in _decoder.DecodeVideoFaster(frame, VideoPixelFormatsEnum.Bgr, codec))
            {
                // FFmpeg's and h264.net's picture is their own, reused on the next decode, so it's
                // copied out, into a frame buffer kept for reuse: a fresh one per frame would be a
                // large-object allocation 30 times a second, and the GC pauses it caused would be
                // this app's, not the pipeline's. (vp8.net's decoder allocates its own frames.)
                int row = raw.Width * 3;
                var bgr = RentFrame(row * raw.Height);
                fixed (byte* dst = bgr)
                {
                    byte* src = (byte*)raw.Sample;
                    for (int y = 0; y < raw.Height; y++) Buffer.MemoryCopy(src + (long)y * raw.Stride, dst + (long)y * row, row, row);
                }
                list.Add((bgr, raw.Width, raw.Height));
            }
        }
        else
        {
            foreach (var s in _decoder!.DecodeVideo(frame, VideoPixelFormatsEnum.Bgr, codec))
                list.Add((s.Sample, (int)s.Width, (int)s.Height));
        }
        return list;
    }

    private void OnDecoded(InFrame f, byte[] bgr, int w, int h)
    {
        _decoded++;
        (_decWidth, _decHeight) = (w, h);

        int number = -1;
        if (_cfg.TestCard)
        {
            number = Barcode.Read(bgr, w, h);
            if (number < 0)
            {
                _unreadable++;
                _unreadSince++;
                Event("unread");
            }
            else
            {
                if (_lastNumber >= 0 && number > _lastNumber + 1)
                {
                    // Unreadable frames in between were counted as such, not as missing too.
                    long missing = number - _lastNumber - 1 - _unreadSince;
                    if (missing > 0)
                    {
                        _gaps += missing;
                        Event("gap", missing);
                    }
                    // Whatever the cause, these numbers never came out of the decoder readable: the
                    // page holds them against page A, or the decoder, not what follows it.
                    _lostAtDecode.Enqueue([_lastNumber + 1, number - 1]);
                    while (_lostAtDecode.Count > 500) _lostAtDecode.TryDequeue(out _);
                }
                else if (_lastNumber >= 0 && number <= _lastNumber)
                {
                    _repeats++;
                    Event("repeat");
                }
                _lastNumber = number;
                _lastNumberAt = Clock.NowMs;
                _unreadSince = 0;
            }
        }

        if (ShowDecoded) _view?.Write(bgr, w, h, number);

        var frame = new DecodedFrame(f.Ts, bgr, w, h, number, f.ArrivedMs);
        _toEncode.Enqueue(frame);
        while (_toEncode.Count > EncodeQueueMax && _toEncode.TryDequeue(out var skipped))
        {
            _encodeSkipped++;
            ReturnFrame(skipped.Bgr);
        }
        _encodeSignal.Set();
    }

    private readonly ConcurrentQueue<byte[]> _framePool = new();

    private byte[] RentFrame(int size)
    {
        while (_framePool.TryDequeue(out var b))
        {
            if (b.Length == size) return b;
        }
        return new byte[size];
    }

    /// <summary>Back to the pool, if it came from it (only the FFmpeg and h264.net decoders' frames do); others are left to the GC.</summary>
    private void ReturnFrame(byte[] bgr)
    {
        if (_decoder is FFmpegVideoEncoder or H264Codec && _framePool.Count < EncodeQueueMax + 4) _framePool.Enqueue(bgr);
    }

    // ---- Encode ----

    private void EncodeLoop()
    {
        var codec = Codecs.Codec(_cfg.CodecOut);
        while (!_cts.IsCancellationRequested)
        {
            if (!_toEncode.TryDequeue(out var f))
            {
                _encodeSignal.WaitOne(200);
                continue;
            }
            if (_disposed) continue;

            var (bgr, w, h) = FitForEncoder(f);
            double t0 = Clock.NowMs;
            byte[]? encoded;
            try
            {
                encoded = _encoder!.EncodeVideo(w, h, bgr, VideoPixelFormatsEnum.Bgr, codec);
            }
            // Everything: vp8.net, av1.net and FFmpeg each throw their own types on a bad frame, and a
            // run has to count the failure and carry on, not lose its encode thread.
            catch (Exception ex)
            {
                _encodeErrors++;
                Event("enc-err");
                var message = $"{ex.GetType().Name}: {ex.Message}";
                if (message != _encodeErrorLogged) _log($"encoder threw {message}");
                _encodeErrorLogged = message;
                continue;
            }
            finally
            {
                ReturnFrame(f.Bgr);
            }
            _encodeMs.Add(Clock.NowMs - t0);
            if (encoded == null || encoded.Length == 0)
            {
                // The encoder took the frame and gave nothing back: its rate control dropped it
                // (av1.net and h264.net do when over their bitrate). Page B counts it as missing too.
                _encodeNoOutput++;
                continue;
            }

            _encoded++;
            _bytesOut += encoded.Length;
            if (Codecs.IsKeyframe(_cfg.CodecOut, encoded))
            {
                _keyOut++;
                Event("b-key");
            }
            _inChainMs.Add(Clock.NowMs - f.ArrivedMs);
            SendToB(f.Ts, encoded);
        }
    }

    /// <summary>
    /// vp8.net only encodes sizes that are multiples of 16, so other sizes are cropped to fit
    /// (the overlay says so). FFmpeg takes any even size.
    /// </summary>
    private (byte[] Bgr, int W, int H) FitForEncoder(DecodedFrame f)
    {
        if (_cfg.Encoder != Codecs.Vp8Net || (f.Width % 16 == 0 && f.Height % 16 == 0))
        {
            _cropped = null;
            return (f.Bgr, f.Width, f.Height);
        }
        int w = f.Width & ~15, h = f.Height & ~15;
        var crop = new byte[w * h * 3];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(f.Bgr, y * f.Width * 3, crop, y * w * 3, w * 3);
        if (_cropped == null) _log($"vp8.net encodes multiples of 16 only: {f.Width}x{f.Height} is cropped to {w}x{h}");
        _cropped = $"{w}x{h}";
        return (crop, w, h);
    }

    /// <summary>Forces a keyframe from our encoder (as a PLI from page B would). In relay mode, asks page A instead.</summary>
    public void ForceKeyframeB(string reason)
    {
        Interlocked.Increment(ref _forced);
        Event("b-force", note: reason);
        if (_cfg.Relay) RequestKeyframeA(reason, manual: true);
        else _encoder?.ForceKeyFrame();
    }

    // ---- Leg B out ----

    /// <summary>
    /// SIPSorcery packetises and sends, keeping each packet for resending; the loss slider's drops
    /// happen after that, in the relay, so page B's NACKs can be answered as they would be on a
    /// real network.
    /// </summary>
    private void SendToB(uint ts, byte[] frame)
    {
        if (_disposed || _b.connectionState != RTCPeerConnectionState.connected) return;
        _framesOutB++;
        _b.SendVideoAt(ts, frame);
    }

    private void OnReportFromB(RTCPCompoundPacket report)
    {
        uint? ours = _b.VideoLocalTrack?.Ssrc;
        foreach (var fb in report.FeedbackMessages)
        {
            if (fb.Header.PacketType == RTCPReportTypesEnum.PSFB &&
                fb.Header.PayloadFeedbackMessageType is PSFBFeedbackTypesEnum.PLI or PSFBFeedbackTypesEnum.FIR &&
                (fb.MediaSSRC == ours || fb.MediaSSRC == 0))
            {
                bool fir = fb.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.FIR;
                if (fir) Interlocked.Increment(ref _firFromB);
                else Interlocked.Increment(ref _pliFromB);
                Event("b-pli", note: fir ? "FIR" : "PLI");
                if (_cfg.Relay) RequestKeyframeA("keyframe request from page B", manual: true);
                else _encoder?.ForceKeyFrame();
            }
            else if (fb.Header.PacketType == RTCPReportTypesEnum.RTPFB && fb.Header.FeedbackMessageType == RTCPFeedbackTypesEnum.NACK)
            {
                Interlocked.Increment(ref _nackFromB);
            }
        }

        var rr = (report.ReceiverReport?.ReceptionReports ?? []).Concat(report.SenderReport?.ReceptionReports ?? [])
            .FirstOrDefault(r => r.SSRC == ours);
        if (rr != null)
        {
            _rrLossB = Math.Round(rr.FractionLost / 256.0 * 100, 1);
            _rrJitterB = Math.Round(rr.Jitter / 90.0, 1);
        }
    }

    // ---- Stats ----

    private void Event(string kind, long n = 0, string? note = null)
    {
        _events.Enqueue(new { t = Math.Round(Clock.NowMs, 1), k = kind, n, note });
        while (_events.Count > 2000) _events.TryDequeue(out _);
    }

    /// <summary>The counters so far and the events since the last call, for the page (several times a second).</summary>
    public object Snapshot()
    {
        var events = new List<object>();
        while (_events.TryDequeue(out var e)) events.Add(e);
        var lostAtDecode = new List<int[]>();
        while (_lostAtDecode.TryDequeue(out var r)) lostAtDecode.Add(r);
        double now = Math.Round(Clock.NowMs, 1);
        long nacks = Interlocked.Read(ref _nackFromB), lost = _linkB?.DroppedToBrowser ?? 0;
        var resend = _b.VideoStream?.RetransmissionMetrics;
        if (nacks > _nackReported) events.Add(new { t = now, k = "b-nack", n = nacks - _nackReported, note = (string?)null });
        if (lost > _lossReported) events.Add(new { t = now, k = "b-loss", n = lost - _lossReported, note = (string?)null });
        (_nackReported, _lossReported) = (nacks, lost);

        return new
        {
            now,
            config = _cfg,
            a = new
            {
                state = _a.connectionState.ToString(),
                framesIn = _framesIn, bytesIn = _bytesIn, keyIn = _keyIn, pli = Interlocked.Read(ref _pliToA),
                dropped = _droppedA, rtpLost = _rtpLostA, reordered = _reorderedA, audioIn = _audioIn,
            },
            dec = _cfg.Relay ? null : new
            {
                decoded = _decoded, errors = _decodeErrors, ms = _decodeMs.Summary(), wait = _queueWaitMs.Summary(),
                queue = _queue.Count, overruns = _overruns, queueDropped = _queueDropped, skippedForKey = _skippedForKey,
                width = _decWidth, height = _decHeight, number = _lastNumber, numberAt = Math.Round(_lastNumberAt, 1),
                gaps = _gaps, unreadable = _unreadable, repeats = _repeats,
            },
            enc = _cfg.Relay ? null : new
            {
                encoded = _encoded, skipped = _encodeSkipped, noOutput = _encodeNoOutput, queue = _toEncode.Count, ms = _encodeMs.Summary(), errors = _encodeErrors,
                keyOut = _keyOut, bytesOut = _bytesOut, cropped = _cropped, forced = Interlocked.Read(ref _forced),
            },
            inChain = _inChainMs.Summary(),
            b = new
            {
                state = _b.connectionState.ToString(),
                framesOut = _framesOutB, pli = Interlocked.Read(ref _pliFromB), fir = Interlocked.Read(ref _firFromB),
                nack = nacks, lossDropped = lost, rrLoss = _rrLossB, rrJitter = _rrJitterB, audioOut = _audioOut,
                lossSimulated = _linkB != null,
                // SIPSorcery's answers to those NACKs.
                resent = resend?.PacketsRetransmitted ?? 0, notKept = resend?.PacketsNotKept ?? 0,
                suppressed = resend?.RetransmissionsSuppressed ?? 0, overBudget = resend?.RetransmissionsOverBudget ?? 0,
            },
            audio = new { ms = _audioMs.Summary() },
            // The whole process's garbage collections: a pause stops every thread, the pipeline's included.
            gc = new { gen2 = GC.CollectionCount(2), pauseMs = Math.Round(GC.GetTotalPauseDuration().TotalMilliseconds, 1) },
            events,
            lostAtDecode,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _queue.CompleteAdding();
        _encodeSignal.Set();
        _decodeThread?.Join(1000);
        _encodeThread?.Join(1000);
        _a.close();
        _b.close();
        _linkB?.Dispose();
        _decoder?.Dispose();
        _encoder?.Dispose();
        _audioDecoder.Dispose();
        _audioEncoder.Dispose();
        _log("chain closed");
    }
}
