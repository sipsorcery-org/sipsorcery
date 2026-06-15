//-----------------------------------------------------------------------------
// Filename: VideoSink.cs
//
// Description: Routes received, depacketised (still encoded) video frames to
// one of three sinks, mirroring AudioSink:
//  - "play":  a spawned ffplay window. Decode is delegated to ffplay so no
//             video decoder is needed in-process and both H264 and VP8 work.
//  - <file>:  a raw bitstream file (H264 Annex B, or VP8 wrapped in IVF).
//  - "-":     the bitstream on stdout. The caller is responsible for routing
//             its result object to stderr in this mode. Enables arbitrary
//             downstream renderers, e.g. "| mpv --vo=tct -" for video in the
//             terminal.
//
// Container selection is by codec: H264 frames arrive as Annex B NAL units
// and are written as-is; VP8 frames are wrapped in an IVF container, with
// the dimensions parsed from the first key frame.
//
// Optionally a decoder can be supplied (see Create). In that mode the frames
// are decoded in-process to raw RGB24 pixels and those are sent to the sink
// instead of the encoded bitstream, so ffplay is started with the rawvideo
// demuxer. This exercises the SIPSorcery decode path rather than ffplay's own
// decoder, the difference being where the picture is decoded.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class VideoSink : IDisposable
{
    private enum SinkMode
    {
        None,
        File,
        Stdout,
        Play,
        Null
    }

    private const int IVF_HEADER_LENGTH = 32;
    private const uint IVF_TIMEBASE = 90000;          // Matches the RTP video clock.

    // The decode/IO work runs on a dedicated worker thread, NOT the caller's. The WebRTC receive
    // thread that delivers frames also services ICE/STUN on the same socket, so it must never block
    // on decode or a stalled pipe. Frames are handed over a bounded queue; when the worker falls
    // behind the oldest queued frame is dropped rather than stalling the receive thread. The depth is
    // enough to absorb the consumer's start-up burst (e.g. the ffplay window opening) while keeping
    // worst-case latency small; a worker that genuinely cannot keep up still drops.
    private const int MAX_QUEUED_FRAMES = 16;

    // ffplay's rawvideo demuxer defaults to 25 fps; without an explicit rate it paces (and so reads
    // from the pipe) at 25 fps, back-pressuring the worker and dropping faster sources. Used only by
    // the decoded ("--decode") play path. Video-only playback disables ffplay's frame dropping (the
    // master clock is the video), so feeding a rate at or above the real one just means "display on
    // arrival" -- it never fast-forwards -- which keeps ffplay from being the bottleneck.
    private const int DEFAULT_RAW_FRAME_RATE = 60;

    private readonly record struct QueuedFrame(byte[] Frame, uint RtpTimestamp, VideoFormat Format);

    private readonly SinkMode _mode;
    private readonly string? _filePath;
    private readonly ILogger _logger;
    private readonly IVideoEncoder? _decoder;
    private readonly int _frameRate;
    private readonly object _queueLock = new();
    private readonly Queue<QueuedFrame> _queue = new();

    private Thread? _worker;
    private bool _stopping;
    private int _droppedFrames;

    private Stream? _out;
    private FileStream? _file;
    private Process? _ffplay;
    private volatile bool _failed;
    private bool _disposed;
    private bool _isVp8;
    private bool _awaitingVp8KeyFrame;
    private bool _awaitingH264Sps;
    private uint _firstTimestamp;
    private bool _hasFirstTimestamp;
    private long _bytesWritten;
    private int _framesWritten;
    private int _rawWidth;
    private int _rawHeight;

    public bool IsActive => _mode != SinkMode.None;
    public bool IsStdout => _mode == SinkMode.Stdout;

    // These counters are written only by the worker thread; read them after Dispose, which joins it.
    public long BytesWritten => _bytesWritten;
    public int FramesWritten => _framesWritten;
    public int DroppedFrames { get { lock (_queueLock) { return _droppedFrames; } } }

    private VideoSink(SinkMode mode, string? filePath, ILogger logger, IVideoEncoder? decoder, int frameRate)
    {
        _mode = mode;
        _filePath = filePath;
        _logger = logger;
        _decoder = decoder;
        _frameRate = frameRate;
    }

    /// <summary>
    /// Creates a sink for the given spec ("play", a file path, "-" for stdout, or null/empty for no
    /// sink). When <paramref name="decoder"/> is supplied the frames are decoded in-process to raw
    /// RGB24 and that is sent to the sink, rather than the encoded bitstream. <paramref name="frameRate"/>
    /// (0 = unknown) sets the rawvideo playback rate for the decoded "play" path so ffplay does not
    /// throttle to its 25 fps default.
    /// </summary>
    public static VideoSink Create(string? spec, ILogger logger, out string? error, IVideoEncoder? decoder = null, int frameRate = 0)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return new VideoSink(SinkMode.None, null, logger, decoder, frameRate);
        }

        if (spec == "-")
        {
            return new VideoSink(SinkMode.Stdout, null, logger, decoder, frameRate);
        }

        if (spec.Equals("play", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoSink(SinkMode.Play, null, logger, decoder, frameRate);
        }

        if (spec.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            // Decode/depacketise but discard the output. Lets the pipeline be exercised headlessly
            // (no ffplay pacing, no disk) to measure throughput / frame drops.
            return new VideoSink(SinkMode.Null, null, logger, decoder, frameRate);
        }

        return new VideoSink(SinkMode.File, spec, logger, decoder, frameRate);
    }

    /// <summary>
    /// Queues one depacketised video frame for the worker thread and returns immediately, so the
    /// caller's thread (the WebRTC receive/ICE thread) is never blocked by decode or IO. If the worker
    /// cannot keep up the oldest queued frame is dropped (see <see cref="DroppedFrames"/>).
    /// </summary>
    public void WriteFrame(byte[] frame, uint rtpTimestamp, VideoFormat format)
    {
        if (_mode == SinkMode.None || _failed || frame.Length == 0)
        {
            return;
        }

        // Copy the frame: the caller may reuse its buffer as soon as this returns.
        byte[] copy = new byte[frame.Length];
        Buffer.BlockCopy(frame, 0, copy, 0, frame.Length);

        lock (_queueLock)
        {
            if (_disposed || _stopping)
            {
                return;
            }

            _worker ??= StartWorker();

            if (_queue.Count >= MAX_QUEUED_FRAMES)
            {
                // Drop the oldest to keep the freshest frame and never block the receive thread.
                _queue.Dequeue();
                _droppedFrames++;
            }

            _queue.Enqueue(new QueuedFrame(copy, rtpTimestamp, format));
            Monitor.Pulse(_queueLock);
        }
    }

    private Thread StartWorker()
    {
        var worker = new Thread(WorkerLoop) { IsBackground = true, Name = "video-sink" };
        worker.Start();
        return worker;
    }

    /// <summary>
    /// The single consumer thread: dequeues frames and does the decode/IO off the receive thread. The
    /// first frame fixes the codec and, for VP8, writing is deferred until a key frame supplies the
    /// IVF dimensions. Exits promptly when stopping, discarding any remaining backlog.
    /// </summary>
    private void WorkerLoop()
    {
        while (true)
        {
            QueuedFrame item;
            lock (_queueLock)
            {
                while (_queue.Count == 0 && !_stopping)
                {
                    Monitor.Wait(_queueLock);
                }

                if (_stopping)
                {
                    return;
                }

                item = _queue.Dequeue();
            }

            if (_failed)
            {
                continue;
            }

            if (_decoder != null)
            {
                WriteDecodedFrame(item.Frame, item.Format);
            }
            else
            {
                WriteEncodedFrame(item.Frame, item.RtpTimestamp, item.Format);
            }
        }
    }

    /// <summary>
    /// Pass-through path: writes the still-encoded frame, wrapping VP8 in IVF and leaving H264 as
    /// Annex B. Assumes the caller holds the lock. Decode is delegated to the sink's consumer.
    /// </summary>
    private void WriteEncodedFrame(byte[] frame, uint rtpTimestamp, VideoFormat format)
    {
        if (_out == null)
        {
            _isVp8 = format.Codec == VideoCodecsEnum.VP8;
            _awaitingVp8KeyFrame = _isVp8;
            _awaitingH264Sps = format.Codec == VideoCodecsEnum.H264;

            if (!Init(format))
            {
                return;
            }
        }

        try
        {
            if (_isVp8)
            {
                if (_awaitingVp8KeyFrame)
                {
                    if (!TryParseVp8KeyFrameDimensions(frame, out ushort width, out ushort height))
                    {
                        return; // Inter frame before the first key frame; not decodable yet.
                    }

                    WriteIvfFileHeader(width, height);
                    _awaitingVp8KeyFrame = false;
                }

                if (!_hasFirstTimestamp)
                {
                    _firstTimestamp = rtpTimestamp;
                    _hasFirstTimestamp = true;
                }

                WriteIvfFrameHeader(frame.Length, unchecked(rtpTimestamp - _firstTimestamp));
            }
            else if (_awaitingH264Sps)
            {
                if (!ContainsH264Sps(frame))
                {
                    return; // Frames before the first SPS/PPS reference parameter sets the decoder hasn't seen.
                }

                _awaitingH264Sps = false;
            }

            _out!.Write(frame, 0, frame.Length);
            _out.Flush();
            _bytesWritten += frame.Length;
            _framesWritten++;
        }
        catch (Exception excp)
        {
            _logger.LogWarning("Video sink write failed, no further video will be written: {Error}", excp.Message);
            _failed = true;
        }
    }

    /// <summary>
    /// Decode path: decodes the frame in-process to raw RGB24 and writes that to the sink. ffplay is
    /// started with the rawvideo demuxer so the picture has been through the SIPSorcery decoder
    /// rather than ffplay's. Assumes the caller holds the lock.
    /// </summary>
    private void WriteDecodedFrame(byte[] frame, VideoFormat format)
    {
        IEnumerable<VideoSample> samples;
        try
        {
            // The decoders (FFmpeg/VP8) always convert to packed 24-bit RGB regardless of the
            // requested pixel format, so the sink is fixed at rgb24.
            samples = _decoder!.DecodeVideo(frame, VideoPixelFormatsEnum.Rgb, format.Codec);
        }
        catch (Exception excp)
        {
            _logger.LogWarning("In-process video decode failed, no further video will be written: {Error}", excp.Message);
            _failed = true;
            return;
        }

        foreach (var sample in samples)
        {
            if (sample.Sample == null || sample.Sample.Length == 0 || sample.Width == 0 || sample.Height == 0)
            {
                continue;
            }

            int width = (int)sample.Width;
            int height = (int)sample.Height;

            if (_out == null)
            {
                _rawWidth = width;
                _rawHeight = height;

                if (!InitRaw())
                {
                    return;
                }
            }
            else if (width != _rawWidth || height != _rawHeight)
            {
                // ffplay's rawvideo demuxer is fixed to the first frame's size and cannot change mid
                // stream, so skip a resized frame rather than corrupt the display.
                _logger.LogWarning("Skipping decoded frame whose {Width}x{Height} differs from the initial {InitW}x{InitH}.",
                    width, height, _rawWidth, _rawHeight);
                continue;
            }

            try
            {
                WriteRawRgb(sample.Sample, width, height);
            }
            catch (Exception excp)
            {
                _logger.LogWarning("Video sink write failed, no further video will be written: {Error}", excp.Message);
                _failed = true;
                return;
            }
        }
    }

    /// <summary>
    /// Writes one decoded RGB24 frame, stripping any row padding the decoder's stride introduced so
    /// the rawvideo stream is tightly packed at width*3 bytes per row.
    /// </summary>
    private void WriteRawRgb(byte[] buffer, int width, int height)
    {
        int rowBytes = width * 3;                 // RGB24, 3 bytes per pixel.
        int stride = buffer.Length / height;

        if (stride == rowBytes)
        {
            _out!.Write(buffer, 0, rowBytes * height);
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                _out!.Write(buffer, row * stride, rowBytes);
            }
        }

        _out!.Flush();
        _bytesWritten += (long)rowBytes * height;
        _framesWritten++;
    }

    /// <summary>
    /// Lazily starts the sink for decoded raw RGB24 output once the first frame's dimensions are
    /// known. Play mode runs ffplay with the rawvideo demuxer; file/stdout get the raw pixels.
    /// </summary>
    private bool InitRaw()
    {
        try
        {
            switch (_mode)
            {
                case SinkMode.File:
                    _file = new FileStream(_filePath!, FileMode.Create, FileAccess.ReadWrite);
                    _out = _file;
                    _logger.LogDebug("Writing decoded raw rgb24 {Width}x{Height} video to {FilePath}.", _rawWidth, _rawHeight, _filePath);
                    return true;

                case SinkMode.Stdout:
                    _out = Console.OpenStandardOutput();
                    Console.Error.WriteLine($"Writing decoded raw rgb24 {_rawWidth}x{_rawHeight} video to stdout.");
                    return true;

                case SinkMode.Play:
                    // -framerate stops ffplay's rawvideo demuxer pacing at its 25 fps default, which
                    // would back-pressure the pipe and drop a faster source. Give headroom over the
                    // nominal rate so the publisher's slightly-fast "-re" pacing does not cause ffplay
                    // to throttle; with frame dropping off (video-only) the extra headroom is harmless.
                    int rawFrameRate = Math.Max(_frameRate * 2, DEFAULT_RAW_FRAME_RATE);
                    var startInfo = new ProcessStartInfo("ffplay")
                    {
                        Arguments = $"-hide_banner -loglevel error -fflags nobuffer -f rawvideo -pixel_format rgb24 -framerate {rawFrameRate} -video_size {_rawWidth}x{_rawHeight} -i -",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true
                    };

                    _ffplay = Process.Start(startInfo);
                    if (_ffplay == null)
                    {
                        throw new ApplicationException("ffplay did not start.");
                    }

                    _ = Task.Run(async () =>
                    {
                        string? line;
                        while ((line = await _ffplay.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            _logger.LogDebug("ffplay: {Line}", line);
                        }
                    });

                    _out = _ffplay.StandardInput.BaseStream;
                    Console.Error.WriteLine($"Rendering in-process decoded rgb24 {_rawWidth}x{_rawHeight} video with ffplay.");
                    return true;

                case SinkMode.Null:
                    _out = Stream.Null;
                    _logger.LogDebug("Discarding decoded raw rgb24 {Width}x{Height} video (null sink).", _rawWidth, _rawHeight);
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception excp) when (_mode == SinkMode.Play)
        {
            _logger.LogError("Could not start ffplay: {Error}. Install ffmpeg (which includes ffplay) and ensure it is on the PATH.", excp.Message);
            _failed = true;
            return false;
        }
        catch (Exception excp)
        {
            _logger.LogError("Could not initialise the video sink: {Error}", excp.Message);
            _failed = true;
            return false;
        }
    }

    private bool Init(VideoFormat format)
    {
        try
        {
            switch (_mode)
            {
                case SinkMode.File:
                    _file = new FileStream(_filePath!, FileMode.Create, FileAccess.ReadWrite);
                    _out = _file;
                    _logger.LogDebug("Writing received {Codec} video to {FilePath}.", format.Codec, _filePath);
                    return true;

                case SinkMode.Stdout:
                    _out = Console.OpenStandardOutput();
                    Console.Error.WriteLine(_isVp8
                        ? "Writing VP8 in an IVF container to stdout."
                        : $"Writing raw {format.Codec} Annex B bitstream to stdout.");
                    return true;

                case SinkMode.Play:
                    string demuxer = _isVp8 ? "ivf" : format.Codec.ToString().ToLowerInvariant();
                    var startInfo = new ProcessStartInfo("ffplay")
                    {
                        Arguments = $"-hide_banner -loglevel error -fflags nobuffer -f {demuxer} -i -",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardError = true
                    };

                    _ffplay = Process.Start(startInfo);
                    if (_ffplay == null)
                    {
                        throw new ApplicationException("ffplay did not start.");
                    }

                    _ = Task.Run(async () =>
                    {
                        string? line;
                        while ((line = await _ffplay.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            _logger.LogDebug("ffplay: {Line}", line);
                        }
                    });

                    _out = _ffplay.StandardInput.BaseStream;
                    Console.Error.WriteLine($"Rendering received {format.Codec} video with ffplay.");
                    return true;

                case SinkMode.Null:
                    _out = Stream.Null;
                    _logger.LogDebug("Discarding received {Codec} video (null sink).", format.Codec);
                    return true;

                default:
                    return false;
            }
        }
        catch (Exception excp) when (_mode == SinkMode.Play)
        {
            _logger.LogError("Could not start ffplay: {Error}. Install ffmpeg (which includes ffplay) and ensure it is on the PATH.", excp.Message);
            _failed = true;
            return false;
        }
        catch (Exception excp)
        {
            _logger.LogError("Could not initialise the video sink: {Error}", excp.Message);
            _failed = true;
            return false;
        }
    }

    /// <summary>
    /// A VP8 key frame starts with a frame tag whose low bit is 0, followed by the
    /// 9D 01 2A start code and 14 bit width and height fields (RFC 6386 section 9.1).
    /// </summary>
    private static bool TryParseVp8KeyFrameDimensions(byte[] frame, out ushort width, out ushort height)
    {
        width = 0;
        height = 0;

        if (frame.Length < 10 || (frame[0] & 0x01) != 0 || frame[3] != 0x9D || frame[4] != 0x01 || frame[5] != 0x2A)
        {
            return false;
        }

        width = (ushort)((frame[6] | (frame[7] << 8)) & 0x3FFF);
        height = (ushort)((frame[8] | (frame[9] << 8)) & 0x3FFF);
        return width > 0 && height > 0;
    }

    /// <summary>
    /// Scans an Annex B frame for an SPS NAL unit (type 7). Encoders send SPS/PPS in-band
    /// with each key frame, so the first SPS marks the first decodable point in the stream.
    /// </summary>
    private static bool ContainsH264Sps(byte[] frame)
    {
        for (int i = 0; i + 3 < frame.Length; i++)
        {
            if (frame[i] == 0x00 && frame[i + 1] == 0x00 &&
                (frame[i + 2] == 0x01 || (frame[i + 2] == 0x00 && i + 4 < frame.Length && frame[i + 3] == 0x01)))
            {
                int nalStart = frame[i + 2] == 0x01 ? i + 3 : i + 4;
                if ((frame[nalStart] & 0x1F) == 7)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void WriteIvfFileHeader(ushort width, ushort height)
    {
        using var writer = new BinaryWriter(_out!, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("DKIF"u8);
        writer.Write((ushort)0);              // Version.
        writer.Write((ushort)IVF_HEADER_LENGTH);
        writer.Write("VP80"u8);
        writer.Write(width);
        writer.Write(height);
        writer.Write(IVF_TIMEBASE);           // Timebase denominator.
        writer.Write(1u);                     // Timebase numerator.
        writer.Write(0u);                     // Frame count, patched on close for files.
        writer.Write(0u);                     // Unused.
    }

    private void WriteIvfFrameHeader(int frameLength, uint pts)
    {
        using var writer = new BinaryWriter(_out!, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write((uint)frameLength);
        writer.Write((ulong)pts);
    }

    public void Dispose()
    {
        Thread? worker;
        lock (_queueLock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _stopping = true;
            Monitor.PulseAll(_queueLock);
            worker = _worker;
        }

        // Stop the worker before finalising the streams it writes to. It exits promptly (discarding
        // any backlog); the timeout guards against it being blocked on a stalled ffplay pipe.
        worker?.Join(2000);

        try
        {
            if (_file != null)
            {
                if (_isVp8 && _framesWritten > 0)
                {
                    // Patch the IVF frame count.
                    _file.Seek(24, SeekOrigin.Begin);
                    using var writer = new BinaryWriter(_file, System.Text.Encoding.ASCII, leaveOpen: true);
                    writer.Write((uint)_framesWritten);
                }
                _file.Dispose();
            }
            else if (_ffplay != null)
            {
                _ffplay.StandardInput.Close();
                if (!_ffplay.WaitForExit(2000))
                {
                    _ffplay.Kill();
                }
                _ffplay.Dispose();
            }
            else
            {
                _out?.Flush();
            }
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Video sink close error: {Error}", excp.Message);
        }
    }
}
