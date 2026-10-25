//-----------------------------------------------------------------------------
// Filename: TerminalParticipant.cs
//
// Description: The "term" bridge endpoint: the local terminal as a duplex peer.
// Its microphone is captured with an ffmpeg process (MicrophoneCapture) and sent
// to the other side as 20ms Opus frames; what the other side sends back (an
// agent's voice and, with --avatar, its face) is muxed as Matroska on stdout for
// a player to show, e.g.
//
//   route --from term --to openai --avatar | mpv --vo=tct --profile=low-latency -
//
// The agents do not produce a continuous audio timeline: speech arrives in bursts
// with silence between utterances, and the OpenAI voice carries no timestamps at
// all. A player needs a steady audio clock, so outgoing audio goes through an
// AudioPacer that emits exactly one frame per frame period of wall time - the next
// queued agent frame, or Opus silence when there is none. Video is stamped with its
// arrival time, so both tracks share the wall clock and stay in sync.
//
// With an avatar the audio clock is held until the avatar's first video frame
// arrives. The Matroska header must declare both tracks, and the sink only waits
// a few seconds for video; on a cold start (loading the native encoder) the first
// frame can take longer than that, and a header written without the video track
// would lose the avatar for the whole session. Holding the audio also means no
// backlog of silence builds up ahead of the picture.
//
// There is no echo cancellation (the browser does that for the web endpoint), so
// use headphones or the agent will hear itself.
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

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class TerminalParticipant : IBridgeParticipant, IConnectable
{
    private const uint OPUS_FRAME_RTP_UNITS = MicrophoneCapture.FRAME_SAMPLES;   // 960 at 48kHz
    private const int VIDEO_CLOCK_RATE = 90000;
    private const int VIDEO_WAIT_SECONDS = 10;   // How long to hold the audio for the avatar's first frame.

    private readonly MicrophoneCapture _mic;
    private readonly MatroskaSinkNode _output;
    private readonly ILogger _logger;
    private readonly AudioFormat _opus = AudioCommonlyUsedFormats.OpusWebRTC;
    private readonly AudioEncoder _micEncoder = new(includeOpus: true);
    private readonly AudioPacer _pacer;
    private readonly Stopwatch _clock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _expectVideo;
    private readonly TaskCompletionSource _firstVideo = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _pacing;
    private Task _completion = Task.CompletedTask;
    private uint _micTimestamp;
    private int _connected;
    private int _framesOut;
    private long _bytesOut;

    public event Action<MediaFrame>? OnFrame;

    /// <summary>Raised on the first microphone frame: the person at the terminal is there, so the agent can greet.</summary>
    public event Action? Connected;

    public long? ConnectTimeMs => null;

    public Task Completion => _completion;

    /// <param name="micDevice">The capture device, or null for the default (see <see cref="MicrophoneCapture"/>).</param>
    /// <param name="expectVideo">True when the other side sends video (an agent with --avatar), so the
    /// Matroska header waits for its first key frame.</param>
    public TerminalParticipant(string? micDevice, bool expectVideo, ILogger logger)
    {
        _logger = logger;
        _expectVideo = expectVideo;
        _mic = new MicrophoneCapture(micDevice, logger);
        _mic.OnSamples += HandleMicSamples;
        _output = new MatroskaSinkNode(null, "-:mkv", logger, expectVideo: expectVideo, expectAudio: true);
        byte[] silence = _micEncoder.EncodeAudio(new short[MicrophoneCapture.FRAME_SAMPLES], _opus);
        _pacer = new AudioPacer(silence, OPUS_FRAME_RTP_UNITS, _opus.RtpClockRate, (payload, timestamp, duration) =>
        {
            _output.Write(MediaFrame.ForAudio(payload, timestamp, duration, _opus));
            if (!ReferenceEquals(payload, silence))
            {
                CountOut(payload.Length);
            }
        });
    }

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            _mic.Start();
        }
        catch (InvalidOperationException excp)
        {
            throw new EdgeException(excp.Message);
        }

        _clock.Start();
        _pacing = Task.Run(() => PaceAudioAsync(_cts.Token));

        // The run ends when the mic stops (ffmpeg exits) or the player closes stdout.
        _completion = Task.WhenAny(_mic.Completion, _output.Closed);

        Console.Error.WriteLine($"Listening on {_mic.Description}. Use headphones: there is no echo cancellation.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the audio pacer, first waiting for the avatar's first video frame when one is expected (see
    /// the file header). The pacer's clock starts when it does, so the wait does not turn into a burst of
    /// catch-up silence.
    /// </summary>
    private async Task PaceAudioAsync(CancellationToken ct)
    {
        if (_expectVideo)
        {
            try
            {
                await _firstVideo.Task.WaitAsync(TimeSpan.FromSeconds(VIDEO_WAIT_SECONDS), ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("No avatar video arrived within {Seconds}s; continuing with audio only.", VIDEO_WAIT_SECONDS);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        long startMs = _clock.ElapsedMilliseconds;
        await _pacer.RunAsync(() => _clock.ElapsedMilliseconds - startMs, ct).ConfigureAwait(false);
    }

    private void HandleMicSamples(short[] pcm)
    {
        if (Interlocked.Exchange(ref _connected, 1) == 0)
        {
            Connected?.Invoke();
        }

        byte[] opus = _micEncoder.EncodeAudio(pcm, _opus);
        _micTimestamp += OPUS_FRAME_RTP_UNITS;
        OnFrame?.Invoke(MediaFrame.ForAudio(opus, _micTimestamp, OPUS_FRAME_RTP_UNITS, _opus));
    }

    public void Write(MediaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        if (frame.Kind == MediaKind.Audio)
        {
            // Opus only: every agent speaks Opus, and the pacer's silence filler is Opus.
            if (frame.AudioFormat.Codec != AudioCodecsEnum.OPUS)
            {
                return;
            }

            _pacer.Enqueue(frame.Payload, frame.DurationRtpUnits);
            return;
        }

        // Video goes straight out, stamped with the wall clock the audio pacer also runs on.
        uint wallTimestamp = (uint)(_clock.ElapsedMilliseconds * (VIDEO_CLOCK_RATE / 1000));
        _output.Write(frame with { RtpTimestamp = wallTimestamp });
        CountOut(frame.Payload.Length);
        _firstVideo.TrySetResult();
    }

    private void CountOut(int bytes)
    {
        Interlocked.Increment(ref _framesOut);
        Interlocked.Add(ref _bytesOut, bytes);
    }

    public SinkStats GetStats() => new(_framesOut, Interlocked.Read(ref _bytesOut), _output.GetStats().Dropped);

    public string Describe() => $"term ({_mic.Description} -> mkv on stdout)";

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_pacing != null)
        {
            try { await _pacing.ConfigureAwait(false); } catch { /* stopping */ }
        }

        _mic.OnSamples -= HandleMicSamples;
        await _mic.DisposeAsync().ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
