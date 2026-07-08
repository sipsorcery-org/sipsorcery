//-----------------------------------------------------------------------------
// Filename: AudioSendPacerUnitTest.cs
//
// Description: Unit tests for the AudioSendPacer class.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

[Trait("Category", "unit")]
public class AudioSendPacerUnitTest
{
    [Fact]
    public async Task SendAsyncAssignsIndexesAndPacesSamples()
    {
        var sentSamples = new List<SentAudioSample>();
        using var sentEvent = new ManualResetEventSlim(false);
        using var pacer = new AudioSendPacer((duration, sample) =>
        {
            lock (sentSamples)
            {
                sentSamples.Add(new(duration, sample.ToArray(), Stopwatch.GetTimestamp()));

                if (sentSamples.Count == 2)
                {
                    sentEvent.Set();
                }
            }
        }, 1000);

        long firstSampleQueuedAt = Stopwatch.GetTimestamp();
        Task<long> firstSendTask = pacer.SendAsync(50, new byte[] { 0x01 });
        Task<long> secondSendTask = pacer.SendAsync(50, new byte[] { 0x02 });

        Assert.True(sentEvent.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, await firstSendTask.ConfigureAwait(false));
        Assert.Equal(1, await secondSendTask.ConfigureAwait(false));

        lock (sentSamples)
        {
            Assert.Equal(2, sentSamples.Count);
            Assert.Equal(new byte[] { 0x01 }, sentSamples[0].Sample);
            Assert.Equal(new byte[] { 0x02 }, sentSamples[1].Sample);

            TimeSpan sendGap = TimeSpan.FromSeconds(
                (double)(sentSamples[1].SentAt - sentSamples[0].SentAt) / Stopwatch.Frequency);
            TimeSpan firstSendDelay = TimeSpan.FromSeconds(
                (double)(sentSamples[0].SentAt - firstSampleQueuedAt) / Stopwatch.Frequency);

            Assert.True(firstSendDelay < TimeSpan.FromMilliseconds(20),
                $"Expected first sample after idle to be sent immediately, actual delay was {firstSendDelay.TotalMilliseconds}ms.");
            Assert.True(sendGap >= TimeSpan.FromMilliseconds(35),
                $"Expected samples to be paced, actual gap was {sendGap.TotalMilliseconds}ms.");
        }
    }

    [Fact]
    public async Task ClearQueueCancelsUnsentSamples()
    {
        var sentSamples = new List<SentAudioSample>();
        using var firstSentEvent = new ManualResetEventSlim(false);
        using var pacer = new AudioSendPacer((duration, sample) =>
        {
            lock (sentSamples)
            {
                sentSamples.Add(new(duration, sample.ToArray(), Stopwatch.GetTimestamp()));
                firstSentEvent.Set();
            }
        }, 1000);

        Task<long> firstSendTask = pacer.SendAsync(200, new byte[] { 0x01 });
        Task<long> secondSendTask = pacer.SendAsync(200, new byte[] { 0x02 });
        Task<long> thirdSendTask = pacer.SendAsync(200, new byte[] { 0x03 });

        Assert.True(firstSentEvent.Wait(TimeSpan.FromSeconds(2)));

        long lowestRemovedIndex = pacer.ClearQueue();

        Assert.Equal(0, await firstSendTask.ConfigureAwait(false));
        Assert.Equal(1, lowestRemovedIndex);
        Assert.Equal(AudioSendPacer.NoQueuedAudioRemoved, pacer.ClearQueue());

        await Assert.ThrowsAsync<TaskCanceledException>(() => secondSendTask).ConfigureAwait(false);
        await Assert.ThrowsAsync<TaskCanceledException>(() => thirdSendTask).ConfigureAwait(false);

        Thread.Sleep(300);

        lock (sentSamples)
        {
            Assert.Single(sentSamples);
            Assert.Equal(new byte[] { 0x01 }, sentSamples[0].Sample);
        }
    }

