//-----------------------------------------------------------------------------
// Filename: ElevenLabsStreamingTtsSpeaker.cs
//
// Description: Low-latency cloud text-to-speech using the ElevenLabs WebSocket
// streaming API (/v1/text-to-speech/{voice_id}/stream-input). Unlike the batch
// engines (LipSyncTtsSpeaker), this consumes text INCREMENTALLY - it is fed the LLM's
// token/sentence stream - and audio chunks stream back continuously, so the avatar can
// start talking before the full reply has been generated or synthesised.
//
// Pipeline per reply:
//   * a sender task forwards each text chunk into the socket, then an end-of-stream marker;
//   * a receive loop decodes the base64 PCM (we request output_format=pcm_16000) into a queue;
//   * a player task plays the queued PCM chunks in order via AudioExtrasSource and, for each
//     chunk, drives the mouth from that chunk's amplitude (RMS) envelope while it plays.
//
// The mouth is deliberately driven by the audio amplitude of each chunk - computed from and
// applied during the same blocking play call - rather than the API's alignment timestamps.
// Alignment timing is relative to the stream while playback advances chunk-by-chunk with
// small gaps, so an alignment-clock mouth drifts ahead and freezes ~2/3 through long
// sentences; tying the mouth to the chunk being played keeps it in sync, the same way the
// batch engines do.
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
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;

namespace demo;

