using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

[Trait("Category", "unit")]
public class GeminiLiveWebSocketClientUnitTests
{
    private readonly ILogger logger;

    public GeminiLiveWebSocketClientUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    private static List<string> SubscribeToMessages(IGeminiLiveWebSocketClient client)
    {
        var messages = new List<string>();
        client.OnMessage += json =>
        {
            lock (messages)
            {
                messages.Add(json);
            }
        };

        return messages;
    }

    [Fact]
    public void IsConnected_Is_False_Before_Connecting()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        using var client = new TestableGeminiLiveWebSocketClient();

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_Sends_The_Api_Key_As_The_Key_Query_Parameter()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        using var client = new TestableGeminiLiveWebSocketClient(null, new FakeWebSocket());

        await client.ConnectAsync();

        Assert.True(client.IsConnected);
        Assert.StartsWith(GeminiLiveWebSocketClient.GEMINI_LIVE_WEBSOCKET_BASE_URL, client.ConnectedUri!.ToString());
        Assert.Equal("?key=test-api-key", client.ConnectedUri!.Query);
    }

    [Fact]
    public async Task ConnectAsync_Failure_Leaves_The_Client_Disconnected()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        using var client = new TestableGeminiLiveWebSocketClient
        {
            ConnectException = new WebSocketException("handshake rejected")
        };

        await Assert.ThrowsAsync<WebSocketException>(() => client.ConnectAsync());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Receive_Loop_Delivers_A_Complete_Text_Message()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);
        var messages = SubscribeToMessages(client);

        await client.ConnectAsync();
        socket.QueueTextFrame(@"{ ""setupComplete"": {} }");

        Assert.True(await Wait.UntilAsync(() => messages.Count == 1));
        Assert.Equal(@"{ ""setupComplete"": {} }", messages[0]);
    }

    [Fact]
    public async Task Receive_Loop_Reassembles_A_Fragmented_Message()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);
        var messages = SubscribeToMessages(client);

        await client.ConnectAsync();

        socket.QueueTextFrame(@"{ ""serverContent"":", endOfMessage: false);
        socket.QueueTextFrame(@" { ""turnComplete"": true } }", endOfMessage: true);

        Assert.True(await Wait.UntilAsync(() => messages.Count == 1));
        Assert.Equal(@"{ ""serverContent"": { ""turnComplete"": true } }", messages[0]);
    }

    [Fact]
    public async Task Receive_Loop_Decodes_A_Binary_Frame_Without_Complaining()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        var log = new CapturingLogger();
        using var client = new TestableGeminiLiveWebSocketClient(log, socket);
        var messages = SubscribeToMessages(client);

        await client.ConnectAsync();
        socket.QueueBinaryFrame(System.Text.Encoding.UTF8.GetBytes(@"{ ""setupComplete"": {} }"));

        Assert.True(await Wait.UntilAsync(() => messages.Count == 1));
        Assert.Equal(@"{ ""setupComplete"": {} }", messages[0]);

        // Gemini sends its JSON in binary frames, so treating that as noteworthy logs a warning per
        // message and buries everything else.
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Receive_Loop_Survives_A_Throwing_Message_Handler()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        var log = new CapturingLogger();
        using var client = new TestableGeminiLiveWebSocketClient(log, socket);

        var delivered = 0;
        client.OnMessage += _ => throw new InvalidOperationException("handler bug");
        client.OnMessage += _ => Interlocked.Increment(ref delivered);

        var closedCount = 0;
        client.OnClosed += () => Interlocked.Increment(ref closedCount);

        await client.ConnectAsync();

        socket.QueueTextFrame(@"{ ""one"": {} }");
        socket.QueueTextFrame(@"{ ""two"": {} }");

        // The session must keep going: both messages arrive and the loop is still running.
        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref delivered) == 2));
        Assert.Equal(0, Volatile.Read(ref closedCount));
        Assert.True(log.Contains(LogLevel.Error, "event handler threw an exception"));
    }

    [Fact]
    public async Task Close_Frame_Raises_OnClosed_But_Not_OnError()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket
        {
            CloseStatusValue = WebSocketCloseStatus.PolicyViolation,
            CloseStatusDescriptionValue = "Request contains an invalid argument."
        };

        var log = new CapturingLogger();
        using var client = new TestableGeminiLiveWebSocketClient(log, socket);

        var closed = 0;
        var errors = 0;
        client.OnClosed += () => Interlocked.Increment(ref closed);
        client.OnError += _ => Interlocked.Increment(ref errors);

        await client.ConnectAsync();
        socket.QueueCloseFrame();

        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref closed) == 1));
        Assert.Equal(0, Volatile.Read(ref errors));

        // The close status is the only diagnostic Gemini gives for a rejected setup or bad key.
        Assert.True(log.Contains(LogLevel.Warning, "PolicyViolation"));
    }

    [Fact]
    public async Task An_Unexpected_Receive_Failure_Raises_OnError_And_OnClosed()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);

        Exception? reported = null;
        var closed = 0;
        client.OnError += ex => reported = ex;
        client.OnClosed += () => Interlocked.Increment(ref closed);

        await client.ConnectAsync();
        socket.QueueReceiveException(new IOException("connection reset"));

        Assert.True(await Wait.UntilAsync(() => reported != null && Volatile.Read(ref closed) == 1));
        Assert.IsType<IOException>(reported);
    }

    [Fact]
    public async Task CloseAsync_Closes_The_Socket_And_Ends_The_Receive_Loop()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);

        var closed = 0;
        client.OnClosed += () => Interlocked.Increment(ref closed);

        await client.ConnectAsync();
        await client.CloseAsync();

        Assert.Equal(1, socket.CloseAsyncCount);
        Assert.True(await Wait.UntilAsync(() => Volatile.Read(ref closed) == 1));
    }

    [Fact]
    public async Task CloseAsync_Swallows_A_Failure_From_The_Socket()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket { CloseException = new WebSocketException("already aborted") };
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);

        await client.ConnectAsync();

        // Teardown is best effort: a socket that has already gone away must not turn shutting the
        // session down into an exception the caller has to handle.
        await client.CloseAsync();

        Assert.Equal(1, socket.CloseAsyncCount);
    }

    [Fact]
    public async Task CloseAsync_On_A_Client_That_Never_Connected_Is_A_No_Op()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        using var client = new TestableGeminiLiveWebSocketClient();

        await client.CloseAsync();
    }

    [Fact]
    public async Task ConnectAsync_While_Already_Connected_Throws()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket, new FakeWebSocket());

        await client.ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync());

        // The live connection is untouched: no second socket was taken and nothing was disposed.
        Assert.Equal(1, client.ConnectAttempts);
        Assert.Equal(0, socket.DisposeCount);
    }

    [Fact]
    public async Task Reconnecting_After_A_Close_Releases_The_Previous_Socket_And_Loop()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var first = new FakeWebSocket();
        var second = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, first, second);

        await client.ConnectAsync();
        await client.CloseAsync();
        first.StateValue = WebSocketState.Closed;

        await client.ConnectAsync();

        Assert.Equal(2, client.ConnectAttempts);
        Assert.Equal(1, first.DisposeCount);

        // Exactly one receive loop is live: a message is delivered once, not twice.
        var messages = SubscribeToMessages(client);
        second.QueueTextFrame(@"{ ""setupComplete"": {} }");

        Assert.True(await Wait.UntilAsync(() => messages.Count == 1));
        await Task.Delay(100);
        Assert.Single(messages);
    }

    [Fact]
    public async Task SendAsync_Before_Connecting_Throws()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        using var client = new TestableGeminiLiveWebSocketClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("{}"));
    }

    [Fact]
    public async Task SendAsync_Writes_A_Single_Utf8_Text_Frame()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);

        await client.ConnectAsync();
        await client.SendAsync(@"{ ""setup"": { ""model"": ""models/zażółć"" } }");

        Assert.Equal(@"{ ""setup"": { ""model"": ""models/zażółć"" } }", Assert.Single(socket.Sent));
    }

    [Fact]
    public async Task Concurrent_Sends_Are_Serialised()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        using var client = new TestableGeminiLiveWebSocketClient(null, socket);

        await client.ConnectAsync();

        // ClientWebSocket throws if a second SendAsync starts while one is in flight, so the client
        // has to serialise them itself.
        await Task.WhenAll(Enumerable.Range(0, 25).Select(i => client.SendAsync($@"{{ ""i"": {i} }}")));

        Assert.Equal(25, socket.Sent.Count);
        Assert.Equal(1, socket.MaxConcurrentSends);
    }

    [Fact]
    public async Task Dispose_Releases_The_Socket_And_Blocks_Further_Use()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        var client = new TestableGeminiLiveWebSocketClient(null, socket);

        await client.ConnectAsync();

        client.Dispose();
        client.Dispose();

        Assert.Equal(1, socket.DisposeCount);
        Assert.False(client.IsConnected);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendAsync("{}"));
    }

    [Fact]
    public async Task Tearing_Down_A_Connection_Is_Not_Reported_As_A_Failure()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var socket = new FakeWebSocket();
        var client = new TestableGeminiLiveWebSocketClient(null, socket);

        var errors = 0;
        client.OnError += _ => Interlocked.Increment(ref errors);

        await client.ConnectAsync();

        // Disposing cancels the receive loop and disposes the socket underneath it; the resulting
        // exception is teardown, not a fault worth telling the consumer about.
        client.Dispose();

        await Task.Delay(200);
        Assert.Equal(0, Volatile.Read(ref errors));
    }
}
