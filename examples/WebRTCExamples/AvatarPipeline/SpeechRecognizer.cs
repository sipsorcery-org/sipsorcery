//-----------------------------------------------------------------------------
// Filename: SpeechRecognizer.cs
//
// Description: Base class for the avatar's speech-to-text "recognisers". It owns the
// shared streaming front-end - a simple energy-based voice-activity detector that
// buffers the decoded 8kHz 16-bit mono microphone PCM (pushed in via Write), and once
// it sees ~0.6s of trailing silence (or a hard length cap) hands the completed
// utterance to a single background worker. Concrete engines only implement
// TranscribeAsync (utterance PCM in, text out): SherpaSpeechRecognizer (local,
// in-process) and ElevenLabsSpeechRecognizer (cloud).
//
// This segmentation exists because neither engine is a true streaming recogniser -
// both transcribe a complete chunk - so the avatar listens utterance-by-utterance
// rather than continuously. (A streaming zipformer model via sherpa-onnx could replace
// this VAD with server-side endpointing.)
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace demo;

public abstract class SpeechRecognizer : ISpeechRecognizer
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<SpeechRecognizer>();

    // VAD / segmentation tuning, all against the incoming 8kHz stream.
    protected const int SampleRate = 8000;
    private const double SilenceRmsThreshold = 350.0;               // RMS below this (16-bit) counts as silence.
    private const int TrailingSilenceSamples = SampleRate * 6 / 10; // ~0.6s of silence ends an utterance.
    private const int MaxUtteranceSamples = SampleRate * 15;        // Hard cap so we always flush eventually.
    private const int MinUtteranceSamples = SampleRate * 3 / 10;    // Ignore blips shorter than ~0.3s.

    private readonly List<short> _utterance = new();
    private bool _hasSpeech;
    private int _trailingSilence;

    private readonly Channel<short[]> _utterances = Channel.CreateUnbounded<short[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private Task _worker;

    private bool _started;
    private bool _disposed;

    /// <summary>Raised with the final text of each recognised utterance (never empty/partial).</summary>
    public event Action<string> OnRecognized;

    /// <summary>Short name of the concrete engine, used in log messages.</summary>
    protected abstract string EngineName { get; }

    /// <summary>Cancellation token tied to the recogniser's lifetime, for engines to honour.</summary>
    protected CancellationToken ShutdownToken => _cts.Token;

    /// <summary>One-off engine initialisation (e.g. load a model), called before the worker starts.</summary>
    protected abstract Task InitAsync();

    /// <summary>Transcribes one completed utterance of 8kHz 16-bit mono PCM to text.</summary>
    protected abstract Task<string> TranscribeAsync(short[] pcm8k);

    /// <summary>Initialises the engine and starts the background recognition worker; safe to call once.</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        await InitAsync().ConfigureAwait(false);

        _worker = Task.Run(ConsumeAsync);
        logger.LogInformation("{Engine} speech recognition started. Speak to the avatar.", EngineName);
    }

    /// <summary>Pushes a block of decoded 8kHz 16-bit mono PCM into the recogniser.</summary>
    public void Write(short[] pcm)
    {
        if (_disposed || !_started || pcm == null || pcm.Length == 0)
        {
            return;
        }

        bool isSpeech = Rms(pcm) > SilenceRmsThreshold;

        if (isSpeech)
        {
            _utterance.AddRange(pcm);
            _hasSpeech = true;
            _trailingSilence = 0;
        }
        else if (_hasSpeech)
        {
            // Keep trailing silence in the buffer; it gives the recogniser a cleaner word boundary.
            _utterance.AddRange(pcm);
            _trailingSilence += pcm.Length;
        }
        // else: leading silence before any speech - drop it so we never transcribe pure silence.

        if (_hasSpeech && (_trailingSilence >= TrailingSilenceSamples || _utterance.Count >= MaxUtteranceSamples))
        {
            Flush();
        }
    }

    private void Flush()
    {
        if (_utterance.Count >= MinUtteranceSamples)
        {
            _utterances.Writer.TryWrite(_utterance.ToArray());
        }

        _utterance.Clear();
        _hasSpeech = false;
        _trailingSilence = 0;
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var pcm8k in _utterances.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                string text;
                try
                {
                    text = Clean(await TranscribeAsync(pcm8k).ConfigureAwait(false));
                }
                catch (Exception excp)
                {
                    logger.LogError(excp, "{Engine} failed to transcribe an utterance.", EngineName);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    logger.LogInformation("Recognised: \"{Text}\"", text);
                    OnRecognized?.Invoke(text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception excp)
        {
            logger.LogError(excp, "{Engine} recognition worker failed.", EngineName);
        }
    }

    /// <summary>Root-mean-square amplitude of a 16-bit PCM block, used as a cheap speech/silence test.</summary>
    private static double Rms(short[] pcm)
    {
        double sumSq = 0;
        for (int i = 0; i < pcm.Length; i++)
        {
            double s = pcm[i];
            sumSq += s * s;
        }
        return Math.Sqrt(sumSq / pcm.Length);
    }

    /// <summary>
    /// Trims and drops bracketed non-speech markers (e.g. "[BLANK_AUDIO]", "(silence)") that Whisper
    /// can emit on near-silent input, so they never reach the LLM. Harmless for plain-text engines.
    /// </summary>
    private static string Clean(string text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            return string.Empty;
        }
        if ((t.StartsWith('[') && t.EndsWith(']')) || (t.StartsWith('(') && t.EndsWith(')')))
        {
            return string.Empty;
        }
        return t;
    }

    /// <summary>Engine-specific cleanup, called after the worker has stopped.</summary>
    protected virtual void DisposeEngine() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try { _cts.Cancel(); } catch { /* best effort */ }
        _utterances.Writer.TryComplete();

        try { _worker?.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }

        DisposeEngine();
        _cts.Dispose();
    }
}
