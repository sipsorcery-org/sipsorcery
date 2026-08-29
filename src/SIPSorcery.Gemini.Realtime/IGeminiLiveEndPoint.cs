using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using SIPSorcery.Gemini.Realtime.Models;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Contract for an end point that communicates with the Gemini Live BidiGenerateContent API.
/// Implementations own the WebSocket connection, translate raw PCM16 audio to/from Gemini's
/// realtime input/output messages, and surface strongly typed server messages.
///
/// Implements both dispose patterns because the end point owns a WebSocket: prefer
/// <see cref="IAsyncDisposable.DisposeAsync"/> so the close handshake and the outbound audio queue
/// can be drained, and use <see cref="IDisposable.Dispose"/> only where awaiting isn't possible.
/// </summary>
public interface IGeminiLiveEndPoint : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Helper used to send Gemini Live control/content messages over the WebSocket.
    /// </summary>
    IGeminiLiveMessenger Messenger { get; }

    /// <summary>
    /// Number of outbound audio chunks discarded because the send queue was full, i.e. audio was
    /// captured faster than it could be written to the socket.
    /// </summary>
    long DroppedAudioChunks { get; }

    /// <summary>
    /// Fired once the session's <see cref="GeminiServerEventSetupComplete"/> message arrives —
    /// the session is ready for audio/content exchange at this point.
    /// </summary>
    event Action? OnConnected;

    /// <summary>
    /// Fired when the WebSocket connection closes, whether cleanly or due to an error.
    /// </summary>
    event Action? OnClosed;

    /// <summary>
    /// Fired if the WebSocket connection fails unexpectedly.
    /// </summary>
    event Action? OnFailed;

    /// <summary>
    /// Fired when the model's turn is interrupted by user input (barge-in). Any audio already
    /// queued for local playback should be discarded.
    /// </summary>
    event Action? OnInterrupted;

    /// <summary>
    /// Raised for each chunk of raw PCM16 little-endian audio decoded from the model's output.
    /// The second parameter is the sample rate in Hz (24000 unless Gemini indicates otherwise).
    /// </summary>
    event Action<byte[], int>? OnAudioReceived;

    /// <summary>
    /// Raised for every parsed server message, regardless of type.
    /// </summary>
    event Action<GeminiServerMessage>? OnServerMessage;

    /// <summary>
    /// Opens the WebSocket connection and sends the initial session setup message.
    /// </summary>
    /// <param name="model">
    /// Optional model to request. When supplied it takes precedence over
    /// <see cref="GeminiSetup.Model"/>, which is applied to a copy so the caller's
    /// <paramref name="setupOverrides"/> instance is left untouched. Pass null to use the model
    /// already set on the setup.
    /// </param>
    /// <param name="setupOverrides">Optional fully populated setup to send instead of a default one.</param>
    /// <param name="ct">Cancellation token to allow the connect/setup attempt to be cancelled.</param>
    Task<Either<Error, Unit>> StartConnect(
        GeminiLiveModelsEnum? model = null,
        GeminiSetup? setupOverrides = null,
        CancellationToken ct = default);

    /// <summary>
    /// Queues a chunk of raw PCM16 little-endian audio (16 kHz mono) for sending to Gemini.
    /// Fire-and-forget: intended to be called from a synchronous microphone capture callback
    /// without blocking it. Chunks are written to the socket in the order they were queued, from
    /// any number of calling threads; if they arrive faster than they can be sent the newest are
    /// dropped and counted in <see cref="DroppedAudioChunks"/> rather than queued without limit.
    /// </summary>
    void SendAudio(byte[] pcm16LittleEndian);

    /// <summary>
    /// Sends a text conversation turn.
    /// </summary>
    Task<Either<Error, Unit>> SendText(string text, bool turnComplete = true);

    /// <summary>
    /// Answers pending function call(s) previously received via <see cref="OnServerMessage"/>.
    /// </summary>
    Task<Either<Error, Unit>> SendToolResponse(IEnumerable<GeminiFunctionResponse> functionResponses);

    /// <summary>
    /// Closes the WebSocket connection. The end point can be reconnected afterwards with
    /// <see cref="StartConnect"/>; use dispose to release it for good.
    /// </summary>
    Task Close();
}
