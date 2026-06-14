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
        Play
    }

    private const int IVF_HEADER_LENGTH = 32;
    private const uint IVF_TIMEBASE = 90000;          // Matches the RTP video clock.

    private readonly SinkMode _mode;
    private readonly string? _filePath;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    private Stream? _out;
    private FileStream? _file;
    private Process? _ffplay;
    private bool _failed;
    private bool _disposed;
    private bool _isVp8;
    private bool _awaitingVp8KeyFrame;
    private bool _awaitingH264Sps;
    private uint _firstTimestamp;
    private bool _hasFirstTimestamp;
    private long _bytesWritten;
    private int _framesWritten;

    public bool IsActive => _mode != SinkMode.None;
    public bool IsStdout => _mode == SinkMode.Stdout;
    public long BytesWritten { get { lock (_lock) { return _bytesWritten; } } }
    public int FramesWritten { get { lock (_lock) { return _framesWritten; } } }

    private VideoSink(SinkMode mode, string? filePath, ILogger logger)
    {
        _mode = mode;
        _filePath = filePath;
        _logger = logger;
    }

    public static VideoSink Create(string? spec, ILogger logger, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return new VideoSink(SinkMode.None, null, logger);
        }

        if (spec == "-")
        {
            return new VideoSink(SinkMode.Stdout, null, logger);
        }

        if (spec.Equals("play", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoSink(SinkMode.Play, null, logger);
        }

        return new VideoSink(SinkMode.File, spec, logger);
    }

    /// <summary>
    /// Writes one depacketised video frame. The first frame fixes the codec and, for VP8,
    /// writing is deferred until a key frame supplies the dimensions for the IVF header.
    /// </summary>
    public void WriteFrame(byte[] frame, uint rtpTimestamp, VideoFormat format)
    {
        if (_mode == SinkMode.None || _failed || frame.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                // Frames can still arrive between the sink closing and the peer connection
                // closing; drop them silently.
                return;
            }

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
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

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
}
