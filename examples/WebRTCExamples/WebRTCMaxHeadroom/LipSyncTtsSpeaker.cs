//-----------------------------------------------------------------------------
// Filename: LipSyncTtsSpeaker.cs
//
// Description: Base class for the avatar's text-to-speech "speakers". It owns the
// shared pipeline - serialise utterances, resample to 16kHz, stream the PCM to the
// WebRTC audio track, and drive the mouth from an amplitude (RMS) envelope of the
// audio - so each concrete engine only has to implement SynthesiseAsync (text in,
// 16-bit mono PCM + sample rate out). Implementations: PiperTtsSpeaker (local) and
// ElevenLabsTtsSpeaker (cloud).
//
// None of these engines emit a viseme timeline (only Azure did), so lip-sync is
// reconstructed here: a short-window RMS envelope of the synthesised speech drives
// the mouth openness (louder => more open), mapped onto a handful of the existing
// 0-21 viseme shapes in MaxHeadroomVideoSource.
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
    private const int EnvelopeFrameMs = 30;      // Mouth update granularity / RMS window.

    // Amplitude bands (fraction of the utterance's peak RMS) -> candidate viseme ids, picked from
    // the 0-21 shape table in MaxHeadroomVideoSource. Rotating within a band adds a little life so
    // the mouth doesn't sit on one shape. Openness roughly increases with loudness.
    private static readonly int[] LowVisemes = { 19, 6, 14 };   // ~0.2 open
    private static readonly int[] MidVisemes = { 4, 1 };        // ~0.4 open
    private static readonly int[] HighVisemes = { 2, 11, 9 };   // ~0.6-0.8 open

    /// <summary>Shared HTTP client for the HTTP-based engines (Piper server, ElevenLabs).</summary>
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly MaxHeadroomVideoSource _video;
    private readonly AudioExtrasSource _audio;
    private readonly int _visemeLeadMs;
    private readonly SemaphoreSlim _speakLock = new(1, 1);
    private int _visemeRotation;

    protected LipSyncTtsSpeaker(MaxHeadroomVideoSource video, AudioExtrasSource audio, int visemeLeadMs)
    {
        _video = video;
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
            var envelope = BuildEnvelope(samples);

            logger.LogInformation("[{Engine}] Synthesised {Samples} samples ({Ms} ms).",
                EngineName, samples.Length, samples.Length * 1000 / TargetRate);

            _video.IsSpeaking = true;
            var stopwatch = Stopwatch.StartNew();

            // Walk the amplitude envelope in real time alongside playback, leading the audio by
            // _visemeLeadMs so the mouth lands in sync once the slower video path reaches the viewer.
            var mouthTask = Task.Run(async () =>
            {
                for (int i = 0; i < envelope.Length; i++)
                {
                    var delay = (long)i * EnvelopeFrameMs - _visemeLeadMs - stopwatch.ElapsedMilliseconds;
                    if (delay > 0)
                    {
                        await Task.Delay((int)delay).ConfigureAwait(false);
                    }
                    _video.CurrentViseme = VisemeForLevel(envelope[i]);
                }
            });

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
            _video.CurrentViseme = 0;
            _video.IsSpeaking = false;
            _speakLock.Release();
        }
    }

    /// <summary>Per-frame RMS envelope, normalised to the utterance's peak (0..1) so the mouth adapts to volume.</summary>
    private static float[] BuildEnvelope(short[] samples)
    {
        int frame = TargetRate * EnvelopeFrameMs / 1000;
        if (frame <= 0)
        {
            return Array.Empty<float>();
        }

        int frames = (samples.Length + frame - 1) / frame;
        var rms = new float[frames];
        float peak = 1f;

        for (int f = 0; f < frames; f++)
        {
            int start = f * frame;
            int end = Math.Min(start + frame, samples.Length);
            double sumSq = 0;
            for (int i = start; i < end; i++)
            {
                double s = samples[i];
                sumSq += s * s;
            }
            float v = (float)Math.Sqrt(sumSq / Math.Max(1, end - start));
            rms[f] = v;
            if (v > peak) { peak = v; }
        }

        for (int f = 0; f < frames; f++)
        {
            rms[f] /= peak;
        }
        return rms;
    }

    /// <summary>Maps a normalised loudness level (0..1) to a viseme id, rotating within each band for liveliness.</summary>
    private int VisemeForLevel(float level)
    {
        if (level < 0.15f)
        {
            return 0; // closed / silence
        }

        int[] band = level < 0.40f ? LowVisemes : level < 0.70f ? MidVisemes : HighVisemes;
        return band[_visemeRotation++ % band.Length];
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
