//-----------------------------------------------------------------------------
// Filename: ElevenLabsSpeechRecognizer.cs
//
// Description: Cloud speech-to-text using the ElevenLabs "scribe" API - one of the
// avatar's STT engines (see SpeechRecognizer for the shared streaming/segmentation
// front-end). Each completed utterance (8kHz 16-bit mono PCM) is wrapped in a WAV
// container and POSTed to /v1/speech-to-text; the returned JSON "text" is raised.
//
// Like the ElevenLabs TTS engine this is a paid cloud service, so it reintroduces an
// external dependency and a per-use cost - the opposite of the offline Whisper path.
// Offered for parity when you are already using ElevenLabs for the voice.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace demo;

public sealed class ElevenLabsSpeechRecognizer : SpeechRecognizer
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<ElevenLabsSpeechRecognizer>();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string Endpoint = "https://api.elevenlabs.io/v1/speech-to-text";

    private readonly string _apiKey;
    private readonly string _modelId;

    protected override string EngineName => "ElevenLabs";

    /// <param name="apiKey">ElevenLabs API key (sent as the xi-api-key header).</param>
    /// <param name="modelId">Speech-to-text model id, e.g. "scribe_v1".</param>
    public ElevenLabsSpeechRecognizer(string apiKey, string modelId = "scribe_v1")
    {
        _apiKey = apiKey;
        _modelId = string.IsNullOrWhiteSpace(modelId) ? "scribe_v1" : modelId;
    }

    protected override Task InitAsync() => Task.CompletedTask; // Nothing to load; it's a remote service.

    protected override async Task<string> TranscribeAsync(short[] pcm8k)
    {
        var wav = ToWav(pcm8k, SampleRate);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_modelId), "model_id");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "utterance.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Add("xi-api-key", _apiKey);

        using var response = await _http.SendAsync(request, ShutdownToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            logger.LogError("ElevenLabs STT returned {Status}: {Detail}", (int)response.StatusCode, detail);
            return string.Empty;
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() : string.Empty;
    }

    /// <summary>Wraps 16-bit mono PCM in a minimal RIFF/WAVE container at the given sample rate.</summary>
    private static byte[] ToWav(short[] pcm, int sampleRate)
    {
        int dataLen = pcm.Length * sizeof(short);
        using var ms = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(ms);

        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + dataLen);
        w.Write(new[] { 'W', 'A', 'V', 'E' });

        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);                          // fmt chunk size.
        w.Write((short)1);                    // PCM.
        w.Write((short)1);                    // mono.
        w.Write(sampleRate);
        w.Write(sampleRate * 2);              // byte rate = rate * channels * bytesPerSample.
        w.Write((short)2);                    // block align = channels * bytesPerSample.
        w.Write((short)16);                   // bits per sample.

        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(dataLen);
        foreach (var s in pcm)
        {
            w.Write(s);
        }

        w.Flush();
        return ms.ToArray();
    }
}