public sealed class ElevenLabsStreamingTtsSpeaker : IStreamingAvatarSpeaker
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<ElevenLabsStreamingTtsSpeaker>();

    private const int TargetRate = 16000;     // output_format=pcm_16000 -> 16-bit mono PCM at 16kHz.
    private const int EnvelopeFrameMs = 30;   // Audio window granularity handed to the renderer.

    private readonly string _apiKey;
    private readonly string _voiceId;
    private readonly string _modelId;
    private readonly IAvatarMouth _renderer;
    private readonly AudioExtrasSource _audio;
    private readonly int _visemeLeadMs;
    private readonly SemaphoreSlim _speakLock = new(1, 1);

    public ElevenLabsStreamingTtsSpeaker(string apiKey, string voiceId, string modelId,
        IAvatarMouth renderer, AudioExtrasSource audio, int visemeLeadMs = 150)
    {
        _apiKey = apiKey;
        _voiceId = voiceId;
        _modelId = modelId;
        _renderer = renderer;
        _audio = audio;
        _visemeLeadMs = visemeLeadMs;
    }

    /// <summary>Speaks a single complete piece of text (wraps it as a one-item stream).</summary>
    public Task SpeakAsync(string text) => SpeakStreamAsync(Single(text));

    private static async IAsyncEnumerable<string> Single(string text)
    {
        yield return text;
        await Task.CompletedTask;
    }

    public async Task SpeakStreamAsync(IAsyncEnumerable<string> textChunks)
    {
        await _speakLock.WaitAsync().ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        // PCM chunks waiting to be played, in arrival order.
        var audioQueue = Channel.CreateUnbounded<short[]>(new UnboundedChannelOptions { SingleReader = true });

        try
        {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("xi-api-key", _apiKey);

            var uri = new Uri($"wss://api.elevenlabs.io/v1/text-to-speech/{_voiceId}/stream-input" +
                              $"?model_id={_modelId}&output_format=pcm_{TargetRate}");
            await ws.ConnectAsync(uri, ct).ConfigureAwait(false);

            // Begin-of-stream: voice settings + a priming space (per the stream-input protocol).
            await SendJsonAsync(ws, new { text = " ", voice_settings = new { stability = 0.5, similarity_boost = 0.8 } }, ct)
                .ConfigureAwait(false);

            _renderer.BeginSpeech();

            var sender = Task.Run(() => SendTextAsync(ws, textChunks, ct), ct);
            var player = Task.Run(() => PlayAsync(audioQueue, ct), ct);

            await ReceiveLoopAsync(ws, audioQueue, ct).ConfigureAwait(false);

            await sender.ConfigureAwait(false);
            audioQueue.Writer.TryComplete();
            await player.ConfigureAwait(false);

            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception ElevenLabsStreamingTtsSpeaker.SpeakStreamAsync.");
        }
        finally
        {
            cts.Cancel();
            audioQueue.Writer.TryComplete();
            _renderer.EndSpeech();
            _speakLock.Release();
        }
    }

    /// <summary>Forwards each text chunk into the socket, then the end-of-stream marker.</summary>
    private async Task SendTextAsync(ClientWebSocket ws, IAsyncEnumerable<string> chunks, CancellationToken ct)
    {
        await foreach (var chunk in chunks.WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }
            // A trailing space nudges ElevenLabs to flush the chunk rather than wait for more context.
            var text = chunk.EndsWith(' ') ? chunk : chunk + " ";
            await SendJsonAsync(ws, new { text }, ct).ConfigureAwait(false);
        }
        await SendJsonAsync(ws, new { text = "" }, ct).ConfigureAwait(false); // end of stream.
    }

    /// <summary>Reads audio messages until the API signals the final frame.</summary>
    private async Task ReceiveLoopAsync(ClientWebSocket ws, Channel<short[]> audioQueue, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = await ReceiveMessageAsync(ws, buffer, ct).ConfigureAwait(false);
            if (message == null)
            {
                break; // socket closed.
            }

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.TryGetProperty("audio", out var audioEl) && audioEl.ValueKind == JsonValueKind.String)
            {
                var b64 = audioEl.GetString();
                if (!string.IsNullOrEmpty(b64))
                {
                    audioQueue.Writer.TryWrite(BytesToShorts(Convert.FromBase64String(b64)));
                }
            }

            if (root.TryGetProperty("isFinal", out var fin) && fin.ValueKind == JsonValueKind.True)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Plays queued PCM chunks in order. For each chunk, a short concurrent task walks the chunk in
    /// real time and hands each audio window to the renderer while the (blocking) play call runs, so
    /// the face stays locked to the audio that is actually sounding.
    /// </summary>
    private async Task PlayAsync(Channel<short[]> audioQueue, CancellationToken ct)
    {
        int frameSamples = TargetRate * EnvelopeFrameMs / 1000;

        await foreach (var chunk in audioQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            Task mouth = Task.CompletedTask;

            if (_renderer.PacesAudioInternally)
            {
                // Self-pacing renderer (the Wav2Lip head): hand it the whole chunk before playback
                // so the model's look-ahead has as much audio as we do.
                for (int start = 0; start < chunk.Length; start += frameSamples)
                {
                    int count = Math.Min(frameSamples, chunk.Length - start);
                    _renderer.PushAudio(new ReadOnlySpan<short>(chunk, start, count), TargetRate);
                }
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                mouth = Task.Run(async () =>
                {
                    for (int start = 0, i = 0; start < chunk.Length; start += frameSamples, i++)
                    {
                        var delay = (long)i * EnvelopeFrameMs - _visemeLeadMs - stopwatch.ElapsedMilliseconds;
                        if (delay > 0)
                        {
                            await Task.Delay((int)delay, ct).ConfigureAwait(false);
                        }
                        int count = Math.Min(frameSamples, chunk.Length - start);
                        _renderer.PushAudio(new ReadOnlySpan<short>(chunk, start, count), TargetRate);
                    }
                }, ct);
            }

            await _audio.SendAudioFromStream(ToStream(chunk), AudioSamplingRatesEnum.Rate16KHz).ConfigureAwait(false);
            await mouth.ConfigureAwait(false);
        }
    }

    /// <summary>Reads one (possibly multi-frame) text message off the socket; null if it closed.</summary>
    private static async Task<string> ReceiveMessageAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task SendJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static short[] BytesToShorts(byte[] bytes)
    {
        var samples = new short[bytes.Length / sizeof(short)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(short));
        return samples;
    }

    private static MemoryStream ToStream(short[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new MemoryStream(bytes);
    }
}
