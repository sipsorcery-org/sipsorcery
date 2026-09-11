//-----------------------------------------------------------------------------
// Filename: ElevenLabsTtsSpeaker.cs
//
// Description: Cloud text-to-speech using ElevenLabs (https://elevenlabs.io) - one of
// the avatar's TTS engines (see LipSyncTtsSpeaker for the shared playback / lip-sync
// pipeline). ElevenLabs has the most natural voices of the options here but, unlike
// Piper, it is a paid cloud service, so it reintroduces an external dependency and a
// per-character cost. Offered as a quality option for deployments where that is an
// acceptable trade.
//
// Each utterance is a POST to /v1/text-to-speech/{voice_id} with the API key in the
// xi-api-key header. We request output_format=pcm_16000, so the response is raw 16-bit
// mono PCM at 16kHz - exactly what the base class wants, with no MP3 decode or resample.
// ElevenLabs does not return a viseme timeline, so the mouth is driven by the audio
// amplitude envelope in the base class, the same as Piper.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;

namespace demo;

public sealed class ElevenLabsTtsSpeaker : LipSyncTtsSpeaker
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<ElevenLabsTtsSpeaker>();

    // Raw 16-bit mono PCM at 16kHz: matches the base class TargetRate, so no decode/resample needed.
    private const string OutputFormat = "pcm_16000";

    private readonly string _apiKey;
    private readonly string _voiceId;
    private readonly string _modelId;

    protected override string EngineName => "ElevenLabs";

    /// <param name="apiKey">ElevenLabs API key (sent as the xi-api-key header).</param>
    /// <param name="voiceId">Voice id to synthesise with (e.g. "21m00Tcm4TlvDq8ikWAM" for "Rachel").</param>
    /// <param name="modelId">Model id, e.g. "eleven_turbo_v2_5" (low latency) or "eleven_multilingual_v2".</param>
    public ElevenLabsTtsSpeaker(string apiKey, string voiceId, string modelId,
        IAvatarMouth renderer, AudioExtrasSource audio, int visemeLeadMs = 150)
        : base(renderer, audio, visemeLeadMs)
    {
        _apiKey = apiKey;
        _voiceId = voiceId;
        _modelId = modelId;
    }

    protected override async Task<(short[] samples, int sampleRate)> SynthesiseAsync(string text)
    {
        var url = $"https://api.elevenlabs.io/v1/text-to-speech/{_voiceId}?output_format={OutputFormat}";
        var body = JsonSerializer.Serialize(new { text, model_id = _modelId });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("xi-api-key", _apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            logger.LogError("ElevenLabs returned {Status} for \"{Text}\": {Detail}",
                (int)response.StatusCode, text, detail);
            return (Array.Empty<short>(), TargetRate);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var samples = new short[bytes.Length / sizeof(short)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(short));
        return (samples, TargetRate);
    }
}
