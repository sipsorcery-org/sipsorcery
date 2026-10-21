using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class UlpfecReceiveBufferUnitTest
    {
        private sealed class Scheduled : IDisposable
        {
            internal long Due;
            internal Action Callback;
            internal bool Cancelled;
            internal bool Fired;
            public void Dispose() => Cancelled = true;
        }

        private sealed class Clock
        {
            internal long Now;
            internal readonly List<Scheduled> Calls = new List<Scheduled>();
            internal IDisposable Schedule(Action callback, TimeSpan delay)
            {
                var call = new Scheduled { Due = Now + (long)Math.Ceiling(delay.TotalMilliseconds), Callback = callback };
                Calls.Add(call);
                return call;
            }
            internal void Advance(long milliseconds)
            {
                Now += milliseconds;
                while (true)
                {
                    var call = Calls.FirstOrDefault(item => !item.Cancelled && !item.Fired && item.Due <= Now);
                    if (call == null) { return; }
                    call.Fired = true;
                    call.Callback();
                }
            }
        }

        private static RTPPacket Packet(ushort sequence, uint timestamp, bool start = false, bool marker = false)
        {
            var packet = new RTPPacket(new ArraySegment<byte>(new byte[] { start ? (byte)0x10 : (byte)0x00, (byte)sequence }), 0);
            packet.Header.SyncSource = 0x1234;
            packet.Header.SequenceNumber = sequence;
            packet.Header.Timestamp = timestamp;
            packet.Header.PayloadType = 96;
            packet.Header.MarkerBit = marker ? 1 : 0;
            return packet;
        }

        private static bool IsStart(RTPPacket packet) => (packet.GetPayloadByteAt(0) & 0x10) != 0;

        private static RtpFecReceiveBuffer Buffer(Clock clock, List<(RTPPacket Packet, ushort Framing)> delivered,
            TimeSpan? wait = null, bool detectStart = true)
            => new RtpFecReceiveBuffer(wait ?? TimeSpan.FromMilliseconds(20),
                (packet, framing) => delivered.Add((packet, framing)), () => clock.Now, clock.Schedule,
                detectStart ? IsStart : (Func<RTPPacket, bool>)null);

        [Fact]
        public void LostMiddlePacketIsInsertedBeforeWholeFrameIsDelivered()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true) }, 100, false);
            buffer.AddBatch(new[] { Packet(102, 3000, marker: true) }, 102, false);
            Assert.Empty(delivered);

            buffer.AddBatch(new[] { Packet(101, 3000) }, 103, true, new ushort[] { 100, 101, 102 });

            Assert.Equal(new ushort[] { 100, 101, 102 }, delivered.Select(item => item.Packet.Header.SequenceNumber));
            Assert.Equal(new ushort[] { 100, 101, 102 }, delivered.Select(item => item.Framing));
            Assert.Equal(0, buffer.BufferedPacketCount);
        }

        [Fact]
        public void MissingFirstPacketCanExtendTentativeStartupBackwards()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(501, 3000), Packet(502, 3000, marker: true) }, 502, false);
            Assert.Empty(delivered);

            buffer.AddBatch(new[] { Packet(500, 3000, start: true) }, 503, true, new ushort[] { 500, 501, 502 });

            Assert.Equal(new ushort[] { 500, 501, 502 }, delivered.Select(item => item.Packet.Header.SequenceNumber));
        }

        [Fact]
        public void RepairCoverageCanConfirmStartupWithoutCodecSpecificStartDetection()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered, detectStart: false);
            buffer.AddBatch(new[] { Packet(10, 3000), Packet(11, 3000, marker: true) }, 11, false);
            Assert.Empty(delivered);

            buffer.AddBatch(Array.Empty<RTPPacket>(), 12, true, new ushort[] { 10, 11 });

            Assert.Equal(new ushort[] { 10, 11 }, delivered.Select(item => item.Packet.Header.SequenceNumber));
        }

        [Fact]
        public void KnownRepairSlotDoesNotIntroduceFramingGapAcrossSequenceWrap()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(65534, 3000, start: true), Packet(65535, 3000) }, 65535, false);
            buffer.AddBatch(Array.Empty<RTPPacket>(), 0, true);
            buffer.AddBatch(new[] { Packet(1, 3000, marker: true) }, 1, false);

            Assert.Equal(new ushort[] { 65534, 65535, 1 }, delivered.Select(item => item.Packet.Header.SequenceNumber));
            Assert.Equal(new ushort[] { 65534, 65535, 0 }, delivered.Select(item => item.Framing));
        }

        [Fact]
        public void RepairRecoveredTerminalFragmentBeforeMediaDoesNotCommitStartupPrematurely()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);

            // All three repairs arrive first. Only the final, one-packet group can
            // reconstruct media immediately; its marker is not proof of frame start.
            buffer.AddBatch(Array.Empty<RTPPacket>(), 105, true, new ushort[] { 100, 101, 102, 103, 104 });
            buffer.AddBatch(Array.Empty<RTPPacket>(), 111, true, new ushort[] { 106, 107, 108, 109, 110 });
            buffer.AddBatch(new[] { Packet(112, 3000, marker: true) }, 113, true, new ushort[] { 112 });
            Assert.Empty(delivered);

            var mediaSequences = new ushort[] { 100, 101, 102, 103, 104, 106, 107, 108, 109, 110 };
            foreach (ushort sequence in mediaSequences)
            {
                buffer.AddBatch(new[] { Packet(sequence, 3000, start: sequence == 100) }, sequence, false);
            }

            Assert.Equal(mediaSequences.Concat(new ushort[] { 112 }), delivered.Select(item => item.Packet.Header.SequenceNumber));
            Assert.Equal(Enumerable.Range(100, 11).Select(sequence => (ushort)sequence), delivered.Select(item => item.Framing));
        }

        [Fact]
        public void DeadlineDropsIncompleteFrameWithoutAnotherArrivalAndLatePacketsCannotResurrectIt()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(500, 3000, start: true), Packet(502, 3000, marker: true) }, 502, false);
            clock.Advance(19);
            Assert.Equal(2, buffer.BufferedPacketCount);
            clock.Advance(1);
            Assert.Equal(0, buffer.BufferedPacketCount);
            Assert.Empty(delivered);

            buffer.AddBatch(new[] { Packet(504, 6000, start: true, marker: true) }, 504, false);
            buffer.AddBatch(new[] { Packet(501, 3000) }, 505, true, new ushort[] { 500, 501, 502 });

            Assert.Single(delivered);
            Assert.Equal(6000u, delivered[0].Packet.Header.Timestamp);
        }

        [Fact]
        public void MissingMarkerExpiresOnIdleWithoutAVisibleSequenceGap()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true) }, 100, false);
            clock.Advance(20);

            Assert.Equal(0, buffer.BufferedPacketCount);
            Assert.Empty(delivered);
            buffer.AddBatch(new[] { Packet(103, 6000, start: true, marker: true) }, 103, false);
            Assert.Single(delivered);
        }

        [Fact]
        public void LateRepairCannotWinWhenTimerCallbackWasDelayed()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true), Packet(102, 3000, marker: true) }, 102, false);
            // Advance monotonic time without executing the scheduled callback, as can
            // happen when the thread pool is busy at the repair deadline.
            clock.Now = 21;
            buffer.AddBatch(new[] { Packet(101, 3000) }, 103, true, new ushort[] { 100, 101, 102 });

            Assert.Empty(delivered);
            Assert.Equal(0, buffer.BufferedPacketCount);
        }

        [Fact]
        public void AmbiguousStartupExpiresWithoutEmittingAnIncompleteFrame()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(501, 3000), Packet(502, 3000, marker: true) }, 502, false);
            clock.Advance(20);

            Assert.Empty(delivered);
            Assert.Equal(0, buffer.BufferedPacketCount);
            buffer.AddBatch(new[] { Packet(504, 6000, start: true, marker: true) }, 504, false);
            Assert.Single(delivered);
        }

        [Fact]
        public void ZeroWaitStillOrdersCompleteFramesAndImmediatelyDropsKnownGaps()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered, TimeSpan.Zero);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true) }, 100, false);
            buffer.AddBatch(new[] { Packet(101, 3000, marker: true) }, 101, false);
            Assert.Equal(2, delivered.Count);

            buffer.AddBatch(new[] { Packet(102, 6000, start: true), Packet(104, 6000, marker: true) }, 104, false);
            Assert.Equal(2, delivered.Count);
            Assert.Equal(0, buffer.BufferedPacketCount);
        }

        [Fact]
        public void OriginalArrivingAfterRecoveredFrameIsNotDeliveredTwice()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true) }, 100, false);
            buffer.AddBatch(new[] { Packet(102, 3000, marker: true) }, 102, false);
            buffer.AddBatch(new[] { Packet(101, 3000) }, 103, true, new ushort[] { 100, 101, 102 });
            buffer.AddBatch(new[] { Packet(101, 3000) }, 101, false);
            buffer.AddBatch(new[] { Packet(102, 3000, marker: true) }, 102, false);

            Assert.Equal(3, delivered.Count);
            Assert.Equal(3, delivered.Select(item => item.Packet.Header.SequenceNumber).Distinct().Count());
        }

        [Fact]
        public void ResetAndDisposeInvalidateAlreadyScheduledTimerCallbacks()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true) }, 100, false);
            var stale = clock.Calls.Last().Callback;
            buffer.Reset();
            buffer.AddBatch(new[] { Packet(1, 9000, start: true, marker: true) }, 1, false);
            stale();
            buffer.Dispose();
            buffer.AddBatch(new[] { Packet(2, 12000, start: true, marker: true) }, 2, false);

            Assert.Single(delivered);
            Assert.Equal(9000u, delivered[0].Packet.Header.Timestamp);
            Assert.Equal(0, buffer.BufferedPacketCount);
        }

        [Fact]
        public void CapacityDropsOversizedFrameAndNextValidFrameCanResume()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            var oversized = Enumerable.Range(0, RtpFecReceiveBuffer.MAX_BUFFERED_MEDIA + 1)
                .Select(index => Packet((ushort)index, 3000, start: index == 0, marker: index == RtpFecReceiveBuffer.MAX_BUFFERED_MEDIA))
                .ToArray();
            buffer.AddBatch(oversized, (ushort)RtpFecReceiveBuffer.MAX_BUFFERED_MEDIA, false);

            Assert.Empty(delivered);
            Assert.InRange(buffer.BufferedPacketCount, 0, RtpFecReceiveBuffer.MAX_BUFFERED_MEDIA);
            buffer.AddBatch(new[] { Packet(140, 6000, start: true, marker: true) }, 140, false);
            Assert.Single(delivered);
        }

        [Fact]
        public void WaitCanBeChangedWithoutDisablingOrderedRecovery()
        {
            var clock = new Clock();
            var delivered = new List<(RTPPacket Packet, ushort Framing)>();
            using var buffer = Buffer(clock, delivered);
            buffer.AddBatch(new[] { Packet(100, 3000, start: true), Packet(102, 3000, marker: true) }, 102, false);
            clock.Advance(10);
            buffer.SetRepairWait(TimeSpan.FromMilliseconds(50));
            clock.Advance(30);
            Assert.Equal(2, buffer.BufferedPacketCount);
            clock.Advance(10);
            Assert.Equal(0, buffer.BufferedPacketCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetRepairWait(TimeSpan.FromMilliseconds(-1)));
        }

        [Fact]
        public void ReentrantArrivalIsSerializedAfterCurrentCompletedFrame()
        {
            var clock = new Clock();
            var sequences = new List<ushort>();
            RtpFecReceiveBuffer buffer = null;
            buffer = new RtpFecReceiveBuffer(TimeSpan.FromMilliseconds(20), (packet, framing) =>
            {
                sequences.Add(packet.Header.SequenceNumber);
                if (packet.Header.SequenceNumber == 100)
                {
                    buffer.AddBatch(new[] { Packet(102, 6000, start: true, marker: true) }, 102, false);
                }
            }, () => clock.Now, clock.Schedule, IsStart);
            using (buffer)
            {
                buffer.AddBatch(new[] { Packet(100, 3000, start: true), Packet(101, 3000, marker: true) }, 101, false);
            }

            Assert.Equal(new ushort[] { 100, 101, 102 }, sequences);
        }
    }
}
