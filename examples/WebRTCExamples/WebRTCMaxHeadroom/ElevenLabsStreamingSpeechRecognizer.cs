//-----------------------------------------------------------------------------
// Filename: ElevenLabsStreamingSpeechRecognizer.cs
//
// Description: Low-latency cloud speech-to-text using the ElevenLabs realtime
// WebSocket API (/v1/speech-to-text/realtime, Scribe v2). Unlike the batch recognisers
// (SpeechRecognizer), this does NO local voice-activity detection: the decoded 8kHz mic
// PCM is streamed straight to the server, which runs its own VAD and returns committed
// (final) transcripts as the speaker finishes each utterance. That removes the
// buffer-until-silence delay and gives lower-latency listening.
//
// Flow:
//   * Write enqueues mic PCM blocks (non-blocking, called from the RTP receive path);
//   * a sender task coalesces ~100ms of audio and sends it as base64 "input_audio_chunk"
//     messages (audio_format=pcm_8000, so the 8kHz mic needs no resample);
//   * a receive loop raises OnRecognized on each "committed_transcript" (final) message;
//     partial transcripts are ignored.
//
// This is a prototype: the commit strategy / message field names are best verified against
// a live account. It is purely additive - the batch recognisers are untouched.
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
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace demo;

public sealed class ElevenLabsStreamingSpeechRecognizer : ISpeechRecognizer
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<ElevenLabsStreamingSpeechRecognizer>();

    private const int SampleRate = 8000;          // The mic stream after G.711 decode.
    private const int SendBatchSamples = 800;     // ~100ms of 8kHz audio per WebSocket message.

    private readonly string _apiKey;
    private readonly string _modelId;
    private readonly string _commitStrategy;

    private readonly Channel<short[]> _audioQueue = Channel.CreateUnbounded<short[]>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    private ClientWebSocket _ws;
    private Task _sender;
    private Task _receiver;
    private bool _started;
    private bool _disposed;

    public event Action<string> OnRecognized;

    /// <param name="apiKey">ElevenLabs API key (sent as the xi-api-key header).</param>
    /// <param name="modelId">Realtime STT model id (default "scribe_v2_realtime").</param>
    /// <param name="commitStrategy">Server commit strategy; "vad" lets the server segment utterances.</param>
    public ElevenLabsStreamingSpeechRecognizer(string apiKey, string modelId = "scribe_v2_realtime", string commitStrategy = "vad")
    {
        _apiKey = apiKey;
        _modelId = string.IsNullOrWhiteSpace(modelId) ? "scribe_v2_realtime" : modelId;
        _commitStrategy = string.IsNullOrWhiteSpace(commitStrategy) ? "vad" : commitStrategy;
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }
        _started = true;

        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("xi-api-key", _apiKey);

        var uri = new Uri($"wss://api.elevenlabs.io/v1/speech-to-text/realtime" +
                          $"?model_id={_modelId}&audio_format=pcm_{SampleRate}&commit_strategy={_commitStrategy}");
        await _ws.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);

        _sender = Task.Run(SendLoopAsync);
        _receiver = Task.Run(ReceiveLoopAsync);
        logger.LogInformation("ElevenLabs realtime speech recognition started (model {Model}). Speak to the avatar.", _modelId);
    }

    public void Write(short[] pcm)
    {
        if (_disposed || !_started || pcm == null || pcm.Length == 0)
        {
            return;
        }
        _audioQueue.Writer.TryWrite(pcm);
    }

    /// <summary>Coalesces queued PCM into ~100ms batches and sends them as base64 audio-chunk messages.</summary>
    private async Task SendLoopAsync()
    {
        var batch = new List<short>(SendBatchSamples);
        try
        {
            await foreach (var pcm in _audioQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                batch.AddRange(pcm);
                if (batch.Count >= SendBatchSamples)
                {
                    await SendAudioAsync(batch.ToArray()).ConfigureAwait(false);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception excp)
        {
            logger.LogError(excp, "ElevenLabs STT sender failed.");
        }
    }

    private async Task SendAudioAsync(short[] pcm)
    {
        var bytes = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);

        var payload = JsonSerializer.Serialize(new
        {
            message_type = "input_audio_chunk",
            audio_base_64 = Convert.ToBase64String(bytes),
            sample_rate = SampleRate,
        });

        var frame = Encoding.UTF8.GetBytes(payload);
        await _ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
    }

    /// <summary>Reads transcript messages; raises OnRecognized on each committed (final) transcript.</summary>
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(buffer).ConfigureAwait(false);
                if (message == null)
                {
                    break; // socket closed.
                }

                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (!root.TryGetProperty("message_type", out var typeEl))
                {
                    continue;
                }

                var type = typeEl.GetString();
                if (type == "committed_transcript" || type == "committed_transcript_with_timestamps")
                {
                    var text = root.TryGetProperty("text", out var t) ? t.GetString()?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        logger.LogInformation("Recognised: \"{Text}\"", text);
                        OnRecognized?.Invoke(text);
                    }
                }
                else if (type is "error" or "auth_error" or "quota_exceeded")
                {
                    logger.LogError("ElevenLabs STT error: {Message}", message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception excp)
        {
            logger.LogError(excp, "ElevenLabs STT receiver failed.");
        }
    }

    /// <summary>Reads one (possibly multi-frame) text message off the socket; null if it closed.</summary>
    private async Task<string> ReceiveMessageAsync(byte[] buffer)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try { _cts.Cancel(); } catch { /* best effort */ }
        _audioQueue.Writer.TryComplete();

        try { Task.WhenAll(_sender ?? Task.CompletedTask, _receiver ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(2)); }
        catch { /* best effort */ }

        try
        {
            if (_ws is { State: WebSocketState.Open })
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch { /* best effort */ }

        _ws?.Dispose();
        _cts.Dispose();
    }
}
