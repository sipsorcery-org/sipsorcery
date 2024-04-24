namespace DataChannelBandwidth;

using System.Buffers;
using Microsoft.Extensions.Logging;

using SIPSorcery.Net;

#pragma warning disable IDE0011 // Add braces
class DataChannelStream : Stream
{
    readonly RTCDataChannel channel;
    int currentMessageOffset;
    byte[] message = [];
    readonly CancellationTokenSource closed = new();
    readonly SemaphoreSlim messageNeeded = new(0, 1);
    readonly SemaphoreSlim messageAvailable = new(0, maxCount: 1);
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly SemaphoreSlim readLock = new(1, 1);
    readonly ILogger? log;
    readonly object sync = new();

    readonly IRTCPeerConnection? ownedConnection;
    public IRTCPeerConnection? OwnedConnection
    {
        get => ownedConnection;
        init
        {
            if (ownedConnection is not null)
                ownedConnection.onconnectionstatechange -= ConnectionStateChanged;
            ownedConnection = value;
            if (value is not null)
                value.onconnectionstatechange += ConnectionStateChanged;
        }
    }

    public int MaxSendBytes { get; init; } = 260_000;
    public long TotalSent => totalSent;
    public long TotalReceived => totalRead;

    public DataChannelStream(RTCDataChannel channel, ILogger? log = null)
    {
        this.log = log;
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.channel.onmessage += OnMessage;
        this.channel.onerror += OnChannelError;
        this.channel.onclose += OnChannelClosed;
    }

    void OnChannelClosed()
    {
        log?.LogInformation("channel closed");
        Unsubscribe();
        closed.Cancel();
    }

    void OnChannelError(string error)
    {
        log?.LogError("channel error: {Error}", error);
        Unsubscribe();
        closed.Cancel();
    }

    void ConnectionStateChanged(RTCPeerConnectionState state)
    {
        log?.LogInformation("connection state changed to {State}", state);
        switch (state)
        {
            case RTCPeerConnectionState.closed
            or RTCPeerConnectionState.disconnected
            or RTCPeerConnectionState.failed:
                Unsubscribe();
                closed.Cancel();
                break;
        }
    }

    int messages;
    void OnMessage(RTCDataChannel _, DataChannelPayloadProtocols protocol, byte[] data)
    {
        int seq = Interlocked.Increment(ref messages);
        log?.LogDebug("{Seq} received", seq);
        try
        {
            messageNeeded.Wait();
            lock (sync)
            {
                message = data;
                currentMessageOffset = 0;
            }
            messageAvailable.Release();
        }
        catch (ObjectDisposedException) { }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (checked(offset + count) > buffer.Length)
            throw new ArgumentException(
                "The sum of offset and count is larger than the buffer length.");

        return Read(buffer.AsSpan().Slice(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        readLock.Wait(closed.Token);
        try
        {
            if (buffer.Length == 0) return 0;

            bool wait;
            do
            {
                lock (sync)
                {
                    wait = MessageNeeded();
                    if (wait)
                        messageNeeded.Release();
                }

                if (wait)
                {
                    try
                    {
                        messageAvailable.Wait(closed.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return 0;
                    }
                }
            } while (wait);

            lock (sync)
            {
                int remaining = message.Length - currentMessageOffset;
                int toCopy = Math.Min(remaining, buffer.Length);
                message.AsSpan(currentMessageOffset, toCopy).CopyTo(buffer);
                currentMessageOffset += toCopy;
                Interlocked.Add(ref totalRead, toCopy);
                return toCopy;
            }
        }
        finally
        {
            readLock.Release();
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
                                                   CancellationToken cancellationToken = default)
    {
        await readLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (buffer.Length == 0) return 0;

            using var cancel =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, closed.Token);

            bool wait;
            do
            {
                lock (sync)
                {
                    wait = MessageNeeded();

                    if (wait)
                        messageNeeded.Release();
                }

                if (wait)
                {
                    try
                    {
                        await messageAvailable.WaitAsync(cancel.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken
                                                                   .IsCancellationRequested)
                    {
                        return 0;
                    }
                }
            } while (wait);

            lock (sync)
            {
                int remaining = message.Length - currentMessageOffset;
                int toCopy = Math.Min(remaining, buffer.Length);
                message.AsSpan(currentMessageOffset, toCopy).CopyTo(buffer.Span);
                currentMessageOffset += toCopy;
                Interlocked.Add(ref totalRead, toCopy);
                return toCopy;
            }
        }
        finally
        {
            readLock.Release();
        }
    }
    long totalRead;

    bool MessageNeeded() => message.Length - currentMessageOffset == 0;

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (checked(offset + count) > buffer.Length)
            throw new ArgumentException(
                "The sum of offset and count is larger than the buffer length.");

        Write(buffer.AsSpan().Slice(offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
                                               CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (buffer.Length > MaxSendBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packet = buffer[..MaxSendBytes];
                await Task.Run(() => Send(packet.Span.ToArray()), cancellationToken)
                          .ConfigureAwait(false);
                buffer = buffer[MaxSendBytes..];
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > 0)
                Send(buffer.Span.ToArray());
        }
        finally
        {
            writeLock.Release();
        }
    }

    long totalSent;
    void Send(byte[] buffer)
    {
        channel.send(buffer);
        Interlocked.Add(ref totalSent, buffer.Length);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        writeLock.Wait(closed.Token);
        try
        {
            while (buffer.Length > MaxSendBytes)
            {
                var packet = buffer[..MaxSendBytes];
                Send(packet.ToArray());
                buffer = buffer[MaxSendBytes..];
            }
            if (buffer.Length > 0)
                Send(buffer.ToArray());
        }
        finally
        {
            writeLock.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        Unsubscribe();
        channel.close();
        (OwnedConnection as IDisposable)?.Dispose();
        messageNeeded.Dispose();
        messageAvailable.Dispose();
    }

    void Unsubscribe()
    {
        channel.onmessage -= OnMessage;
        channel.onclose -= OnChannelClosed;
        if (ownedConnection is { } connection)
            connection.onconnectionstatechange -= ConnectionStateChanged;
    }

    public override void Flush() { }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}