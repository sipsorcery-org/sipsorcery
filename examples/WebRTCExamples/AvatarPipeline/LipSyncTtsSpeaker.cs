//-----------------------------------------------------------------------------
// Filename: LipSyncTtsSpeaker.cs
//
// Description: Base class for the avatar's text-to-speech "speakers". It owns the
// shared pipeline - serialise utterances, resample to 16kHz, stream the PCM to the
// WebRTC audio track, and hand the audio to the IAvatarRenderer that animates the
// face - so each concrete engine only has to implement SynthesiseAsync (text in,
// 16-bit mono PCM + sample rate out). Implementations: SherpaTtsSpeaker (local,
// in-process) and ElevenLabsTtsSpeaker (cloud).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace demo;

public abstract class LipSyncTtsSpeaker : IAvatarSpeaker
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<LipSyncTtsSpeaker>();

    protected const int TargetRate = 16000;      // SendAudioFromStream consumes 16kHz mono PCM.
    private const int EnvelopeFrameMs = 30;      // Audio window granularity handed to the renderer.

    /// <summary>Shared HTTP client for the HTTP-based engines (ElevenLabs).</summary>
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly IAvatarMouth _renderer;
    private readonly AudioExtrasSource _audio;
    private readonly int _visemeLeadMs;
    private readonly SemaphoreSlim _speakLock = new(1, 1);

    protected LipSyncTtsSpeaker(IAvatarMouth renderer, AudioExtrasSource audio, int visemeLeadMs)
    {
        _renderer = renderer;
        _audio = audio;
        _visemeLeadMs = visemeLeadMs;
    }

    /// <summary>Short name of the concrete engine, used in log messages.</summary>
    protected abstract string EngineName { get; }

    /// <summary>Synthesises <paramref name="text"/>, returning 16-bit mono PCM and its sample rate.</summary>
    protected abstract Task<(short[] samples, int sampleRate)> SynthesiseAsync(string text);

    /// <summary>
    /// Synthesises <paramref name="text"/> and plays it through the avatar with amplitude-driven
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
            logger.LogInformation("[{Engine}] Synthesising: \"{Text}\"", EngineName, text);

            var (pcm, sampleRate) = await SynthesiseAsync(text).ConfigureAwait(false);
            if (pcm == null || pcm.Length == 0)
            {
                logger.LogError("[{Engine}] produced no audio for \"{Text}\".", EngineName, text);
                return;
            }

            var samples = sampleRate == TargetRate ? pcm : Resample(pcm, sampleRate, TargetRate);

            logger.LogInformation("[{Engine}] Synthesised {Samples} samples ({Ms} ms).",
                EngineName, samples.Length, samples.Length * 1000 / TargetRate);

            _renderer.BeginSpeech();

            int frameSamples = TargetRate * EnvelopeFrameMs / 1000;
            Task mouthTask = Task.CompletedTask;

            if (_renderer.PacesAudioInternally)
            {
                // The renderer paces itself (the Wav2Lip head): hand it the WHOLE utterance up
                // front so its model's look-ahead never waits on real-time delivery, then give
                // the slower video path a head start before the audio track plays.
                for (int start = 0; start < samples.Length; start += frameSamples)
                {
                    int count = Math.Min(frameSamples, samples.Length - start);
                    _renderer.PushAudio(new ReadOnlySpan<short>(samples, start, count), TargetRate);
                }
                if (_visemeLeadMs > 0)
                {
                    await Task.Delay(_visemeLeadMs).ConfigureAwait(false);
                }
            }
            else
            {
                // The renderer reacts instantly (cartoon): walk the audio in real time alongside
                // playback, leading by _visemeLeadMs so the face lands in sync once the slower
                // video path reaches the viewer.
                var stopwatch = Stopwatch.StartNew();
                mouthTask = Task.Run(async () =>
                {
                    for (int start = 0, i = 0; start < samples.Length; start += frameSamples, i++)
                    {
                        var delay = (long)i * EnvelopeFrameMs - _visemeLeadMs - stopwatch.ElapsedMilliseconds;
                        if (delay > 0)
                        {
                            await Task.Delay((int)delay).ConfigureAwait(false);
                        }
                        int count = Math.Min(frameSamples, samples.Length - start);
                        _renderer.PushAudio(new ReadOnlySpan<short>(samples, start, count), TargetRate);
                    }
                });
            }

            await _audio.SendAudioFromStream(ToStream(samples), AudioSamplingRatesEnum.Rate16KHz)
                .ConfigureAwait(false);

            await mouthTask.ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception {Engine}.SpeakAsync.", EngineName);
        }
        finally
        {
            _renderer.EndSpeech();
            _speakLock.Release();
        }
    }

    /// <summary>Linear resample of 16-bit mono PCM from <paramref name="srcRate"/> to <paramref name="dstRate"/>.</summary>
    protected static short[] Resample(short[] src, int srcRate, int dstRate)
    {
        if (src.Length == 0 || srcRate == dstRate)
        {
            return src;
        }

        long outLen = (long)src.Length * dstRate / srcRate;
        var dst = new short[outLen];
        double step = (double)srcRate / dstRate;

        for (long i = 0; i < outLen; i++)
        {
            double srcPos = i * step;
            int i0 = (int)srcPos;
            int i1 = Math.Min(i0 + 1, src.Length - 1);
            double frac = srcPos - i0;
            dst[i] = (short)(src[i0] * (1 - frac) + src[i1] * frac);
        }
        return dst;
    }

    private static MemoryStream ToStream(short[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new MemoryStream(bytes);
    }
}
