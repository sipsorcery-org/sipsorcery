//-----------------------------------------------------------------------------
// Filename: AudioScopeTransform.cs
//
// Description: A source decorator that adds a generated "audio scope" video to a
// stream: it passes the wrapped source's audio frames through unchanged AND, in
// parallel, turns that audio into a video visualisation (a waveform or spectrum)
// that it also emits into the graph. So "route --from sip:... --to whip:... --scope"
// forwards the call audio and adds a second, synthesised video track of it.
//
// The visualisation and its VP8 encoding are delegated to an external ffmpeg
// process (the showwaves / showspectrum filters), in keeping with the rule that
// per-sample / DSP work belongs to ffmpeg, never a managed node - the same way
// AudioSink and VideoSink shell out to ffplay. The decoded PCM is piped to
// ffmpeg's stdin; ffmpeg renders, encodes VP8 and writes an IVF stream to stdout,
// which a reader thread parses back into encoded frames for the graph.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class AudioScopeTransform : ISourceNode
{
    private const int VP8_PAYLOAD_ID = 96;
    private const uint VIDEO_CLOCK_RATE = 90000;
    private const int IVF_FILE_HEADER_LENGTH = 32;
    private const int IVF_FRAME_HEADER_LENGTH = 12;

    private readonly ISourceNode _inner;
    private readonly EdgeOptions _options;
    private readonly ILogger _logger;
    private readonly VideoFormat _videoFormat = new(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID);
    private readonly AudioEncoder _audioDecoder = new();
    private readonly object _ffmpegLock = new();

    private Process? _ffmpeg;
    private Thread? _reader;
    private bool _failed;
    private bool _stopping;
    private uint _videoTimestamp;
    private readonly uint _frameDurationRtpUnits;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _inner.Completion;

    public long? ConnectTimeMs => _inner.ConnectTimeMs;

    public AudioScopeTransform(ISourceNode inner, EdgeOptions options, ILogger logger)
    {
        _inner = inner;
        _options = options;
        _logger = logger;
        _frameDurationRtpUnits = VIDEO_CLOCK_RATE / (uint)Math.Max(1, options.Fps);
    }

    public string Describe() => $"{_inner.Describe()} +scope({_options.ScopeMode} {_options.ScopeSize})";

    public async Task StartAsync(CancellationToken ct)
    {
        // Tee the wrapped source: every frame it produces is forwarded, and audio is also fed to the
        // scope. ffmpeg starts lazily on the first audio frame, once the sample rate is known.
        _inner.OnFrame += HandleInnerFrame;
        await _inner.StartAsync(ct).ConfigureAwait(false);
    }

    private void HandleInnerFrame(MediaFrame frame)
    {
        // Pass through whatever the wrapped source produced (the audio being forwarded to the sink).
        OnFrame?.Invoke(frame);

        if (frame.Kind == MediaKind.Audio && !_failed)
        {
            FeedScope(frame);
        }
    }

    private void FeedScope(MediaFrame audioFrame)
    {
        short[] pcm;
        try
        {
            pcm = _audioDecoder.DecodeAudio(audioFrame.Payload, audioFrame.AudioFormat);
        }
        catch (Exception excp)
        {
            _logger.LogWarning("Audio scope decode failed, no further scope video will be produced: {Error}", excp.Message);
            _failed = true;
            return;
        }

        if (pcm.Length == 0)
        {
            return;
        }

        lock (_ffmpegLock)
        {
            if (_stopping || _failed)
            {
                return;
            }

            if (_ffmpeg == null && !StartFfmpeg(audioFrame.AudioFormat.ClockRate))
            {
                return;
            }

            try
            {
                var bytes = new byte[pcm.Length * sizeof(short)];
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                _ffmpeg!.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                _ffmpeg.StandardInput.BaseStream.Flush();
            }
            catch (Exception excp)
            {
                _logger.LogWarning("Audio scope write failed, no further scope video will be produced: {Error}", excp.Message);
                _failed = true;
            }
        }
    }

    /// <summary>
    /// Starts ffmpeg reading s16le mono PCM on stdin, rendering the scope and writing VP8-in-IVF to
    /// stdout. Assumes <see cref="_ffmpegLock"/> is held.
    /// </summary>
    private bool StartFfmpeg(int sampleRate)
    {
        string filter = BuildFilter();
        string fileName = string.IsNullOrWhiteSpace(_options.FfmpegPath)
            ? "ffmpeg"
            : Path.Combine(_options.FfmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

        var startInfo = new ProcessStartInfo(fileName)
        {
            Arguments = $"-hide_banner -loglevel error -f s16le -ar {sampleRate} -ac 1 -i - " +
                        $"-filter_complex \"[0:a]{filter}[v]\" -map \"[v]\" " +
                        "-c:v libvpx -deadline realtime -cpu-used 5 -b:v 1M -f ivf -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            _ffmpeg = Process.Start(startInfo);
            if (_ffmpeg == null)
            {
                throw new ApplicationException("ffmpeg did not start.");
            }
        }
        catch (Exception excp)
        {
            _logger.LogError("Could not start ffmpeg for the audio scope: {Error}. Install ffmpeg and ensure it is on the PATH, or pass --ffmpeg-path.", excp.Message);
            _failed = true;
            return false;
        }

        // Drain stderr so ffmpeg cannot block, surfacing anything it says as debug.
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await _ffmpeg.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                _logger.LogDebug("ffmpeg(scope): {Line}", line);
            }
        });

        _reader = new Thread(ReadIvfLoop) { IsBackground = true, Name = "audio-scope-ivf" };
        _reader.Start();

        _logger.LogDebug("Audio scope ffmpeg started: {Mode} {Size} @ {Fps}fps from {Rate}Hz audio.",
            _options.ScopeMode, _options.ScopeSize, _options.Fps, sampleRate);
        return true;
    }

    private string BuildFilter()
    {
        string size = _options.ScopeSize;
        int fps = _options.Fps;

        // Both filters convert audio to video; normalise to the target fps and a VP8-friendly pixel
        // format. showwaves is a moving waveform; showspectrum is a scrolling frequency spectrum.
        return _options.ScopeMode.Equals("spectrum", StringComparison.OrdinalIgnoreCase)
            ? $"showspectrum=s={size}:slide=scroll:color=intensity,fps={fps},format=yuv420p"
            : $"showwaves=s={size}:mode=cline:colors=0x33CC33,fps={fps},format=yuv420p";
    }

    /// <summary>
    /// Reads the IVF stream from ffmpeg's stdout and emits each VP8 frame into the graph. The IVF
    /// layout is the inverse of VideoSink's writer: a 32-byte file header then, per frame, a 12-byte
    /// header (4-byte LE size + 8-byte LE pts) followed by the frame payload.
    /// </summary>
    private void ReadIvfLoop()
    {
        Stream stdout = _ffmpeg!.StandardOutput.BaseStream;
        try
        {
            var fileHeader = new byte[IVF_FILE_HEADER_LENGTH];
            stdout.ReadExactly(fileHeader, 0, fileHeader.Length);

            var frameHeader = new byte[IVF_FRAME_HEADER_LENGTH];
            while (!_stopping)
            {
                stdout.ReadExactly(frameHeader, 0, frameHeader.Length);
                uint frameLength = BinaryPrimitives.ReadUInt32LittleEndian(frameHeader);
                if (frameLength == 0 || frameLength > 10_000_000)
                {
                    _logger.LogWarning("Audio scope IVF frame length {Length} out of range; stopping the scope reader.", frameLength);
                    return;
                }

                var frame = new byte[frameLength];
                stdout.ReadExactly(frame, 0, frame.Length);

                _videoTimestamp += _frameDurationRtpUnits;
                OnFrame?.Invoke(MediaFrame.ForVideo(frame, _videoTimestamp, _videoFormat, _frameDurationRtpUnits));
            }
        }
        catch (EndOfStreamException)
        {
            // ffmpeg closed its stdout (exited). The scope simply stops; the wrapped audio source
            // keeps running.
            _logger.LogDebug("Audio scope ffmpeg output ended.");
        }
        catch (Exception excp)
        {
            if (!_stopping)
            {
                _logger.LogWarning("Audio scope reader stopped: {Error}", excp.Message);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_ffmpegLock)
        {
            _stopping = true;
        }

        _inner.OnFrame -= HandleInnerFrame;

        Process? ffmpeg;
        lock (_ffmpegLock)
        {
            ffmpeg = _ffmpeg;
        }

        if (ffmpeg != null)
        {
            try
            {
                // Closing stdin lets ffmpeg drain and exit; then the reader thread sees EOF.
                ffmpeg.StandardInput.Close();
                if (!ffmpeg.WaitForExit(2000))
                {
                    ffmpeg.Kill();
                }
                ffmpeg.Dispose();
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Audio scope ffmpeg close error: {Error}", excp.Message);
            }
        }

        _reader?.Join(2000);

        await _inner.DisposeAsync().ConfigureAwait(false);
    }
}
