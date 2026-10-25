//-----------------------------------------------------------------------------
// Filename: MatroskaSinkNode.cs
//
// Description: A route sink that muxes the graph's video AND audio into a live
// Matroska stream, to stdout ("--to -:mkv") or a file ("--to out.mkv"). Unlike the
// raw "-" / file sinks, which carry the video bitstream only, a player such as mpv
// gets both tracks with real timestamps:
//
//   route --from testpattern --to -:mkv | mpv -
//
// Still repacketise, not transcode: frames are only rewrapped. H264 Annex B is
// converted to Matroska's length-prefixed form (with the SPS/PPS from the first key
// frame as the avcC codec private data); VP8 passes as-is; Opus gets an OpusHead
// and G.711 a WAVEFORMATEX (A_MS/ACM). Other codecs are left out with a warning.
//
// The Matroska header has to declare every track before the first frame, and the
// H264 avcC needs the SPS/PPS, so frames are held until a video key frame and an
// audio frame have both arrived (or HEADER_WAIT_MS passes with only one of them),
// then the header is written, the held frames flushed and the rest streamed.
//
// Each stream's RTP clock is mapped to milliseconds from when its first frame
// arrived, which lines the tracks up without any RTCP sender reports.
//
// Also used by the "term" bridge endpoint, which tells it which tracks to expect
// (audio only without an avatar) and watches Closed to end when mpv quits.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class MatroskaSinkNode : ISinkNode
{
    private const int VIDEO_TRACK = 1;
    private const int AUDIO_TRACK = 2;
    private const int QUEUE_CAPACITY = 512;
    private const int HEADER_WAIT_MS = 3000;   // How long to hold frames waiting for the other track.
    private const int MAX_HELD_FRAMES = 1024;

    private readonly string? _filePath;   // null for stdout
    private readonly string _spec;
    private readonly ILogger _logger;
    private readonly Channel<(MediaFrame Frame, long ArrivalMs)> _queue;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Task _worker;
    private readonly bool _expectVideo;
    private readonly bool _expectAudio;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Worker thread state.
    private readonly List<(MediaFrame Frame, long ArrivalMs)> _held = new();
    private Stream? _output;
    private MatroskaWriter? _writer;
    private bool _failed;
    private long _firstArrivalMs = -1;
    private byte[]? _avcC;
    private MatroskaTrack? _videoTrack;
    private MatroskaTrack? _audioTrack;
    private bool _warnedVideoCodec;
    private bool _warnedAudioCodec;
    private readonly StreamClock _videoClock = new();
    private readonly StreamClock _audioClock = new();

    private int _frames;
    private int _dropped;

    /// <param name="filePath">The file to write, or null for stdout.</param>
    /// <param name="expectVideo">Hold the header for a video track. When the caller knows there is none
    /// (an audio only stream) set false so the header is written on the first audio frame rather than
    /// after HEADER_WAIT_MS.</param>
    /// <param name="expectAudio">Likewise for an audio track.</param>
    public MatroskaSinkNode(string? filePath, string spec, ILogger logger, bool expectVideo = true, bool expectAudio = true)
    {
        _filePath = filePath;
        _spec = spec;
        _logger = logger;
        _expectVideo = expectVideo;
        _expectAudio = expectAudio;
        _queue = Channel.CreateBounded<(MediaFrame, long)>(
            new BoundedChannelOptions(QUEUE_CAPACITY) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));
        _worker = Task.Run(WorkerLoopAsync);
    }

    public bool IsStdout => _filePath == null;

    /// <summary>Completes if the output stops accepting data, typically the reader (mpv) closing the pipe.</summary>
    public Task Closed => _closed.Task;

    // Nothing to bring up: the output is opened when the header is written.
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public void Write(MediaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        // Copy the payload: the caller may reuse its buffer once this returns.
        var copy = frame with { Payload = (byte[])frame.Payload.Clone() };
        _queue.Writer.TryWrite((copy, _clock.ElapsedMilliseconds));
    }

    public SinkStats GetStats() => new(_frames, _writer?.BytesWritten ?? 0, _dropped);

    public string Describe() => _spec;

    private async Task WorkerLoopAsync()
    {
        await foreach (var (frame, arrivalMs) in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_failed)
            {
                continue;
            }

            try
            {
                Handle(frame, arrivalMs);
            }
            catch (IOException excp)
            {
                // Typically the reader (mpv) closed the pipe.
                _logger.LogWarning("Matroska output closed, no further frames will be written: {Error}", excp.Message);
                _failed = true;
                _closed.TrySetResult();
            }
            catch (Exception excp)
            {
                _logger.LogWarning("Matroska sink failed, no further frames will be written: {Error}", excp.Message);
                _failed = true;
                _closed.TrySetResult();
            }
        }
    }

    private void Handle(MediaFrame frame, long arrivalMs)
    {
        if (_firstArrivalMs < 0)
        {
            _firstArrivalMs = arrivalMs;
        }

        if (_writer != null)
        {
            WriteBlock(frame, arrivalMs);
            return;
        }

        // Still waiting to write the header: learn the tracks from the frames as they arrive.
        if (frame.Kind == MediaKind.Video && _videoTrack == null)
        {
            _videoTrack = TryCreateVideoTrack(frame);
        }
        else if (frame.Kind == MediaKind.Audio && _audioTrack == null)
        {
            _audioTrack = TryCreateAudioTrack(frame.AudioFormat);
        }

        _held.Add((frame, arrivalMs));

        bool bothReady = (_videoTrack != null || !_expectVideo) && (_audioTrack != null || !_expectAudio);
        bool waitedLongEnough = arrivalMs - _firstArrivalMs >= HEADER_WAIT_MS && (_videoTrack != null || _audioTrack != null);
        if (bothReady || waitedLongEnough || _held.Count >= MAX_HELD_FRAMES)
        {
            if (_videoTrack == null && _audioTrack == null)
            {
                // Nothing usable arrived; keep waiting but cap what is held.
                _held.RemoveAt(0);
                return;
            }

            WriteHeaderAndHeldFrames();
        }
    }

    private void WriteHeaderAndHeldFrames()
    {
        var tracks = new List<MatroskaTrack>();
        if (_videoTrack != null) { tracks.Add(_videoTrack); }
        if (_audioTrack != null) { tracks.Add(_audioTrack); }

        _output = _filePath == null ? Console.OpenStandardOutput() : File.Create(_filePath);
        _writer = new MatroskaWriter(_output);
        _writer.WriteHeader(tracks);

        _logger.LogDebug("Matroska header written: {Tracks}.", string.Join(", ", tracks.Select(t => t.CodecId)));
        if (_filePath == null)
        {
            Console.Error.WriteLine($"Writing Matroska ({string.Join(" + ", tracks.Select(t => t.CodecId))}) to stdout.");
        }

        foreach (var (frame, arrivalMs) in _held)
        {
            WriteBlock(frame, arrivalMs);
        }
        _held.Clear();
    }

    private void WriteBlock(MediaFrame frame, long arrivalMs)
    {
        byte[]? data;
        bool keyFrame;
        int track;
        StreamClock clock;
        int clockRate;

        if (frame.Kind == MediaKind.Video)
        {
            if (_videoTrack == null)
            {
                return;
            }

            track = VIDEO_TRACK;
            clock = _videoClock;
            clockRate = frame.VideoFormat.ClockRate > 0 ? frame.VideoFormat.ClockRate : 90000;

            if (frame.VideoFormat.Codec == VideoCodecsEnum.H264)
            {
                data = H264.ToLengthPrefixed(frame.Payload, out keyFrame);
            }
            else
            {
                data = frame.Payload;
                keyFrame = (frame.Payload[0] & 0x01) == 0;   // VP8: the frame tag's P bit is 0 for a key frame.
            }

            // A decoder can only start on a key frame, so drop video until the first one.
            if (!clock.Started && !keyFrame)
            {
                return;
            }
        }
        else
        {
            if (_audioTrack == null)
            {
                return;
            }

            track = AUDIO_TRACK;
            clock = _audioClock;
            clockRate = frame.AudioFormat.RtpClockRate > 0 ? frame.AudioFormat.RtpClockRate : frame.AudioFormat.ClockRate;
            data = frame.Payload;
            keyFrame = true;
        }

        if (data == null || data.Length == 0)
        {
            return;
        }

        long timestampMs = clock.ToMilliseconds(frame.RtpTimestamp, clockRate, arrivalMs - _firstArrivalMs);
        _writer!.WriteFrame(track, timestampMs, keyFrame, data);
        _frames++;
    }

    private MatroskaTrack? TryCreateVideoTrack(MediaFrame frame)
    {
        switch (frame.VideoFormat.Codec)
        {
            case VideoCodecsEnum.H264:
                // The avcC needs the SPS and PPS, which travel in-band with the key frames.
                _avcC ??= H264.TryBuildAvcC(frame.Payload);
                return _avcC == null ? null : new MatroskaTrack(VIDEO_TRACK, true, "V_MPEG4/ISO/AVC", _avcC);

            case VideoCodecsEnum.VP8:
                if ((frame.Payload[0] & 0x01) != 0 || frame.Payload.Length < 10)
                {
                    return null;   // Wait for a key frame, which carries the dimensions.
                }
                int width = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(6)) & 0x3FFF;
                int height = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(8)) & 0x3FFF;
                return new MatroskaTrack(VIDEO_TRACK, true, "V_VP8", Width: width, Height: height);

            default:
                if (!_warnedVideoCodec)
                {
                    _logger.LogWarning("The Matroska sink does not support {Codec} video yet (H264 and VP8 only); the video is left out.", frame.VideoFormat.Codec);
                    _warnedVideoCodec = true;
                }
                return null;
        }
    }

    private MatroskaTrack? TryCreateAudioTrack(AudioFormat format)
    {
        switch (format.Codec)
        {
            case AudioCodecsEnum.OPUS:
                // WebRTC Opus is always 48kHz on the wire; declare 2 channels (the SDP convention) so a
                // stereo packet is not folded down, while mono packets still decode fine.
                return new MatroskaTrack(AUDIO_TRACK, false, "A_OPUS", OpusHead(2), SamplingFrequency: 48000, Channels: 2);

            case AudioCodecsEnum.PCMU:
            case AudioCodecsEnum.PCMA:
                return new MatroskaTrack(AUDIO_TRACK, false, "A_MS/ACM",
                    WaveFormatEx(format.Codec == AudioCodecsEnum.PCMU ? (ushort)7 : (ushort)6), SamplingFrequency: 8000, Channels: 1);

            default:
                if (!_warnedAudioCodec)
                {
                    _logger.LogWarning("The Matroska sink does not support {Codec} audio yet (Opus, PCMU and PCMA only); the audio is left out.", format.Codec);
                    _warnedAudioCodec = true;
                }
                return null;
        }
    }

    /// <summary>The Opus identification header (RFC 7845 section 5.1), Matroska's A_OPUS codec private data.</summary>
    public static byte[] OpusHead(int channels)
    {
        var head = new byte[19];
        Encoding.ASCII.GetBytes("OpusHead").CopyTo(head, 0);
        head[8] = 1;                                                  // version
        head[9] = (byte)channels;
        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(10), 0);      // pre-skip: none for a live stream
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(12), 48000);  // input sample rate
        BinaryPrimitives.WriteInt16LittleEndian(head.AsSpan(16), 0);       // output gain
        head[18] = 0;                                                 // channel mapping family 0
        return head;
    }

    /// <summary>A WAVEFORMATEX for 8kHz mono G.711 (format tag 7 = mu-law, 6 = A-law).</summary>
    public static byte[] WaveFormatEx(ushort formatTag)
    {
        var wfx = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(wfx.AsSpan(0), formatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(wfx.AsSpan(2), 1);       // channels
        BinaryPrimitives.WriteUInt32LittleEndian(wfx.AsSpan(4), 8000);    // samples per second
        BinaryPrimitives.WriteUInt32LittleEndian(wfx.AsSpan(8), 8000);    // average bytes per second
        BinaryPrimitives.WriteUInt16LittleEndian(wfx.AsSpan(12), 1);      // block align
        BinaryPrimitives.WriteUInt16LittleEndian(wfx.AsSpan(14), 8);      // bits per sample
        BinaryPrimitives.WriteUInt16LittleEndian(wfx.AsSpan(16), 0);      // no extra data
        return wfx;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("Matroska sink worker did not drain within 2s.");
        }

        // Flush whatever is still held if the run ended before the header was due.
        if (_writer == null && !_failed && _held.Count > 0 && (_videoTrack != null || _audioTrack != null))
        {
            try { WriteHeaderAndHeldFrames(); }
            catch (Exception excp) { _logger.LogDebug("Matroska final flush failed: {Error}", excp.Message); }
        }

        if (_output != null)
        {
            try { await _output.DisposeAsync().ConfigureAwait(false); }
            catch (Exception excp) { _logger.LogDebug("Matroska output close failed: {Error}", excp.Message); }
        }
    }

    /// <summary>
    /// Maps one stream's RTP timestamps to milliseconds: the first frame lands at the time it arrived
    /// (relative to the sink's first frame of any stream) and later ones follow its RTP clock. Never
    /// goes backwards.
    /// </summary>
    private sealed class StreamClock
    {
        private uint _baseRtp;
        private long _baseMs;
        private long _lastMs = -1;
        private long _unwrapped;
        private uint _lastRtp;

        public bool Started { get; private set; }

        public long ToMilliseconds(uint rtpTimestamp, int clockRate, long arrivalMs)
        {
            if (!Started)
            {
                Started = true;
                _baseRtp = _lastRtp = rtpTimestamp;
                _baseMs = Math.Max(0, arrivalMs);
            }

            // Unwrap the 32 bit RTP timestamp so a long run does not jump back at the wrap.
            _unwrapped += unchecked((int)(rtpTimestamp - _lastRtp));
            _lastRtp = rtpTimestamp;

            long ms = _baseMs + _unwrapped * 1000 / Math.Max(1, clockRate);
            if (ms < _lastMs)
            {
                ms = _lastMs;
            }
            _lastMs = ms;
            return ms;
        }
    }

    /// <summary>The H264 Annex B to Matroska (ISO/AVC) conversions.</summary>
    public static class H264
    {
        private const int NAL_IDR = 5;
        private const int NAL_SPS = 7;
        private const int NAL_PPS = 8;
        private const int NAL_AUD = 9;

        /// <summary>Returns the (offset, length) of each NAL unit in an Annex B access unit, start codes removed.</summary>
        public static List<(int Offset, int Length)> SplitAnnexB(ReadOnlySpan<byte> au)
        {
            var nals = new List<(int, int)>();
            int start = -1;
            int i = 0;
            while (i + 2 < au.Length)
            {
                if (au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1)
                {
                    if (start >= 0)
                    {
                        // A 4 byte start code leaves a trailing zero on the previous NAL; trim it.
                        int end = i;
                        while (end > start && au[end - 1] == 0) { end--; }
                        nals.Add((start, end - start));
                    }
                    i += 3;
                    start = i;
                }
                else
                {
                    i++;
                }
            }

            if (start >= 0 && start < au.Length)
            {
                nals.Add((start, au.Length - start));
            }
            return nals;
        }

        /// <summary>
        /// Converts an Annex B access unit to 4 byte length-prefixed NAL units, dropping access unit
        /// delimiters. <paramref name="keyFrame"/> is true if it contains an IDR slice.
        /// </summary>
        public static byte[] ToLengthPrefixed(byte[] au, out bool keyFrame)
        {
            keyFrame = false;
            var nals = SplitAnnexB(au);
            var output = new MemoryStream(au.Length + 16);
            Span<byte> length = stackalloc byte[4];

            foreach (var (offset, len) in nals)
            {
                if (len == 0)
                {
                    continue;
                }

                int type = au[offset] & 0x1F;
                if (type == NAL_AUD)
                {
                    continue;
                }
                if (type == NAL_IDR)
                {
                    keyFrame = true;
                }

                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)len);
                output.Write(length);
                output.Write(au, offset, len);
            }

            return output.ToArray();
        }

        /// <summary>Builds an AVCDecoderConfigurationRecord (avcC) from the first SPS and PPS in an access unit, or null.</summary>
        public static byte[]? TryBuildAvcC(byte[] au)
        {
            byte[]? sps = null, pps = null;
            foreach (var (offset, len) in SplitAnnexB(au))
            {
                if (len == 0) { continue; }
                int type = au[offset] & 0x1F;
                if (type == NAL_SPS && sps == null && len >= 4) { sps = au.AsSpan(offset, len).ToArray(); }
                else if (type == NAL_PPS && pps == null) { pps = au.AsSpan(offset, len).ToArray(); }
            }

            if (sps == null || pps == null)
            {
                return null;
            }

            var avcC = new MemoryStream();
            avcC.WriteByte(1);                     // configurationVersion
            avcC.WriteByte(sps[1]);                // AVCProfileIndication
            avcC.WriteByte(sps[2]);                // profile_compatibility
            avcC.WriteByte(sps[3]);                // AVCLevelIndication
            avcC.WriteByte(0xFF);                  // 6 reserved bits + lengthSizeMinusOne = 3 (4 byte lengths)
            avcC.WriteByte(0xE1);                  // 3 reserved bits + one SPS
            avcC.WriteByte((byte)(sps.Length >> 8));
            avcC.WriteByte((byte)sps.Length);
            avcC.Write(sps);
            avcC.WriteByte(1);                     // one PPS
            avcC.WriteByte((byte)(pps.Length >> 8));
            avcC.WriteByte((byte)pps.Length);
            avcC.Write(pps);
            return avcC.ToArray();
        }
    }
}
