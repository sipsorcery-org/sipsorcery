//-----------------------------------------------------------------------------
// Filename: AzureSpeechRecognizer.cs
//
// Description: Continuous speech-to-text with the Azure Cognitive Services Speech
// SDK - the listening counterpart to AzureTtsSpeaker. Decoded 8kHz 16-bit mono PCM
// (the avatar call's G.711 audio, after decoding the received RTP) is pushed in via
// Write; each final recognised utterance is raised on OnRecognized for the caller
// to route through the LLM and speak. This is what lets you TALK to the avatar, in
// parallel with the /say and /ask text boxes.
//
// Uses the same Azure Speech resource (key + region) as the TTS, so no extra
// configuration or package is needed.
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

namespace demo;

public sealed class AzureSpeechRecognizer : IDisposable
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<AzureSpeechRecognizer>();

    private readonly PushAudioInputStream _pushStream;
    private readonly AudioConfig _audioConfig;
    private readonly SpeechRecognizer _recognizer;

    private bool _started;
    private bool _disposed;

    /// <summary>Raised with the final text of each recognised utterance (never empty/partial).</summary>
    public event Action<string> OnRecognized;

    public AzureSpeechRecognizer(string subscriptionKey, string region)
    {
        var speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);

        // The avatar call audio is 8kHz 16-bit mono once the received G.711 RTP is decoded.
        _pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(8000, 16, 1));
        _audioConfig = AudioConfig.FromStreamInput(_pushStream);
        _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);

        _recognizer.Recognized += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
            {
                logger.LogInformation("Recognised: \"{Text}\"", e.Result.Text);
                OnRecognized?.Invoke(e.Result.Text);
            }
        };

        _recognizer.Canceled += (s, e) =>
            logger.LogWarning("Speech recognition canceled: {Reason}. {ErrorDetails}", e.Reason, e.ErrorDetails);
    }

    /// <summary>Begins continuous recognition; safe to call once.</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        logger.LogInformation("Speech recognition started (8kHz mono). Speak to the avatar.");
    }

    /// <summary>Pushes a block of decoded 8kHz 16-bit mono PCM into the recogniser.</summary>
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
            logger.LogDebug("Speech recognizer stop error: {Error}", excp.Message);
        }

        try { _pushStream.Close(); } catch { /* best effort */ }
        _recognizer.Dispose();
        _audioConfig.Dispose();
        _pushStream.Dispose();
    }
}