    [Fact]
    public async Task SendAsyncDoesNotCopyQueuedMemory()
    {
        byte[] mutableSample = { 0x01, 0x02 };
        byte[] senderArray = null;

        using var pacer = new AudioSendPacer((uint _, Memory<byte> sample) =>
        {
            Assert.True(MemoryMarshal.TryGetArray(sample, out ArraySegment<byte> segment));
            senderArray = segment.Array;
        }, 1000);

        long queueIndex = await pacer.SendAsync(20, mutableSample).ConfigureAwait(false);

        Assert.Equal(0, queueIndex);
        Assert.Same(mutableSample, senderArray);

        mutableSample[0] = 0xff;
    }

    [Fact]
    public async Task SendAsyncCancellationRemovesQueuedSample()
    {
        var sentSamples = new List<SentAudioSample>();
        using var firstSentEvent = new ManualResetEventSlim(false);
        using var cancellationTokenSource = new CancellationTokenSource();
        using var pacer = new AudioSendPacer((duration, sample) =>
        {
            lock (sentSamples)
            {
                sentSamples.Add(new(duration, sample.ToArray(), Stopwatch.GetTimestamp()));
                firstSentEvent.Set();
            }
        }, 1000);

        Task<long> firstSendTask = pacer.SendAsync(200, new byte[] { 0x01 });
        Task<long> secondSendTask = pacer.SendAsync(200, new byte[] { 0x02 }, cancellationTokenSource.Token);

        Assert.True(firstSentEvent.Wait(TimeSpan.FromSeconds(2)));

        cancellationTokenSource.Cancel();

        TaskCanceledException excp = await Assert.ThrowsAsync<TaskCanceledException>(() => secondSendTask).ConfigureAwait(false);

        Assert.Equal(cancellationTokenSource.Token, excp.CancellationToken);
        Assert.Equal(0, await firstSendTask.ConfigureAwait(false));

        Thread.Sleep(300);

        lock (sentSamples)
        {
            Assert.Single(sentSamples);
            Assert.Equal(new byte[] { 0x01 }, sentSamples[0].Sample);
        }
    }

    [Fact]
    public async Task ClearQueueUsesSuppliedCancellationToken()
    {
        using var firstSentEvent = new ManualResetEventSlim(false);
        using var cancellationTokenSource = new CancellationTokenSource();
        using var pacer = new AudioSendPacer((_, _) => firstSentEvent.Set(), 1000);

        Task<long> firstSendTask = pacer.SendAsync(200, new byte[] { 0x01 });
        Task<long> secondSendTask = pacer.SendAsync(200, new byte[] { 0x02 });

        Assert.True(firstSentEvent.Wait(TimeSpan.FromSeconds(2)));

        long lowestRemovedIndex = pacer.ClearQueue(cancellationTokenSource.Token);
        TaskCanceledException excp = await Assert.ThrowsAsync<TaskCanceledException>(() => secondSendTask).ConfigureAwait(false);

        Assert.Equal(0, await firstSendTask.ConfigureAwait(false));
        Assert.Equal(1, lowestRemovedIndex);
        Assert.Equal(cancellationTokenSource.Token, excp.CancellationToken);
    }

    [Fact]
    public async Task DisposeCancelsQueuedSamplesWithDisposedCancellationToken()
    {
        using var firstSentEvent = new ManualResetEventSlim(false);
        var pacer = new AudioSendPacer((_, _) => firstSentEvent.Set(), 1000);
        CancellationToken disposedCancellationToken = pacer.DisposedCancellationToken;

        Task<long> firstSendTask = pacer.SendAsync(200, new byte[] { 0x01 });
        Task<long> secondSendTask = pacer.SendAsync(200, new byte[] { 0x02 });

        Assert.True(firstSentEvent.Wait(TimeSpan.FromSeconds(2)));

        pacer.Dispose();

        TaskCanceledException excp = await Assert.ThrowsAsync<TaskCanceledException>(() => secondSendTask).ConfigureAwait(false);

        Assert.Equal(0, await firstSendTask.ConfigureAwait(false));
        Assert.True(disposedCancellationToken.IsCancellationRequested);
        Assert.Equal(disposedCancellationToken, excp.CancellationToken);
    }

    private readonly struct SentAudioSample(uint durationRtpUnits, byte[] sample, long sentAt)
    {
        public uint DurationRtpUnits { get; } = durationRtpUnits;

        public byte[] Sample { get; } = sample;

        public long SentAt { get; } = sentAt;
    }
}
