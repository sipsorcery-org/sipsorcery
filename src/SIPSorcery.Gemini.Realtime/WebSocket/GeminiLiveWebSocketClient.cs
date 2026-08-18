using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// WebSocket transport for the Gemini Live BidiGenerateContent API
/// (https://ai.google.dev/gemini-api/docs/live-api). Establishes and maintains the single
/// persistent WebSocket connection used for the whole session and runs the background receive loop
/// that reassembles and hands off complete JSON messages.
/// </summary>
public class GeminiLiveWebSocketClient : IGeminiLiveWebSocketClient, IDisposable
{
    /// <summary>
    /// The Gemini Live BidiGenerateContent WebSocket endpoint. Authentication is via the "key"
    /// query parameter, same as the rest of the Generative Language API. This accepts both a
    /// plain Gemini API key (format "AIzaSy...") and a short-lived ephemeral token minted via the
    /// Live API's auth_tokens endpoint (format "AQ....") — ephemeral tokens are a drop-in
    /// replacement for the API key, not a separate auth mechanism.
    ///
    /// NOTE: an earlier revision of this client used an "access_token" query parameter instead,
    /// based on a documentation excerpt that turned out to be misleading — Google's API gateway
    /// rejects that with a PolicyViolation close ("doesn't allow unregistered callers ... Please
    /// use API Key"), which is how this was caught.
    /// </summary>
    public const string GEMINI_LIVE_WEBSOCKET_BASE_URL =
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";

    private const int RECEIVE_BUFFER_SIZE = 16 * 1024;

    /// <summary>
    /// Interval for the WebSocket keep-alive ping. A Gemini Live session is mostly idle in one
    /// direction while the other side talks, which is long enough for an intermediate NAT or load
    /// balancer to drop an idle connection without either end noticing.
    /// </summary>
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    private readonly string _apiKey;
    private readonly ILogger _logger;

    /// <summary>
    /// Serialises connect attempts so a second overlapping <see cref="ConnectAsync"/> cannot
    /// replace the socket and cancellation source of an in-flight one, which would strand the
    /// original socket and leave its receive loop running against a connection nobody owns.
    /// </summary>
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private WebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private volatile bool _disposed;

    /// <summary>
    /// ClientWebSocket allows only one outstanding SendAsync call at a time — a second call while
    /// one is already in flight throws InvalidOperationException (and can leave the socket in a
    /// broken state). Realtime audio chunks arrive on their own timer (e.g. every ~100ms from a
    /// microphone capture callback) independently of how long the previous chunk's send took, so
    /// without serialising sends here, a slow network round-trip on one chunk causes the next
    /// chunk's send to fail — heard as dropped audio, and eventually a wedged connection once the
    /// socket faults.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public event Action<string>? OnMessage;
    public event Action? OnClosed;
    public event Action<Exception>? OnError;

    /// <param name="apiKey">The Google AI Studio / Gemini API key, or an ephemeral Live API token.</param>
    /// <param name="logger">Logging instance for this class. A null logger is used if not supplied.</param>
    public GeminiLiveWebSocketClient(string apiKey, ILogger? logger = null)
    {
        _apiKey = apiKey;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var uri = new Uri($"{GEMINI_LIVE_WEBSOCKET_BASE_URL}?key={Uri.EscapeDataString(_apiKey)}");

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            if (_webSocket is { State: WebSocketState.Open or WebSocketState.Connecting })
            {
                throw new InvalidOperationException("The Gemini Live WebSocket is already connected.");
            }

            // Tear down the remains of any previous, now closed, connection before replacing it so
            // that reconnecting on the same instance doesn't leak a socket or a receive loop.
            TearDownConnection();

            _logger.LogDebug("Connecting to Gemini Live WebSocket endpoint.");

            var webSocket = await ConnectWebSocketAsync(uri, ct).ConfigureAwait(false);

            var receiveCts = new CancellationTokenSource();
            _webSocket = webSocket;
            _receiveCts = receiveCts;

            // Intentionally not awaited: runs for the lifetime of the connection.
            _ = Task.Run(() => ReceiveLoop(webSocket, receiveCts.Token));
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Creates and connects the underlying socket. Overridable so tests (and callers needing
    /// proxy/header/certificate customisation) can substitute their own <see cref="WebSocket"/>
    /// without going near a real network.
    /// </summary>
    protected virtual async Task<WebSocket> ConnectWebSocketAsync(Uri uri, CancellationToken ct)
    {
        var webSocket = new ClientWebSocket();
        webSocket.Options.KeepAliveInterval = KeepAliveInterval;

        try
        {
            await webSocket.ConnectAsync(uri, ct).ConfigureAwait(false);
        }
        catch
        {
            webSocket.Dispose();
            throw;
        }

        return webSocket;
    }

    private async Task ReceiveLoop(WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[RECEIVE_BUFFER_SIZE];

        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var messageStream = new MemoryStream();

                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Gemini typically closes immediately (before ever emitting a message)
                        // when authentication fails or the setup payload is rejected — the
                        // close status/description usually explains why.
                        _logger.LogWarning(
                            "Gemini Live WebSocket close frame received. CloseStatus={CloseStatus}, Description={CloseStatusDescription}",
                            webSocket.CloseStatus,
                            webSocket.CloseStatusDescription);
                        return;
                    }

                    // Everything else is a message payload. Gemini sends its JSON in BINARY frames,
                    // not text ones, so the frame type is deliberately not checked or reported here:
                    // both carry UTF-8 JSON and warning about binary frames would fire on every
                    // single message.
                    messageStream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);

                // A failure in message handling (a malformed payload, a consumer's event handler
                // throwing) concerns one message only. Keep the loop — and therefore the session —
                // running instead of tearing the connection down over it.
                EventRaiser.Raise(_logger, OnMessage, json, nameof(OnMessage));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when CloseAsync()/Dispose() cancels the receive loop.
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            // Also expected: tearing the connection down disposes the socket, so a receive that was
            // already in flight can surface as an ObjectDisposedException/WebSocketException rather
            // than a cancellation. Not a failure — don't report it as one.
            _logger.LogDebug(ex, "Gemini Live WebSocket receive loop ended during connection teardown.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini Live WebSocket receive loop terminated unexpectedly.");
            EventRaiser.Raise(_logger, OnError, ex, nameof(OnError));
        }
        finally
        {
            EventRaiser.Raise(_logger, OnClosed, nameof(OnClosed));
        }
    }

    public async Task SendAsync(string json, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Read the field once: a concurrent close/reconnect could otherwise swap the socket
        // between the state check and the send, turning this into a null dereference or a send on
        // an abandoned connection.
        var webSocket = _webSocket;

        if (webSocket is not { State: WebSocketState.Open })
        {
            throw new InvalidOperationException("Gemini Live WebSocket is not connected.");
        }

        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        _receiveCts?.Cancel();

        var webSocket = _webSocket;

        if (webSocket is { State: WebSocketState.Open })
        {
            try
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "normal", ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Exception closing Gemini Live WebSocket.");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        TearDownConnection();

        // _sendLock and _connectLock are intentionally left undisposed. A send or connect started
        // just before this point may still be inside its finally block, and disposing a
        // SemaphoreSlim out from under it turns an orderly shutdown into an ObjectDisposedException
        // on a background thread. Neither holds an unmanaged handle (no AvailableWaitHandle is
        // used), so there is nothing to release beyond managed memory.

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Cancels the receive loop and releases the current socket and its cancellation source.
    /// </summary>
    private void TearDownConnection()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        _webSocket?.Dispose();
        _webSocket = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(GeminiLiveWebSocketClient));
        }
    }
}
