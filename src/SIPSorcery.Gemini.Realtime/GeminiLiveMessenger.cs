using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Gemini.Realtime.Models;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Facilitates sending Gemini Live control/content messages (setup, text/audio turns, tool
/// responses) over the BidiGenerateContent WebSocket, and parses incoming server messages.
/// </summary>
public class GeminiLiveMessenger : IGeminiLiveMessenger
{
    public const string DEFAULT_AUDIO_MIME_TYPE = "audio/pcm;rate=16000";

    private readonly IGeminiLiveWebSocketClient _webSocketClient;
    private readonly ILogger _logger;

    public event Action<GeminiServerMessage>? OnServerMessage;

    /// <param name="webSocketClient">Transport client for the Gemini Live WebSocket connection.</param>
    /// <param name="logger">Logging instance for this class. A null logger is used if not supplied.</param>
    public GeminiLiveMessenger(
        IGeminiLiveWebSocketClient webSocketClient,
        ILogger? logger = null)
    {
        _webSocketClient = webSocketClient;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Sends the initial BidiGenerateContentSetup message. Must be the first message sent after
    /// the WebSocket connects.
    /// </summary>
    public Task<Either<Error, Unit>> SendSetupAsync(GeminiSetup setup, CancellationToken ct = default)
        => SendAsync(new GeminiClientSetupMessage { Setup = setup }, ct);

    /// <summary>
    /// Sends a text conversation turn.
    /// </summary>
    public Task<Either<Error, Unit>> SendClientContentAsync(
        string text,
        bool turnComplete = true,
        GeminiRoleEnum role = GeminiRoleEnum.user,
        CancellationToken ct = default)
    {
        var message = new GeminiClientContentMessage
        {
            ClientContent = new GeminiClientContent
            {
                Turns = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        Role = role,
                        Parts = new List<GeminiPart> { new GeminiPart { Text = text } }
                    }
                },
                TurnComplete = turnComplete
            }
        };

        return SendAsync(message, ct);
    }

    /// <summary>
    /// Streams a chunk of raw PCM16 little-endian audio (default 16 kHz mono, matching Gemini
    /// Live's required input format).
    /// </summary>
    public Task<Either<Error, Unit>> SendRealtimeInputAudioAsync(
        byte[] pcm16LittleEndian,
        string mimeType = DEFAULT_AUDIO_MIME_TYPE,
        CancellationToken ct = default)
    {
        var message = new GeminiRealtimeInputMessage
        {
            RealtimeInput = new GeminiRealtimeInput
            {
                Audio = new GeminiBlob
                {
                    MimeType = mimeType,
                    Data = Convert.ToBase64String(pcm16LittleEndian)
                }
            }
        };

        return SendAsync(message, ct);
    }

    /// <summary>
    /// Signals that no more audio will follow for the current input stream.
    /// </summary>
    public Task<Either<Error, Unit>> SendAudioStreamEndAsync(CancellationToken ct = default)
        => SendAsync(new GeminiRealtimeInputMessage { RealtimeInput = new GeminiRealtimeInput { AudioStreamEnd = true } }, ct);

    /// <summary>
    /// Marks the start of a user activity turn. Only meaningful when automatic activity detection
    /// has been disabled in the session's <see cref="GeminiSetup"/>.
    /// </summary>
    public Task<Either<Error, Unit>> SendActivityStartAsync(CancellationToken ct = default)
        => SendAsync(new GeminiRealtimeInputMessage { RealtimeInput = new GeminiRealtimeInput { ActivityStart = new object() } }, ct);

    /// <summary>
    /// Marks the end of a user activity turn. Only meaningful when automatic activity detection
    /// has been disabled in the session's <see cref="GeminiSetup"/>.
    /// </summary>
    public Task<Either<Error, Unit>> SendActivityEndAsync(CancellationToken ct = default)
        => SendAsync(new GeminiRealtimeInputMessage { RealtimeInput = new GeminiRealtimeInput { ActivityEnd = new object() } }, ct);

    /// <summary>
    /// Answers one or more pending <see cref="GeminiServerEventToolCall"/> function calls.
    /// </summary>
    public Task<Either<Error, Unit>> SendToolResponseAsync(IEnumerable<GeminiFunctionResponse> functionResponses, CancellationToken ct = default)
        => SendAsync(new GeminiToolResponseMessage { ToolResponse = new GeminiToolResponse { FunctionResponses = functionResponses.ToList() } }, ct);

    private async Task<Either<Error, Unit>> SendAsync(GeminiClientMessage message, CancellationToken ct)
    {
        if (!_webSocketClient.IsConnected)
        {
            return Error.New("Gemini Live WebSocket is not connected.");
        }

        var json = message.ToJson();

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Sending Gemini Live client message: {Json}", DescribeForLog(message, json));
        }

        try
        {
            await _webSocketClient.SendAsync(json, ct).ConfigureAwait(false);
            return Unit.Default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Gemini Live client message.");
            return Error.New($"Failed to send Gemini Live client message: {ex.Message}");
        }
    }

    /// <summary>
    /// Keeps the base64 payload of realtime audio chunks out of the logs. Audio is sent ~10 times
    /// a second and each chunk is several kilobytes of base64, so logging it verbatim buries every
    /// other message and bloats log storage for no diagnostic gain.
    /// </summary>
    private static string DescribeForLog(GeminiClientMessage message, string json)
    {
        if (message is GeminiRealtimeInputMessage { RealtimeInput.Audio: { } audio } && audio.Data != null)
        {
            return $"{{\"realtimeInput\":{{\"audio\":{{\"mimeType\":\"{audio.MimeType}\",\"data\":\"<{audio.Data.Length} base64 chars>\"}}}}}}";
        }

        return json;
    }

    /// <summary>
    /// Handles a raw text message received from the WebSocket. Parses the JSON into the
    /// appropriate <see cref="GeminiServerMessage"/> subtype and raises
    /// <see cref="OnServerMessage"/> for it.
    ///
    /// This method runs on the WebSocket receive loop, where an escaping exception would kill the
    /// loop and silently end the whole session while the socket is still open. It therefore never
    /// throws: a payload this library cannot make sense of (e.g. an enum value Google adds later)
    /// degrades to a <see cref="GeminiUnknownServerMessage"/> carrying the original JSON, and a
    /// consumer's event handler that throws is logged and suppressed.
    /// </summary>
    public void HandleIncomingMessage(string json)
    {
        GeminiServerMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<GeminiServerMessage>(json, JsonOptions.Default);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to deserialise Gemini Live server message; forwarding raw JSON via GeminiUnknownServerMessage. Payload: {Payload}",
                json);

            var fallback = new GeminiUnknownServerMessage
            {
                OriginalKey = TryExtractFirstKey(json),
                OriginalJson = json
            };
            RaiseOnServerMessage(fallback);
            return;
        }

        if (message == null)
        {
            _logger.LogWarning("Received empty/non-JSON message on Gemini Live WebSocket: {Payload}", json);
            return;
        }

        if (message is GeminiUnknownServerMessage unknown)
        {
            _logger.LogWarning(
                "Unrecognised Gemini Live server message key '{Key}'; forwarding as GeminiUnknownServerMessage with original JSON in OriginalJson.",
                unknown.OriginalKey);
        }

        RaiseOnServerMessage(message);
    }

    private void RaiseOnServerMessage(GeminiServerMessage message)
        => EventRaiser.Raise(_logger, OnServerMessage, message, nameof(OnServerMessage));

    private static string? TryExtractFirstKey(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                return property.Name;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
