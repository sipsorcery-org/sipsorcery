//-----------------------------------------------------------------------------
// Filename: AzureTtsSpeaker.cs
//
// Description: Uses the Azure Cognitive Services Speech SDK to synthesise text
// to 16kHz 16-bit mono PCM and, at the same time, collect the viseme timeline
// (VisemeReceived events). The PCM is streamed to the WebRTC audio track via
// AudioExtrasSource.SendAudioFromStream while a parallel task walks the viseme
// timeline and updates MaxHeadroomVideoSource.CurrentViseme so the mouth stays
// in sync with the speech.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
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
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace demo
{
    public class AzureTtsSpeaker
    {
        private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<AzureTtsSpeaker>();

        private readonly SpeechConfig _speechConfig;
        private readonly MaxHeadroomVideoSource _video;
        private readonly AudioExtrasSource _audio;
        private readonly SemaphoreSlim _speakLock = new(1, 1);

        // How far ahead of the audio to drive the mouth, to compensate for the video
        // path (encode + browser jitter buffer + decode) arriving later than audio.
        // Increase if the mouth lags the sound; decrease if it leads it.
        private readonly int _visemeLeadMs;

        public AzureTtsSpeaker(string subscriptionKey, string region, string voiceName,
            MaxHeadroomVideoSource video, AudioExtrasSource audio, int visemeLeadMs = 150)
        {
            _video = video;
            _audio = audio;
            _visemeLeadMs = visemeLeadMs;

            _speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
            _speechConfig.SpeechSynthesisVoiceName = voiceName;
            // Raw PCM, no RIFF header, exactly what SendAudioFromStream expects at 16kHz.
            _speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);
        }

        /// <summary>
        /// Synthesises <paramref name="text"/> and plays it through the avatar with
        /// lip-sync. Only one utterance is spoken at a time.
        /// </summary>
        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await _speakLock.WaitAsync().ConfigureAwait(false);

            try
            {
                var timeline = new List<(long offsetMs, int visemeId)>();

                // No AudioConfig => the SDK does not play to the local speaker; we take
                // the rendered PCM from the result instead.
                using var synthesizer = new SpeechSynthesizer(_speechConfig, null);

                synthesizer.VisemeReceived += (s, e) =>
                {
                    // AudioOffset is in 100-nanosecond ticks.
                    timeline.Add(((long)(e.AudioOffset / 10000), (int)e.VisemeId));
                };

                logger.LogInformation("Synthesising: \"{Text}\"", text);

                using var result = await synthesizer.SpeakTextAsync(text).ConfigureAwait(false);

                if (result.Reason != ResultReason.SynthesizingAudioCompleted)
                {
                    var details = SpeechSynthesisCancellationDetails.FromResult(result);
                    logger.LogError("Azure TTS failed: {Reason}. {ErrorDetails}", result.Reason, details.ErrorDetails);
                    return;
                }

                logger.LogInformation("Synthesised {Bytes} PCM bytes, {VisemeCount} visemes.", result.AudioData.Length, timeline.Count);

                _video.IsSpeaking = true;

                var stopwatch = Stopwatch.StartNew();

                // Walk the viseme timeline in real time alongside the audio playback.
                var visemeTask = Task.Run(async () =>
                {
                    foreach (var (offsetMs, visemeId) in timeline)
                    {
                        // Lead the audio by _visemeLeadMs so the mouth lands in sync once
                        // the slower video path reaches the viewer.
                        var delay = offsetMs - _visemeLeadMs - stopwatch.ElapsedMilliseconds;
                        if (delay > 0)
                        {
                            await Task.Delay((int)delay).ConfigureAwait(false);
                        }
                        _video.CurrentViseme = visemeId;
                    }
                });

                await _audio.SendAudioFromStream(new MemoryStream(result.AudioData), AudioSamplingRatesEnum.Rate16KHz)
                    .ConfigureAwait(false);

                await visemeTask.ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception AzureTtsSpeaker.SpeakAsync.");
            }
            finally
            {
                _video.CurrentViseme = 0;
                _video.IsSpeaking = false;
                _speakLock.Release();
            }
        }
    }
}
