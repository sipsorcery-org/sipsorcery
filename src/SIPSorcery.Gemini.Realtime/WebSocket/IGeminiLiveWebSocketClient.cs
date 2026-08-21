using System;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Contract for the transport used to talk to the Gemini Live BidiGenerateContent WebSocket
/// endpoint. Unlike OpenAI's Realtime API (SDP offer/answer over REST, then media/control over
/// WebRTC), Gemini Live uses a single persistent WebSocket for setup, audio and control messages.
/// </summary>
public interface IGeminiLiveWebSocketClient
{
    /// <summary>
    /// True while the underlying WebSocket connection is open.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Raised for each complete message received from Gemini (raw JSON; Gemini sends it in binary
    /// frames, this is the UTF-8 decoded payload).
    /// </summary>
    event Action<string>? OnMessage;

    /// <summary>
    /// Raised once the connection has closed, whether cleanly or due to an error.
    /// </summary>
    event Action? OnClosed;

    /// <summary>
    /// Raised if the receive loop terminates because of an unexpected exception.
    /// </summary>
    event Action<Exception>? OnError;

    /// <summary>
    /// Opens the WebSocket connection to the BidiGenerateContent endpoint. The model to use is
    /// selected separately, via the "model" field of the <see cref="Models.GeminiSetup"/> message
    /// sent immediately after connecting.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a single JSON text message to Gemini.
    /// </summary>
    Task SendAsync(string json, CancellationToken ct = default);

    /// <summary>
    /// Closes the WebSocket connection gracefully.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}
