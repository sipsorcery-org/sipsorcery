//-----------------------------------------------------------------------------
// Filename: AzureTts.cs
//
// Description: Azure Cognitive Services text-to-speech for the voice agent.
// Synthesises text to 16kHz 16-bit mono PCM and, at the same time, collects the
// Azure viseme timeline. The agent encodes the PCM to Opus and emits it into the
// bridge; the viseme timeline is returned but unused while the agent is audio-only
// (it is what a future --avatar would drive the mouth from).
//
// A trimmed, audio-only port of the WebRTCMaxHeadroom example's AzureTtsSpeaker
// (which is coupled to the avatar's AudioExtrasSource + video source).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class AzureTts
{
    /// <summary>The synthesised audio (16kHz 16-bit mono PCM) plus the viseme timeline (offset ms, id).</summary>
    public readonly record struct Synthesis(byte[] Pcm, IReadOnlyList<(long OffsetMs, int VisemeId)> Visemes);

    public const int SampleRate = 16000;

    private readonly SpeechConfig _speechConfig;
    private readonly ILogger _logger;

    public AzureTts(string subscriptionKey, string region, string voiceName, ILogger logger)
    {
        _logger = logger;
        _speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        _speechConfig.SpeechSynthesisVoiceName = voiceName;
        // Raw 16kHz PCM, no RIFF header.
        _speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
    }

    public async Task<Synthesis> SynthesizeAsync(string text)
    {
        var timeline = new List<(long, int)>();

        // No AudioConfig => the SDK does not play to the local speaker; we take the rendered PCM
        // from the result instead.
        using var synthesizer = new SpeechSynthesizer(_speechConfig, null);
        synthesizer.VisemeReceived += (s, e) => timeline.Add(((long)(e.AudioOffset / 10000), (int)e.VisemeId));

        using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(result);
            _logger.LogError("Azure TTS failed: {Reason}. {ErrorDetails}", result.Reason, details.ErrorDetails);
            return new Synthesis(Array.Empty<byte>(), timeline);
        }

        return new Synthesis(result.AudioData, timeline);
    }
}
