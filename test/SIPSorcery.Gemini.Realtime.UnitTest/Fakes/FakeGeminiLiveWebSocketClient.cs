using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

public class FakeGeminiLiveWebSocketClient : IGeminiLiveWebSocketClient, IDisposable
{
    private readonly List<string> _sent = new();

    public bool IsConnected { get; set; }

    public int ConnectCount { get; private set; }
    public int CloseCount { get; private set; }
    public int DisposeCount { get; private set; }

    /// <summary>Thrown by <see cref="ConnectAsync"/> when set.</summary>
    public Exception? ConnectException { get; set; }

    /// <summary>Thrown by <see cref="SendAsync"/> when set.</summary>
    public Exception? SendException { get; set; }

    /// <summary>Thrown by <see cref="CloseAsync"/> when set.</summary>
    public Exception? CloseException { get; set; }

    /// <summary>Awaited inside <see cref="ConnectAsync"/> when set, to hold a connect open.</summary>
    public TaskCompletionSource<bool>? ConnectGate { get; set; }

    /// <summary>Awaited inside <see cref="SendAsync"/> when set, to hold sends open.</summary>
    public TaskCompletionSource<bool>? SendGate { get; set; }

    /// <summary>Signalled the first time <see cref="SendAsync"/> is entered.</summary>
    public TaskCompletionSource<bool> FirstSendEntered { get; } = new();

    /// <summary>
    /// Upper bound on the random delay added inside <see cref="SendAsync"/>. Models the variable
    /// write latency of a real socket, which is what turns an ordering bug into visible reordering.
    /// </summary>
    public int MaxSendJitterMs { get; set; }

    private readonly Random _jitter = new(20260818);

    public event Action<string>? OnMessage;
    public event Action? OnClosed;
    public event Action<Exception>? OnError;

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

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ConnectCount++;

        if (ConnectGate != null)
        {
            await ConnectGate.Task.ConfigureAwait(false);
        }

        if (ConnectException != null)
        {
            throw ConnectException;
        }

        IsConnected = true;
    }

    public async Task SendAsync(string json, CancellationToken ct = default)
    {
        FirstSendEntered.TrySetResult(true);

        if (SendGate != null)
        {
            await SendGate.Task.ConfigureAwait(false);
        }

        if (MaxSendJitterMs > 0)
        {
            int delay;
            lock (_jitter)
            {
                delay = _jitter.Next(MaxSendJitterMs + 1);
            }

            await Task.Delay(delay).ConfigureAwait(false);
        }

        if (SendException != null)
        {
            throw SendException;
        }

        lock (_sent)
        {
            _sent.Add(json);
        }
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        CloseCount++;
        IsConnected = false;

        if (CloseException != null)
        {
            throw CloseException;
        }

        return Task.CompletedTask;
    }

    public void Dispose() => DisposeCount++;

    /// <summary>Simulates a message arriving from Gemini.</summary>
    public void RaiseMessage(string json) => OnMessage?.Invoke(json);

    /// <summary>Simulates the transport reporting that the connection closed.</summary>
    public void RaiseClosed()
    {
        IsConnected = false;
        OnClosed?.Invoke();
    }

    /// <summary>Simulates the transport reporting an unexpected failure.</summary>
    public void RaiseError(Exception ex) => OnError?.Invoke(ex);
}
