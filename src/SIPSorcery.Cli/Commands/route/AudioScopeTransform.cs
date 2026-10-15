//-----------------------------------------------------------------------------
// Filename: AudioScopeTransform.cs
//
// Description: A source decorator that adds a generated "audio scope" video to a
// stream: it passes the wrapped source's audio frames through unchanged AND, in
// parallel, turns that audio into a video visualisation (a waveform or spectrum)
// that it also emits into the graph. So "route --from sip:... --to whip:... --scope"
// forwards the call audio and adds a second, synthesised video track of it.
//
// The visualisation and its H264 encoding are delegated to an external ffmpeg
// process (the showwaves / showspectrum filters + libx264), in keeping with the
// rule that per-sample / DSP work belongs to ffmpeg, never a managed node - the
// same way AudioSink and VideoSink shell out to ffplay. The decoded PCM is piped
// to ffmpeg's stdin; ffmpeg renders, encodes H264 and writes an Annex B stream to
// stdout. x264 is told to emit Access Unit Delimiters (aud=1), so a reader thread
// can split the stream back into one encoded frame (access unit) per AUD for the
// graph. H264 (not VP8) because the public Broadcast Box test endpoint rejects VP8.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 23 Jun 2026	Aaron Clauson	Switched the scope video from VP8/IVF to H264/Annex B for Broadcast Box.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    private const uint VIDEO_CLOCK_RATE = 90000;
    private const int READ_CHUNK_SIZE = 16 * 1024;

    private readonly ISourceNode _inner;
    private readonly EdgeOptions _options;
    private readonly ILogger _logger;
    private readonly VideoFormat _videoFormat = RouteVideoFormats.H264;
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
    /// Starts ffmpeg reading s16le mono PCM on stdin, rendering the scope and writing an H264 Annex B
    /// stream (with Access Unit Delimiters) to stdout. Assumes <see cref="_ffmpegLock"/> is held.
    /// </summary>
    private bool StartFfmpeg(int sampleRate)
    {
        string filter = BuildFilter();
        string fileName = string.IsNullOrWhiteSpace(_options.FfmpegPath)
            ? "ffmpeg"
            : Path.Combine(_options.FfmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

        // libx264 in baseline/zerolatency: one access unit per input frame, no reordering. aud=1
        // inserts an Access Unit Delimiter at the start of each AU so the reader can frame the stream;
        // a ~1s keyframe interval lets a receiver that joins late sync quickly (no PLI handling here).
        var startInfo = new ProcessStartInfo(fileName)
        {
            Arguments = $"-hide_banner -loglevel error -f s16le -ar {sampleRate} -ac 1 -i - " +
                        $"-filter_complex \"[0:a]{filter}[v]\" -map \"[v]\" " +
                        $"-c:v libx264 -profile:v baseline -tune zerolatency -pix_fmt yuv420p " +
                        $"-g {Math.Max(1, _options.Fps)} -x264-params aud=1 -f h264 -",
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

        _reader = new Thread(ReadAnnexBLoop) { IsBackground = true, Name = "audio-scope-h264" };
        _reader.Start();

        _logger.LogDebug("Audio scope ffmpeg started: {Mode} {Size} @ {Fps}fps H264 from {Rate}Hz audio.",
            _options.ScopeMode, _options.ScopeSize, _options.Fps, sampleRate);
        return true;
    }

    private string BuildFilter()
    {
        string size = _options.ScopeSize;
        int fps = _options.Fps;

        // Both filters convert audio to video; normalise to the target fps and the 4:2:0 pixel format
        // H264 baseline needs. showwaves is a moving waveform; showspectrum is a scrolling spectrum.
        return _options.ScopeMode.Equals("spectrum", StringComparison.OrdinalIgnoreCase)
            ? $"showspectrum=s={size}:slide=scroll:color=intensity,fps={fps},format=yuv420p"
            : $"showwaves=s={size}:mode=cline:colors=0x33CC33,fps={fps},format=yuv420p";
    }

    /// <summary>
    /// Reads the H264 Annex B stream from ffmpeg's stdout and emits one encoded frame (access unit)
    /// per Access Unit Delimiter into the graph. The AUDs (x264 aud=1) are the frame boundaries: the
    /// bytes from one AUD start code up to the next are one access unit. The final (unterminated) AU
    /// is emitted when ffmpeg closes its stdout.
    /// </summary>
    private void ReadAnnexBLoop()
    {
        Stream stdout = _ffmpeg!.StandardOutput.BaseStream;
        var buffer = new List<byte>(64 * 1024);
        var readChunk = new byte[READ_CHUNK_SIZE];
        try
        {
            while (!_stopping)
            {
                int n = stdout.Read(readChunk, 0, readChunk.Length);
                if (n <= 0)
                {
                    break; // ffmpeg closed its stdout (exited).
                }

                for (int i = 0; i < n; i++)
                {
                    buffer.Add(readChunk[i]);
                }

                EmitCompleteAccessUnits(buffer);
            }

            // Emit any final, complete-enough access unit left buffered at EOF.
            if (buffer.Count > 4)
            {
                EmitAccessUnit(buffer, 0, buffer.Count);
            }
        }
        catch (Exception excp)
        {
            if (!_stopping)
            {
                _logger.LogWarning("Audio scope reader stopped: {Error}", excp.Message);
            }
        }
    }

    /// <summary>
    /// Emits every access unit that is fully buffered (i.e. followed by the next AUD) and trims them
    /// from the buffer, leaving the trailing partial access unit for the next read.
    /// </summary>
    private void EmitCompleteAccessUnits(List<byte> buffer)
    {
        var audIndices = new List<int>();
        for (int i = 0; i + 4 < buffer.Count;)
        {
            if (IsAudStartCode(buffer, i, out int afterStartCode))
            {
                audIndices.Add(i);
                i = afterStartCode;
            }
            else
            {
                i++;
            }
        }

        // Need the next AUD to know the first AU is complete.
        if (audIndices.Count < 2)
        {
            return;
        }

        for (int k = 0; k < audIndices.Count - 1; k++)
        {
            EmitAccessUnit(buffer, audIndices[k], audIndices[k + 1] - audIndices[k]);
        }

        buffer.RemoveRange(0, audIndices[^1]);
    }

    private void EmitAccessUnit(List<byte> buffer, int start, int length)
    {
        if (length <= 0)
        {
            return;
        }

        byte[] au = new byte[length];
        buffer.CopyTo(start, au, 0, length);

        _videoTimestamp += _frameDurationRtpUnits;
        OnFrame?.Invoke(MediaFrame.ForVideo(au, _videoTimestamp, _videoFormat, _frameDurationRtpUnits));
    }

    /// <summary>
    /// True if an Annex B start code (00 00 01 or 00 00 00 01) at <paramref name="i"/> is followed by
    /// an Access Unit Delimiter NAL (type 9). <paramref name="afterStartCode"/> is the index of the
    /// NAL header byte.
    /// </summary>
    private static bool IsAudStartCode(List<byte> b, int i, out int afterStartCode)
    {
        afterStartCode = i;

        if (b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 0 && b[i + 3] == 1)
        {
            afterStartCode = i + 4;
        }
        else if (b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 1)
        {
            afterStartCode = i + 3;
        }
        else
        {
            return false;
        }

        return afterStartCode < b.Count && (b[afterStartCode] & 0x1F) == 9;
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
