using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

public class TestableGeminiLiveWebSocketClient : GeminiLiveWebSocketClient
{
    private readonly Queue<FakeWebSocket> _sockets = new();

    public TestableGeminiLiveWebSocketClient(ILogger? logger = null, params FakeWebSocket[] sockets)
        : base("test-api-key", logger)
    {
        foreach (var socket in sockets)
        {
            _sockets.Enqueue(socket);
        }
    }

    /// <summary>The URI the client would have connected to, captured for assertion.</summary>
    public Uri? ConnectedUri { get; private set; }

    public int ConnectAttempts { get; private set; }

    /// <summary>Thrown instead of returning a socket when set.</summary>
    public Exception? ConnectException { get; set; }

    protected override Task<WebSocket> ConnectWebSocketAsync(Uri uri, CancellationToken ct)
    {
        ConnectAttempts++;
        ConnectedUri = uri;

        if (ConnectException != null)
        {
            throw ConnectException;
        }

        if (_sockets.Count == 0)
        {
            throw new InvalidOperationException("No FakeWebSocket left to hand out; queue one per expected connect.");
        }

        return Task.FromResult<WebSocket>(_sockets.Dequeue());
    }
}
