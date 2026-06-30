//-----------------------------------------------------------------------------
// Filename: WhisperSpeechRecognizer.cs
//
// Description: Local, offline speech-to-text using Whisper.net (a managed wrapper
// over whisper.cpp) - the listening counterpart to AzureTtsSpeaker and a drop-in
// replacement for the old AzureSpeechRecognizer. It exposes the same surface
// (StartAsync / Write / OnRecognized / Dispose) so the call wiring in Program.cs is
// unchanged.
//
// Unlike the Azure SDK, Whisper is NOT a streaming recogniser: it transcribes a
// complete chunk of audio in one shot. So this class does its own utterance
// segmentation. Decoded 8kHz 16-bit mono PCM (the avatar call's G.711 audio after
// RTP decode) is pushed in via Write; a simple energy-based voice-activity detector
// buffers speech and, once it sees ~0.6s of trailing silence (or the utterance hits
// a hard cap), the buffered audio is resampled to the 16kHz mono float Whisper
// expects and queued. A single background worker runs Whisper sequentially and
// raises OnRecognized with each final transcript.
//
// Whisper.net.Runtime ships native whisper.cpp binaries for win-x64, linux-x64 and
// osx, so this runs unchanged in a Linux container / Kubernetes pod (CPU only; no
// GPU or cloud dependency). The ggml model file is resolved from WHISPER_MODEL or,
// if absent, downloaded once to the app directory.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace demo;

public sealed class WhisperSpeechRecognizer : IDisposable
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<WhisperSpeechRecognizer>();

    // Default model: base English. Good accuracy and fast enough for near-real-time on CPU.
    // Override the file via WHISPER_MODEL or the download source via WHISPER_MODEL_URL.
    private const string DefaultModelFile = "ggml-base.en.bin";
    private const string DefaultModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";

    // VAD / segmentation tuning, all against the incoming 8kHz stream.
    private const int SampleRate = 8000;
    private const double SilenceRmsThreshold = 350.0;       // RMS below this (16-bit) counts as silence.
    private const int TrailingSilenceSamples = SampleRate * 6 / 10; // ~0.6s of silence ends an utterance.
    private const int MaxUtteranceSamples = SampleRate * 15;        // Hard cap so we always flush eventually.
    private const int MinUtteranceSamples = SampleRate * 3 / 10;    // Ignore blips shorter than ~0.3s.

    private readonly string _modelPath;
    private readonly string _modelUrl;
    private readonly string _language;

    private readonly List<short> _utterance = new();
    private bool _hasSpeech;
    private int _trailingSilence;

    private readonly Channel<short[]> _utterances = Channel.CreateUnbounded<short[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    private WhisperFactory _factory;
    private WhisperProcessor _processor;
    private Task _worker;

    private bool _started;
    private bool _disposed;

    /// <summary>Raised with the final text of each recognised utterance (never empty/partial).</summary>
    public event Action<string> OnRecognized;

    public WhisperSpeechRecognizer(string modelPath = null, string language = "en")
    {
        _modelPath = string.IsNullOrWhiteSpace(modelPath) ? DefaultModelFile : modelPath;
        _modelUrl = Environment.GetEnvironmentVariable("WHISPER_MODEL_URL") ?? DefaultModelUrl;
        _language = string.IsNullOrWhiteSpace(language) ? "en" : language;
    }

    /// <summary>Loads the model and starts the background recognition worker; safe to call once.</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        await EnsureModelAsync().ConfigureAwait(false);

        _factory = WhisperFactory.FromPath(_modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage(_language)
            .Build();

        _worker = Task.Run(ConsumeAsync);
        logger.LogInformation("Whisper speech recognition started (model {Model}, language {Lang}). Speak to the avatar.",
            Path.GetFileName(_modelPath), _language);
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
            // Keep trailing silence in the buffer; it gives Whisper a cleaner word boundary.
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
                var samples = To16kFloat(pcm8k);

                var sb = new StringBuilder();
                await foreach (var segment in _processor.ProcessAsync(samples, _cts.Token).ConfigureAwait(false))
                {
                    sb.Append(segment.Text);
                }

                var text = Clean(sb.ToString());
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
            logger.LogError(excp, "Whisper recognition worker failed.");
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

    /// <summary>Upsamples 8kHz 16-bit PCM to the 16kHz normalised float mono Whisper requires (linear interpolation, factor 2).</summary>
    private static float[] To16kFloat(short[] pcm8k)
    {
        int n = pcm8k.Length;
        var outBuf = new float[n * 2];
        const float scale = 1f / 32768f;
        for (int i = 0; i < n; i++)
        {
            short cur = pcm8k[i];
            short next = (i + 1 < n) ? pcm8k[i + 1] : cur;
            outBuf[2 * i] = cur * scale;
            outBuf[2 * i + 1] = (cur + next) * 0.5f * scale;
        }
        return outBuf;
    }

    /// <summary>
    /// Whisper emits bracketed non-speech markers (e.g. "[BLANK_AUDIO]", "(silence)") on near-silent
    /// input; treat those as empty so they never reach the LLM.
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

    /// <summary>Resolves the ggml model file, downloading it once if it isn't already present.</summary>
    private async Task EnsureModelAsync()
    {
        if (File.Exists(_modelPath))
        {
            return;
        }

        logger.LogInformation("Whisper model {Path} not found; downloading from {Url} (one-off).", _modelPath, _modelUrl);

        var dir = Path.GetDirectoryName(Path.GetFullPath(_modelPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await using var src = await http.GetStreamAsync(_modelUrl).ConfigureAwait(false);
        var tmp = _modelPath + ".download";
        await using (var dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst).ConfigureAwait(false);
        }
        File.Move(tmp, _modelPath, overwrite: true);

        logger.LogInformation("Whisper model downloaded to {Path}.", _modelPath);
    }

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

        _processor?.Dispose();
        _factory?.Dispose();
        _cts.Dispose();
    }
}
