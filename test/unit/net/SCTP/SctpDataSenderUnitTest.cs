//-----------------------------------------------------------------------------
// Filename: SctpDataSenderUnitTest.cs
//
// Description: Unit tests for the SctpDataSender class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Apr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpDataSenderUnitTest
    {
        private ILogger logger = null;

        public SctpDataSenderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a small buffer, less than MTU, gets processed correctly.
        /// </summary>
        [Fact]
        public async Task SmallBufferSend()
        {
            BlockingCollection<byte[]> inStm = new BlockingCollection<byte[]>();
            BlockingCollection<byte[]> outStm = new BlockingCollection<byte[]>();
            var mockTransport = new MockB2BSctpTransport(outStm, inStm);
            SctpAssociation assoc = new SctpAssociation(mockTransport, null, 5000, 5000, 1400, 0);

            SctpDataSender sender = new SctpDataSender("dummy", assoc.SendChunk, 1400, 0, 1000);
            sender.StartSending();
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });

            await Task.Delay(100);

            Assert.Single(outStm);

            byte[] sendBuffer = outStm.Single();
            SctpPacket pkt = SctpPacket.Parse(sendBuffer, 0, sendBuffer.Length);

            Assert.NotNull(pkt);
            Assert.NotNull(pkt.Chunks.Single() as SctpDataChunk);

            var datachunk = pkt.Chunks.Single() as SctpDataChunk;

            Assert.Equal("000102", datachunk.UserData.HexStr());
        }

        /// <summary>
        /// Tests that the congestion window gets filled up.
        /// </summary>
        [Fact]
        public async Task FillCongestionWindow()
        {
            uint arwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            ushort mtu = 1400;

            Action<SctpDataChunk> blackholeSender = (chunk) => { };

            SctpDataSender sender = new SctpDataSender("dummy", blackholeSender, mtu, 0, arwnd);
            sender.StartSending();

            Assert.Equal(SctpDataSender.CONGESTION_WINDOW_FACTOR, sender._congestionWindow);

            var buffer = new byte[mtu];
            
            sender.SendData(0, 0, buffer);
            sender.SendData(0, 0, buffer);
            sender.SendData(0, 0, buffer);
            sender.SendData(0, 0, buffer);

            await Task.Delay(100);

            Assert.True(sender._congestionWindow < sender._outstandingBytes);
        }

        /// <summary>
        /// Regression test for the DoSend Reset-race (producer-consumer lost-wakeup).
        /// Before the fix, <c>_senderMre.Reset()</c> ran AFTER the send work, so any
        /// <c>Set()</c> fired by an incoming SACK in the window between the last chunk
        /// sent and the Reset was wiped, and the thread blocked for the full
        /// <see cref="SctpDataSender.BURST_PERIOD_MILLISECONDS"/> (50 ms) before noticing
        /// more cwnd was available. This capped throughput at
        /// <c>MAX_BURST * MTU / BURST_PERIOD = 4 * 1300 / 50 ms = 104 KB/s</c> even on
        /// localhost loopback where SACKs round-trip in microseconds.
        ///
        /// With the fix (Reset moved to the TOP of the loop, preserving Set-during-send
        /// for the next Wait), 500 KB ships in well under 500 ms on any reasonable
        /// machine - the bottleneck becomes the ManualResetEventSlim wake latency
        /// (sub-millisecond), not the 50 ms burst period.
        ///
        /// Pre-fix expected: 500 KB / 104 KB/s = ~4800 ms + slow-start ramp = ~5+ seconds.
        /// Post-fix expected: under 500 ms.
        /// Threshold set at 2000 ms to leave slack for CI jitter while still proving
        /// we're not on the broken 100-KB/s ceiling.
        /// </summary>
        [Fact]
        public async Task Throughput_FastSackWake_ExceedsBurstCeiling()
        {
            uint arwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            ushort mtu = 1400;
            uint initialTSN = 0;

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);
            SctpDataSender sender = new SctpDataSender("throughput-probe", null, mtu, initialTSN, arwnd);

            // Deliver data + SACK synchronously on the sender's own DoSend thread - the
            // worst case for the Reset race, because the SACK's Set() fires BEFORE the
            // Reset that wiped it pre-fix.
            Action<SctpDataChunk> doSend = (chunk) =>
            {
                receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());
            };
            sender._sendDataChunk = doSend;
            sender.StartSending();

            // 360 chunks of 1400 bytes = 504 KB. Large enough to exit slow-start and
            // enter the steady-state where the race would dominate, small enough that
            // a fast machine finishes in milliseconds.
            const int chunksToSend = 360;
            var buffer = new byte[mtu];

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < chunksToSend; i++)
            {
                sender.SendData(0, 0, buffer);
            }

            // Wait for all chunks to be acknowledged by the receiver.
            uint expectedAckTSN = (uint)(chunksToSend - 1);
            while (receiver.CumulativeAckTSN != expectedAckTSN && sw.ElapsedMilliseconds < 10000)
            {
                await Task.Delay(5);
            }
            sw.Stop();

            Assert.Equal(expectedAckTSN, receiver.CumulativeAckTSN);
            Assert.True(sw.ElapsedMilliseconds < 2000,
                $"Throughput regression: {chunksToSend * mtu} bytes took {sw.ElapsedMilliseconds} ms " +
                $"(effective {(double)(chunksToSend * mtu) / sw.ElapsedMilliseconds:F1} KB/s). " +
                $"Expected under 2000 ms with SACK-wake race fix. Pre-fix throughput is " +
                $"capped at ~104 KB/s by the Reset race, giving ~{chunksToSend * mtu / 104} ms.");
        }

        /// <summary>
        /// Tests that the congestion window increases in slow start mode.
        /// </summary>
        [Fact(Skip = "Regularly failing on AppVeyor CI.")]
        public async Task IncreaseCongestionWindowSlowStart()
        {
            uint arwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            ushort mtu = 1400;
            uint initialTSN = 0;

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);
            SctpDataSender sender = new SctpDataSender("dummy", null, mtu, initialTSN, arwnd);
            sender._burstPeriodMilliseconds = 1;

            Action<SctpDataChunk> reluctantSender = (chunk) =>
            {
                if (chunk.TSN % 5 == 0)
                {
                    receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());
                }
            };

            sender._sendDataChunk = reluctantSender;
            sender.StartSending();

            Assert.Equal(SctpDataSender.CONGESTION_WINDOW_FACTOR, sender._congestionWindow);

            var buffer = new byte[mtu];

            for (int i = 0; i <= 10; i++)
            {
                sender.SendData(0, 0, buffer);
            }

            await Task.Delay(100);

            Assert.Equal(SctpDataSender.CONGESTION_WINDOW_FACTOR + mtu, sender._congestionWindow);
        }
    }
}
