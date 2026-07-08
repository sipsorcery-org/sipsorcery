//-----------------------------------------------------------------------------
// Filename: AudioSendPacer.cs
//
// Description: Utility class for pacing queued audio samples to a send
// delegate.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Net;

/// <summary>
/// Paces queued audio samples to a send delegate that accepts encoded audio
/// samples as <see cref="Memory{Byte}"/>.
/// </summary>
/// <remarks>
/// This class is intentionally independent of the rest of SIPSorcery. The
/// RTP clock rate is required to convert RTP timestamp units into wall clock
/// time for pacing.
/// </remarks>
public sealed class AudioSendPacer : IDisposable
{
    public const long NoQueuedAudioRemoved = -1;

    private readonly Action<uint, Memory<byte>> _sendAudio;
    private readonly int _rtpClockRate;
    private readonly object _lock = new();
    private readonly Queue<QueuedAudioSample> _queue = new();
    private readonly CancellationTokenSource _disposedCancellationTokenSource = new();
    private readonly Timer _sendTimer;

    private long _nextQueueIndex;
    private bool _disposed;
    private bool _sendInProgress;
    private bool _sendScheduled;

    public AudioSendPacer(Action<uint, Memory<byte>> sendAudio, int rtpClockRate)
    {
        if (sendAudio == null)
        {
            throw new ArgumentNullException(nameof(sendAudio));
        }

        if (rtpClockRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rtpClockRate), "The RTP clock rate must be greater than zero.");
        }

        _sendAudio = sendAudio;
        _rtpClockRate = rtpClockRate;
        DisposedCancellationToken = _disposedCancellationTokenSource.Token;
        _sendTimer = new(SendTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Gets the cancellation token used to cancel queued send tasks when this
    /// pacer is disposed.
    /// </summary>
    public CancellationToken DisposedCancellationToken { get; }

    /// <summary>
    /// Raised if the send delegate throws. The pacer will continue sending
    /// subsequent queued samples after reporting the exception.
    /// </summary>
    public event Action<Exception> OnSendError;

    /// <summary>
    /// Gets the number of samples currently waiting to be sent. This excludes
    /// a sample that is already being sent.
    /// </summary>
    public int QueuedCount
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Queues an audio sample to be sent on the pacer's media clock and
    /// completes when the sample has been passed to the send delegate.
    /// </summary>
    /// <param name="durationRtpUnits">The duration in RTP timestamp units represented by the sample.</param>
    /// <param name="sample">The encoded audio sample to send.</param>
    /// <param name="cancellationToken">Cancellation token that cancels the sample if it has not yet begun sending.</param>
    /// <returns>
    /// A task that completes with the monotonically increasing queue index
    /// assigned to the sample once it has been sent. The task is cancelled if
    /// the queued sample is removed by <see cref="ClearQueue"/> or cancelled
    /// before sending.
    /// </returns>
    public Task<long> SendAsync(uint durationRtpUnits, Memory<byte> sample, CancellationToken cancellationToken = default)
    {
        var queuedSample = new QueuedAudioSample(
            durationRtpUnits,
            sample,
            cancellationToken,
            new(TaskCreationOptions.RunContinuationsAsynchronously));

        lock (_lock)
        {
            ThrowIfDisposed();

            if (cancellationToken.IsCancellationRequested)
            {
                TrySetCanceled(queuedSample.SendTask, cancellationToken);
                return queuedSample.SendTask.Task;
            }

            queuedSample.QueueIndex = _nextQueueIndex++;
            _queue.Enqueue(queuedSample);

            if (cancellationToken.CanBeCanceled)
            {
                queuedSample.CancellationRegistration = cancellationToken.Register(static state =>
                    ((QueuedAudioSample)state).Cancel(), queuedSample);
            }

            if (!_sendInProgress && !_sendScheduled)
            {
                _sendScheduled = true;
                _sendTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
        }

        return queuedSample.SendTask.Task;
    }

    /// <summary>
    /// Clears all queued audio samples that have not yet been sent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to associate with tasks cancelled by this clear operation.</param>
    /// <returns>
    /// The lowest queue index removed, or <see cref="NoQueuedAudioRemoved"/>
    /// if no queued samples were removed.
    /// </returns>
    public long ClearQueue(CancellationToken cancellationToken = default)
    {
        List<QueuedAudioSample> removedSamples;
        long lowestRemovedQueueIndex;

        lock (_lock)
        {
            ThrowIfDisposed();

            if (_queue.Count == 0)
            {
                return NoQueuedAudioRemoved;
            }

            lowestRemovedQueueIndex = _queue.Peek().QueueIndex;
            removedSamples = new(_queue);
            _queue.Clear();

            if (!_sendInProgress && _sendScheduled)
            {
                _sendScheduled = false;
                _sendTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        foreach (QueuedAudioSample removedSample in removedSamples)
        {
            removedSample.DisposeCancellationRegistration();
            TrySetCanceled(removedSample.SendTask, cancellationToken);
        }

        return lowestRemovedQueueIndex;
    }

    public void Dispose()
    {
        List<QueuedAudioSample> removedSamples = null;

        lock (_lock)
        {
            if (!_disposed)
            {
                _disposed = true;
                removedSamples = new(_queue);
                _queue.Clear();
                _sendScheduled = false;
                _sendTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        if (removedSamples != null)
        {
            CancelDisposedCancellationToken();
        }

        foreach (QueuedAudioSample removedSample in removedSamples ?? [])
        {
            removedSample.DisposeCancellationRegistration();
            TrySetCanceled(removedSample.SendTask, DisposedCancellationToken);
        }

        _sendTimer.Dispose();
    }

    private void SendTimerCallback(object state)
    {
        while (true)
        {
            QueuedAudioSample queuedSample;

            lock (_lock)
            {
                if (_disposed || _queue.Count == 0)
                {
                    _sendInProgress = false;
                    _sendScheduled = false;
                    return;
                }

                queuedSample = _queue.Dequeue();
                _sendInProgress = true;
                _sendScheduled = false;
            }

            queuedSample.DisposeCancellationRegistration();

            if (queuedSample.IsCanceled)
            {
                continue;
            }

            long sendStartedAt = Stopwatch.GetTimestamp();

            try
            {
                _sendAudio(queuedSample.DurationRtpUnits, queuedSample.Sample);
                queuedSample.SendTask.TrySetResult(queuedSample.QueueIndex);
            }
            catch (Exception excp)
            {
                queuedSample.SendTask.TrySetException(excp);
                ReportSendError(excp);
            }

            TimeSpan nextDelay = GetRemainingDelay(queuedSample.DurationRtpUnits, sendStartedAt);

            lock (_lock)
            {
                _sendInProgress = false;

                if (!_disposed && _queue.Count > 0)
                {
                    _sendScheduled = true;
                    _sendTimer.Change(nextDelay, Timeout.InfiniteTimeSpan);
                }
            }

            return;
        }
    }

    private void ReportSendError(Exception excp)
    {
        try
        {
            OnSendError?.Invoke(excp);
        }
        catch
        {
            // Do not allow an error handler exception to stop the pacer.
        }
    }

    private void CancelDisposedCancellationToken()
    {
        try
        {
            _disposedCancellationTokenSource.Cancel();
        }
        catch
        {
            // Do not allow external callbacks on the exposed token to break dispose.
        }
    }

    private static void TrySetCanceled(TaskCompletionSource<long> sendTask, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            sendTask.TrySetCanceled(cancellationToken);
        }
        else
        {
            sendTask.TrySetCanceled();
        }
    }

    private TimeSpan GetRemainingDelay(uint durationRtpUnits, long sendStartedAt)
    {
        TimeSpan sampleDuration = RtpUnitsToTimeSpan(durationRtpUnits);
        long elapsedTicks = Stopwatch.GetTimestamp() - sendStartedAt;
        TimeSpan elapsed = TimeSpan.FromSeconds((double)elapsedTicks / Stopwatch.Frequency);

        return elapsed >= sampleDuration ? TimeSpan.Zero : sampleDuration - elapsed;
    }

    private TimeSpan RtpUnitsToTimeSpan(uint durationRtpUnits)
    {
        double ticks = (double)durationRtpUnits * TimeSpan.TicksPerSecond / _rtpClockRate;

        if (ticks >= TimeSpan.MaxValue.Ticks)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromTicks((long)ticks);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioSendPacer));
        }
    }

    private sealed class QueuedAudioSample(
        uint durationRtpUnits,
        Memory<byte> sample,
        CancellationToken cancellationToken,
        TaskCompletionSource<long> sendTask)
    {
        private int _cancellationRegistrationDisposed;

        public long QueueIndex { get; set; }

        public bool IsCanceled => SendTask.Task.IsCanceled;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public uint DurationRtpUnits { get; } = durationRtpUnits;

        public Memory<byte> Sample { get; } = sample;

        public TaskCompletionSource<long> SendTask { get; } = sendTask;

        public void Cancel() => TrySetCanceled(SendTask, CancellationToken);

        public void DisposeCancellationRegistration()
        {
            if (Interlocked.Exchange(ref _cancellationRegistrationDisposed, 1) == 0)
            {
                CancellationRegistration.Dispose();
                CancellationRegistration = default;
            }
        }
    }
}
