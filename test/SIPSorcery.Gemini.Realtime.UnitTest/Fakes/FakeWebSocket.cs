using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

public class FakeWebSocket : WebSocket
{
    private sealed record Frame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage, Exception? Throw);

    private readonly Channel<Frame> _frames = Channel.CreateUnbounded<Frame>();
    private readonly List<string> _sent = new();

    private int _concurrentSends;

    public override WebSocketCloseStatus? CloseStatus => CloseStatusValue;
    public override string? CloseStatusDescription => CloseStatusDescriptionValue;
    public override string? SubProtocol => null;
    public override WebSocketState State => StateValue;

    public WebSocketCloseStatus? CloseStatusValue { get; set; }
    public string? CloseStatusDescriptionValue { get; set; }
    public WebSocketState StateValue { get; set; } = WebSocketState.Open;

    public int DisposeCount { get; private set; }
    public int CloseAsyncCount { get; private set; }

    /// <summary>Highest number of <see cref="SendAsync"/> calls that were ever in flight together.</summary>
    public int MaxConcurrentSends { get; private set; }

    /// <summary>Awaited inside <see cref="SendAsync"/> when set, to hold sends open.</summary>
    public TaskCompletionSource<bool>? SendGate { get; set; }

    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }
    }

    public void QueueTextFrame(string text, bool endOfMessage = true)
        => _frames.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage, null));

    public void QueueBinaryFrame(byte[] payload, bool endOfMessage = true)
        => _frames.Writer.TryWrite(new Frame(payload, WebSocketMessageType.Binary, endOfMessage, null));

    public void QueueCloseFrame()
        => _frames.Writer.TryWrite(new Frame(Array.Empty<byte>(), WebSocketMessageType.Close, true, null));

    public void QueueReceiveException(Exception ex)
        => _frames.Writer.TryWrite(new Frame(Array.Empty<byte>(), WebSocketMessageType.Text, true, ex));

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var frame = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (frame.Throw != null)
        {
            throw frame.Throw;
        }

        if (frame.MessageType == WebSocketMessageType.Close)
        {
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        if (buffer.Array == null || frame.Payload.Length > buffer.Count)
        {
            throw new InvalidOperationException("FakeWebSocket frames must fit in the receive buffer.");
        }

        Buffer.BlockCopy(frame.Payload, 0, buffer.Array, buffer.Offset, frame.Payload.Length);

        return new WebSocketReceiveResult(frame.Payload.Length, frame.MessageType, frame.EndOfMessage);
    }

    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        var inFlight = Interlocked.Increment(ref _concurrentSends);
        if (inFlight > MaxConcurrentSends)
        {
            MaxConcurrentSends = inFlight;
        }

        try
        {
            if (SendGate != null)
            {
                await SendGate.Task.ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }

            lock (_sent)
            {
                _sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            }
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentSends);
        }
    }

    /// <summary>Thrown by <see cref="CloseAsync"/> when set.</summary>
    public Exception? CloseException { get; set; }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        CloseAsyncCount++;

        if (CloseException != null)
        {
            throw CloseException;
        }

        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override void Abort() => _frames.Writer.TryComplete();

    public override void Dispose()
    {
        DisposeCount++;
        _frames.Writer.TryComplete();
    }
}
