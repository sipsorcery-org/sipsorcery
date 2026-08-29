using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using SIPSorcery.Gemini.Realtime.Models;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Sends Gemini Live BidiGenerateContent client messages over the WebSocket transport and parses
/// the messages coming back. Exposed by <see cref="IGeminiLiveEndPoint.Messenger"/> for the
/// lower-level operations the end point does not wrap directly (activity markers, audio stream
/// end, non-default audio MIME types).
/// </summary>
public interface IGeminiLiveMessenger
{
    /// <summary>
    /// Raised for each successfully parsed server message. Consumers should normally subscribe to
    /// <see cref="IGeminiLiveEndPoint.OnServerMessage"/> instead; the end point forwards this.
    /// </summary>
    event Action<GeminiServerMessage>? OnServerMessage;

    /// <summary>
    /// Sends the initial BidiGenerateContentSetup message. Must be the first message sent after
    /// the WebSocket connects.
    /// </summary>
    Task<Either<Error, Unit>> SendSetupAsync(GeminiSetup setup, CancellationToken ct = default);

    /// <summary>
    /// Sends a text conversation turn.
    /// </summary>
    Task<Either<Error, Unit>> SendClientContentAsync(
        string text,
        bool turnComplete = true,
        GeminiRoleEnum role = GeminiRoleEnum.user,
        CancellationToken ct = default);

    /// <summary>
    /// Streams a chunk of raw PCM16 little-endian audio.
    /// </summary>
    Task<Either<Error, Unit>> SendRealtimeInputAudioAsync(
        byte[] pcm16LittleEndian,
        string mimeType = GeminiLiveMessenger.DEFAULT_AUDIO_MIME_TYPE,
        CancellationToken ct = default);

    /// <summary>
    /// Signals that no more audio will follow for the current input stream.
    /// </summary>
    Task<Either<Error, Unit>> SendAudioStreamEndAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks the start of a user activity turn. Only meaningful when automatic activity detection
    /// has been disabled in the session's <see cref="GeminiSetup"/>.
    /// </summary>
    Task<Either<Error, Unit>> SendActivityStartAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks the end of a user activity turn. Only meaningful when automatic activity detection
    /// has been disabled in the session's <see cref="GeminiSetup"/>.
    /// </summary>
    Task<Either<Error, Unit>> SendActivityEndAsync(CancellationToken ct = default);

    /// <summary>
    /// Answers one or more pending <see cref="GeminiServerEventToolCall"/> function calls.
    /// </summary>
    Task<Either<Error, Unit>> SendToolResponseAsync(IEnumerable<GeminiFunctionResponse> functionResponses, CancellationToken ct = default);

    /// <summary>
    /// Parses a raw JSON text message received from the WebSocket and raises
    /// <see cref="OnServerMessage"/> for it. Never throws.
    /// </summary>
    void HandleIncomingMessage(string json);
}
