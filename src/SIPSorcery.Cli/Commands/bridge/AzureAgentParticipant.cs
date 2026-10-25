//-----------------------------------------------------------------------------
// Filename: AzureAgentParticipant.cs
//
// Description: The "agent" bridge endpoint: a locally-assembled voice agent that
// can be spoken to. Inbound audio (the other participant's OPUS) is decoded to
// 16kHz PCM and fed to Azure speech-to-text; each recognised utterance is sent to
// an OpenAI-compatible LLM (in-character, e.g. Max Headroom) and the reply is
// synthesised with Azure TTS, encoded back to OPUS and emitted into the bridge.
//
// A duplex participant: Write consumes inbound audio (the ears), OnFrame produces
// the agent's speech (the voice). It reuses the speech-to-text proven in the
// WebRTCMaxHeadroom example. Audio-only for now - the TTS viseme timeline is kept
// for a future --avatar that puts a lip-synced face on the same voice.
//
// OPUS is decoded directly at 16kHz and the TTS PCM re-encoded as 16kHz OPUS (no
// resampler; the OPUS RTP clock stays 48kHz, RFC 7587). The browser's echo
// canceller keeps the agent out of its own microphone, so it is full duplex.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class AzureAgentParticipant : IBridgeParticipant, IGreetable, ITranscriptSource
{
    public const string DEFAULT_GREETING = "M-m-max Headroom here. Welcome to the show!";

    private const int OPUS_RTP_CLOCK_RATE = 48000;          // OPUS RTP timestamp clock (RFC 7587).
    private const int FRAME_MS = 20;                        // One OPUS frame per 20 ms.

    // How far (ms) to drive the avatar mouth ahead of the audio it belongs to. The WebRTCMaxHeadroom
    // example uses +150 (mouth ahead) because there its video path is slower than audio. The bridge is
    // the other way round: audio is hand-paced as 20 ms frames and pushed through extra graph hops plus
    // the browser's audio jitter buffer, so it lands later than the (same-as-example) avatar video and
    // the mouth was running ahead. A small negative lead (a lag) pulls the mouth back onto the sound.
    // Tune: more negative if the mouth still leads the voice; toward 0/positive if it starts to lag.
    private const int VISEME_LEAD_MS = -100;

    private readonly AzureSpeechRecognizer _recognizer;
    private readonly AzureTts _tts;
    private readonly LlmClient _llm;
    private readonly string _greeting;
    private readonly ILogger _logger;

    private readonly AudioEncoder _micDecoder = new();      // decode inbound OPUS -> PCM (for STT)
    private readonly AudioEncoder _ttsEncoder = new();      // encode TTS 16 kHz PCM -> OPUS
    private readonly AudioFormat _opus16k;                  // OPUS at 16 kHz, for encoding the TTS reply
    private readonly AudioFormat _opus48k;                  // OPUS at native 48 kHz, for decoding the mic

    private readonly AgentAvatar? _avatar;                  // null unless --avatar: the lip-synced face

    private readonly SemaphoreSlim _speakLock = new(1, 1);  // one utterance (greeting/reply) at a time
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationToken _ct;
    private uint _ttsTimestamp;
    private int _framesSpoken;

    // Half-duplex gate: while the agent is speaking (and a short tail after) the microphone is NOT fed
    // to speech-to-text, so the agent's own voice leaking back through imperfect browser echo
    // cancellation cannot pollute recognition (the main reason naive STT-in-a-voice-bot fails).
    private const int ECHO_TAIL_MS = 300;
    private volatile bool _speaking;
    private long _gateUntilTicks;
    private int _micFrames;
    private int _micWindowPeak;
    private long _bytesSpoken;

    public event Action<MediaFrame>? OnFrame;

    public event Action<string, string>? OnTranscript;

    public long? ConnectTimeMs => null;

    public Task Completion => _completion.Task;

    public AzureAgentParticipant(
        string azureKey, string azureRegion, string voice, string? persona,
        string? llmEndpoint, string? llmModel, string? llmApiKey, string? greeting, string? avatar, ILogger logger)
    {
        _logger = logger;
        _greeting = string.IsNullOrWhiteSpace(greeting) ? DEFAULT_GREETING : greeting!;

        _recognizer = new AzureSpeechRecognizer(azureKey, azureRegion, logger);
        _recognizer.OnRecognized += text => _ = HandleUtteranceAsync(text);

        _tts = new AzureTts(azureKey, azureRegion, voice, logger);
        _llm = new LlmClient(llmEndpoint, llmModel, llmApiKey, persona, logger);

        var f = AudioCommonlyUsedFormats.OpusWebRTC;
        f.ClockRate = AzureTts.SampleRate;                 // encode the TTS reply as 16 kHz OPUS
        _opus16k = f;

        // Decode the browser microphone at OPUS's native 48 kHz: Concentus produces silence when asked
        // to decode 48 kHz-encoded packets at a non-native rate, so we decode at 48 kHz and resample
        // down to the 16 kHz the recogniser wants.
        _opus48k = AudioCommonlyUsedFormats.OpusWebRTC;    // ClockRate is already 48000

        if (avatar != null)
        {
            // The face: rendered + H264-encoded continuously. During SpeakAsync the wav2lip head is
            // handed each utterance's TTS audio for its model; the max cartoon follows the TTS visemes.
            _avatar = AgentAvatar.Create(avatar, audioDrivenMouth: false, logger);
            _avatar.OnFrame += frame => OnFrame?.Invoke(frame);
        }
    }

    public string Describe() =>
        $"agent (azure stt -> llm{(_llm.IsConfigured ? "" : " (echo)")} -> azure tts{(_avatar != null ? $" + {_avatar.Name} avatar" : "")})";

    public async Task StartAsync(CancellationToken ct)
    {
        _ct = ct;
        if (_avatar != null)
        {
            // The renderer runs continuously (idle face when silent), so video flows the whole session.
            await _avatar.StartAsync().ConfigureAwait(false);
        }
        await _recognizer.StartAsync().ConfigureAwait(false);
        _logger.LogDebug("Voice agent started{Avatar}.", _avatar != null ? " with avatar" : "");

        if (_llm.IsConfigured)
        {
            _logger.LogDebug("Agent LLM: model '{Model}' at {Endpoint}.", _llm.Model, _llm.Endpoint);
        }
        else
        {
            _logger.LogDebug("Agent LLM: none configured (--llm/LLM_ENDPOINT unset); echoing recognised speech.");
        }
    }

    /// <summary>Inbound audio from the other participant: decode to 16 kHz PCM and feed the recogniser
    /// (unless the agent is speaking, to keep its own voice out of recognition).</summary>
    public void Write(MediaFrame frame)
    {
        if (frame.Kind != MediaKind.Audio || frame.Payload.Length == 0)
        {
            return;
        }

        // Don't listen while we're talking (or for a short echo tail after), so the agent's own voice
        // leaking through imperfect browser AEC can't be mis-recognised as the user.
        if (_speaking || DateTime.UtcNow.Ticks < Interlocked.Read(ref _gateUntilTicks))
        {
            return;
        }

        try
        {
            short[] pcm48 = _micDecoder.DecodeAudio(frame.Payload, _opus48k);
            short[] pcm = PcmResampler.Resample(pcm48, 48000, AzureTts.SampleRate);
            _recognizer.Write(pcm);

            // Diagnostics (--verbose): confirm mic audio is reaching STT and at a usable level.
            _micFrames++;
            int peak = 0;
            for (int i = 0; i < pcm.Length; i++)
            {
                int a = Math.Abs((int)pcm[i]);
                if (a > peak) { peak = a; }
            }
            if (peak > _micWindowPeak) { _micWindowPeak = peak; }
            if (_micFrames % 100 == 0)
            {
                _logger.LogDebug("Mic->STT: {Frames} frames fed, recent peak {Pct}% of full scale.",
                    _micFrames, _micWindowPeak * 100 / 32767);
                _micWindowPeak = 0;
            }
        }
        catch (Exception excp)
        {
            _logger.LogWarning("Voice agent failed to decode inbound audio: {Error}", excp.Message);
        }
    }

    /// <summary>Speaks a one-off greeting (cued by the command when the far side connects).</summary>
    public void Greet() => _ = SpeakAsync(_greeting);

    private async Task HandleUtteranceAsync(string prompt)
    {
        OnTranscript?.Invoke("you", prompt.Trim());
        try
        {
            await foreach (var sentence in _llm.StreamReplyAsync(prompt))
            {
                await SpeakAsync(sentence).ConfigureAwait(false);
            }
        }
        catch (Exception excp)
        {
            _logger.LogWarning(excp, "Voice agent reply failed.");
        }
    }

    /// <summary>
    /// Synthesises text, encodes it to 20 ms OPUS frames and emits them paced at real time so the
    /// far side plays the speech at the right speed. Serialised so a greeting and a reply, or two
    /// replies, never interleave their audio.
    /// </summary>
    private async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        OnTranscript?.Invoke("ai", text.Trim());

        await _speakLock.WaitAsync(_ct).ConfigureAwait(false);
        _speaking = true;       // stop feeding the mic to STT for this turn (avoid hearing ourselves)
        try
        {
            var synthesis = await _tts.SynthesizeAsync(text).ConfigureAwait(false);
            byte[] pcm = synthesis.Pcm;                     // 16 kHz 16-bit mono
            if (pcm.Length == 0)
            {
                return;
            }

            int frameSamples = AzureTts.SampleRate / 1000 * FRAME_MS;   // 320 samples / 20 ms
            int frameBytes = frameSamples * sizeof(short);             // 640 bytes
            uint durationRtpUnits = (uint)(OPUS_RTP_CLOCK_RATE / 1000 * FRAME_MS); // 960 @ 48 kHz

            var visemes = synthesis.Visemes;
            int visemeIndex = 0;
            var face = _avatar?.MaxFace;
            if (face != null)
            {
                face.IsSpeaking = true;
            }
            else if (_avatar != null)
            {
                // The wav2lip head paces its mouth itself, so it is given the whole utterance up front
                // and follows it on its own clock from here, as the audio below is paced out.
                var speech = new short[pcm.Length / sizeof(short)];
                Buffer.BlockCopy(pcm, 0, speech, 0, speech.Length * sizeof(short));
                _avatar.BeginSpeech();
                _avatar.PushSpeech(speech, AzureTts.SampleRate);
            }

            var sw = Stopwatch.StartNew();
            int frameIndex = 0;

            for (int offset = 0; offset < pcm.Length; offset += frameBytes)
            {
                if (_ct.IsCancellationRequested)
                {
                    break;
                }

                // Drive the avatar mouth from the viseme timeline, leading/lagging the audio by
                // VISEME_LEAD_MS so the rendered mouth lands on the sound once both reach the viewer.
                while (face != null && visemeIndex < visemes.Count && visemes[visemeIndex].OffsetMs - VISEME_LEAD_MS <= sw.ElapsedMilliseconds)
                {
                    face.CurrentViseme = visemes[visemeIndex].VisemeId;
                    visemeIndex++;
                }

                var samples = new short[frameSamples];     // zero-padded for the trailing partial frame
                int bytes = Math.Min(frameBytes, pcm.Length - offset);
                Buffer.BlockCopy(pcm, offset, samples, 0, bytes);

                byte[] encoded;
                try
                {
                    encoded = _ttsEncoder.EncodeAudio(samples, _opus16k);
                }
                catch (Exception excp)
                {
                    _logger.LogWarning("Voice agent OPUS encode failed: {Error}", excp.Message);
                    break;
                }

                if (encoded.Length > 0)
                {
                    _ttsTimestamp += durationRtpUnits;
                    OnFrame?.Invoke(MediaFrame.ForAudio(encoded, _ttsTimestamp, durationRtpUnits, AudioCommonlyUsedFormats.OpusWebRTC));
                    Interlocked.Increment(ref _framesSpoken);
                    Interlocked.Add(ref _bytesSpoken, encoded.Length);
                }

                // Pace to real time using the wall clock (no per-frame drift).
                frameIndex++;
                long waitMs = (long)frameIndex * FRAME_MS - sw.ElapsedMilliseconds;
                if (waitMs > 0)
                {
                    await Task.Delay((int)waitMs, _ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-utterance.
        }
        finally
        {
            if (_avatar?.MaxFace is { } restFace)
            {
                restFace.CurrentViseme = 0;     // mouth back to rest
                restFace.IsSpeaking = false;
            }
            _avatar?.EndSpeech();
            // Keep the mic gated for a short echo tail so the speaker's decay doesn't get recognised.
            Interlocked.Exchange(ref _gateUntilTicks, DateTime.UtcNow.AddMilliseconds(ECHO_TAIL_MS).Ticks);
            _speaking = false;
            _speakLock.Release();
        }
    }

    public SinkStats GetStats() => new(_framesSpoken, Interlocked.Read(ref _bytesSpoken), 0);

    public async ValueTask DisposeAsync()
    {
        _completion.TrySetResult();
        if (_avatar != null)
        {
            try { await _avatar.CloseAsync().ConfigureAwait(false); } catch { /* best effort */ }
            _avatar.Dispose();
        }
        try { _recognizer.Dispose(); } catch { /* best effort */ }
        _speakLock.Dispose();
    }
}
