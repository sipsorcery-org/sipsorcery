//-----------------------------------------------------------------------------
// Filename: AzureSpeechRecognizer.cs
//
// Description: Continuous speech-to-text with the Azure Speech SDK for the voice
// agent - the listening counterpart to AzureTts. Decoded 16kHz 16-bit mono PCM
// (the bridge's Opus audio decoded at 16kHz) is pushed in via Write; each final
// recognised utterance is raised on OnRecognized for the agent to route through
// the LLM and speak.
//
// Same approach proven in the WebRTCMaxHeadroom example, at 16kHz here because the
// bridge audio is Opus (decoded wideband) rather than the example's 8kHz G.711.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class AzureSpeechRecognizer : IDisposable
{
    public const int SampleRate = 16000;

    private readonly ILogger _logger;
    private readonly PushAudioInputStream _pushStream;
    private readonly AudioConfig _audioConfig;
    private readonly SpeechRecognizer _recognizer;

    private bool _started;
    private bool _disposed;

    /// <summary>Raised with the final text of each recognised utterance (never empty/partial).</summary>
    public event Action<string>? OnRecognized;

    public AzureSpeechRecognizer(string subscriptionKey, string region, ILogger logger)
    {
        _logger = logger;

        var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        _pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(SampleRate, 16, 1));
        _audioConfig = AudioConfig.FromStreamInput(_pushStream);
        _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);

        _recognizer.Recognized += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
            {
                _logger.LogInformation("Recognised: \"{Text}\"", e.Result.Text);
                OnRecognized?.Invoke(e.Result.Text);
            }
        };

        // Partial hypotheses (--verbose): if these appear, Azure is getting usable audio and the issue
        // is endpointing/quality; if they never appear while you speak, the audio is not reaching it.
        _recognizer.Recognizing += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Result.Text))
            {
                _logger.LogDebug("Recognising: \"{Text}\"", e.Result.Text);
            }
        };

        _recognizer.Canceled += (s, e) =>
            _logger.LogWarning("Speech recognition canceled: {Reason}. {ErrorDetails}", e.Reason, e.ErrorDetails);
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        _logger.LogDebug("Speech recognition started ({Rate}Hz mono).", SampleRate);
    }

    /// <summary>Pushes a block of decoded 16kHz 16-bit mono PCM into the recogniser.</summary>
    public void Write(short[] pcm)
    {
        if (_disposed || pcm == null || pcm.Length == 0)
        {
            return;
        }

        var bytes = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        _pushStream.Write(bytes);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (_started)
            {
                _recognizer.StopContinuousRecognitionAsync().Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Speech recognizer stop error: {Error}", excp.Message);
        }

        try { _pushStream.Close(); } catch { /* best effort */ }
        _recognizer.Dispose();
        _audioConfig.Dispose();
        _pushStream.Dispose();
    }
}
