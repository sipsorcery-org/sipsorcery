//-----------------------------------------------------------------------------
// Filename: SctpDataSendRecvUnitTest.cs
//
// Description: Unit tests to check the SctpDataSender and SctpDataReceiver
// classes work together.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpDataSendRecvUnitTest
    {
        private readonly ILogger logger;

        public SctpDataSendRecvUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a SACK chunk can trigger a retransmit after being reported missing 3 times (RFC4960 7.2.4).
        /// </summary>
        /// <remarks>
        /// The "blocked" chunk is dropped via a stable per-chunk predicate (drop only the first transmission
        /// of one TSN) rather than by swapping sender._sendDataChunk between a delivering and a dropping
        /// delegate around timed awaits. The previous approach was racy: SendData only enqueues the chunk and
        /// the sender's worker thread invokes _sendDataChunk asynchronously, so under load the chunk that was
        /// meant to be dropped could be dequeued and delivered after the delegate had been swapped back,
        /// advancing the receiver past the expected cumulative ACK (intermittent
        /// "Expected initialTSN, Actual initialTSN+2" failures).
        /// </remarks>
        [Fact]
        public async Task SACKChunkRetransmit()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);
            SctpDataSender sender = new SctpDataSender("dummy", null, mtu, initialTSN, arwnd);
            sender.StartSending();

            // The second chunk (this TSN) is the one we force to go missing. Only its FIRST transmission is
            // dropped; everything else - including its eventual fast-retransmit (SendCount > 1) - is delivered
            // to the receiver and SACKed back. This is deterministic regardless of the worker-thread timing.
            uint missingTSN = initialTSN + 1;

            Action<SctpDataChunk> relay = (chunk) =>
            {
                if (chunk.TSN == missingTSN && chunk.SendCount == 1)
                {
                    // Drop only the first transmission so the chunk is reported missing in the gap ack blocks.
                    return;
                }

                receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());
            };
            sender._sendDataChunk = relay;

            // Queue five chunks (TSNs initialTSN .. initialTSN+4). The second is dropped on its first send,
            // leaving a gap. As the later chunks arrive out of order the gap is reported missing; after the
            // third miss indication the sender enters fast recovery (RFC4960 7.2.4) and retransmits the
            // missing chunk, which is then delivered, allowing the cumulative ACK to advance to the last TSN.
            for (int i = 0; i < 5; i++)
            {
                sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            }

            // TSN is advanced synchronously by SendData so this is not timing dependent.
            Assert.Equal(initialTSN + 5, sender.TSN);

            // Poll (rather than sleeping a fixed amount) for the fast retransmit to recover the gap. Because
            // the missing chunk's first send was dropped, the only way the receiver's cumulative ACK can reach
            // initialTSN+4 is for the chunk to be fast-retransmitted and delivered - so reaching it proves the
            // retransmit path ran.
            await WaitForConditionAsync(
                () => receiver.CumulativeAckTSN == initialTSN + 4,
                TimeSpan.FromSeconds(5));

            Assert.Equal(initialTSN + 4, receiver.CumulativeAckTSN);
        }

        /// <summary>
        /// Polls <paramref name="condition"/> until it returns true or <paramref name="timeout"/> elapses.
        /// Used to wait on state driven by the sender's background worker thread without relying on a fixed
        /// sleep that can be too short under load.
        /// </summary>
        private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(25);
            }
        }

        /// <summary>
        /// Tests that a send/receive works correctly if the initial DATA chunk is dropped.
        /// </summary>
        /// <remarks>
        /// The original version of this test asserted that the receiver's cumulative ACK was still null a
        /// fixed 100ms after the (dropped) first send, with the sender's RTO set to 250ms. That negative
        /// assertion raced the RTO timer: under CI load Task.Delay can resume well past the 250ms RTO, by
        /// which time the chunk had been retransmitted, delivered and SACKed (intermittent "Expected: null,
        /// Actual: initialTSN" failures). The drop of the first transmission is guaranteed by the relay
        /// predicate and verified from the delivery log instead, and the positive outcomes are polled for
        /// rather than asserted after fixed sleeps.
        /// </remarks>
        [Fact]
        public async Task InitialDataChunkDropped()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);
            SctpDataSender sender = new SctpDataSender("dummy", null, mtu, initialTSN, arwnd);
            sender._rtoInitialMilliseconds = 250;
            sender._rtoMinimumMilliseconds = 250;
            sender._rtoMaximumMilliseconds = 250;
            sender.StartSending();

            var delivered = new ConcurrentBag<(uint tsn, int sendCount)>();

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK.
            // The first transmission of the first chunk is dropped; its RTO retransmit
            // (SendCount > 1) and everything else is delivered.
            Action<SctpDataChunk> doSend = (chunk) =>
            {
                if (chunk.TSN == initialTSN && chunk.SendCount == 1)
                {
                    logger.LogDebug("Data chunk {TSN} dropped.", chunk.TSN);
                }
                else
                {
                    delivered.Add((chunk.TSN, chunk.SendCount));
                    receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());
                }
            };
            sender._sendDataChunk = doSend;

            // The first chunk's initial transmission is dropped, so the cumulative ACK can only reach
            // initialTSN via the RTO retransmit - reaching it proves the retransmit path ran.
            sender.SendData(0, 0, new byte[] { 0x55 });
            Assert.Equal(initialTSN + 1, sender.TSN);
            await WaitForConditionAsync(() => receiver.CumulativeAckTSN == initialTSN, TimeSpan.FromSeconds(5));
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            sender.SendData(0, 0, new byte[] { 0x55 });
            Assert.Equal(initialTSN + 2, sender.TSN);
            await WaitForConditionAsync(() => receiver.CumulativeAckTSN == initialTSN + 1, TimeSpan.FromSeconds(5));
            Assert.Equal(initialTSN + 1, receiver.CumulativeAckTSN);

            sender.SendData(0, 0, new byte[] { 0x55 });
            Assert.Equal(initialTSN + 3, sender.TSN);
            await WaitForConditionAsync(() => receiver.CumulativeAckTSN == initialTSN + 2, TimeSpan.FromSeconds(5));
            Assert.Equal(initialTSN + 2, receiver.CumulativeAckTSN);

            // The first transmission of the initial chunk must never have reached the receiver.
            Assert.DoesNotContain(delivered, d => d.tsn == initialTSN && d.sendCount == 1);
        }

        /// <summary>
        /// Tests that a buffer with an 10 x MTU length size gets
        /// processed correctly.
        /// </summary>
        [Fact]
        public void MediumBufferSend()
        {
            ushort mtu = 1400;
            SctpDataReceiver receiver = new SctpDataReceiver(1000, mtu, 0);
            SctpDataSender sender = new SctpDataSender("dummy", null, mtu, 0, 1000);
            sender.StartSending();

            SctpDataFrame frame = SctpDataFrame.Empty;
            ManualResetEventSlim frameReady = new ManualResetEventSlim(false);

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK. 
            Action<SctpDataChunk> doSend = (chunk) =>
            {
                logger.LogDebug("Data chunk {TSN} provided to receiver.", chunk.TSN);
                var frames = receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());

                if (frames.Count > 0)
                {
                    logger.LogDebug("Receiver got frame of length {Length}.", frames.First().UserData?.Length);
                    frame = frames.First();
                    frameReady.Set();
                }

            };
            sender._sendDataChunk = doSend;

            byte[] buffer = new byte[10 * mtu];
            Crypto.GetRandomBytes(buffer);
            string hash = Crypto.GetSHA256Hash(buffer);

            logger.LogDebug("Medium buffer hash {Hash}.", hash);

            sender.SendData(0, 0, buffer);

            frameReady.WaitHandle.WaitOne(5000, true);

            Assert.False(frame.IsEmpty());
            Assert.Equal(buffer.Length, frame.UserData.Length);
            Assert.Equal(hash, Crypto.GetSHA256Hash(frame.UserData));
        }

        /// <summary>
        /// Tests that a buffer with length of the default maximum data channel size gets
        /// processed correctly.
        /// </summary>
        [Fact]
        public void MaxBufferSend()
        {
            uint arwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;
            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, 1400, 0);
            SctpDataSender sender = new SctpDataSender("dummy", null, 1400, 0, arwnd);
            sender.StartSending();

            SctpDataFrame frame = SctpDataFrame.Empty;
            ManualResetEventSlim frameReady = new ManualResetEventSlim(false);

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK. 
            Action<SctpDataChunk> doSend = (chunk) =>
            {
                logger.LogDebug("Data chunk {TSN} provided to receiver.", chunk.TSN);
                var frames = receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());

                if (frames.Count > 0)
                {
                    logger.LogDebug("Receiver got frame of length {Length}.", frames.First().UserData?.Length);
                    frame = frames.First();
                    frameReady.Set();
                }

            };
            sender._sendDataChunk = doSend;

            byte[] buffer = new byte[RTCSctpTransport.SCTP_DEFAULT_MAX_MESSAGE_SIZE];
            Crypto.GetRandomBytes(buffer);
            string hash = Crypto.GetSHA256Hash(buffer);

            logger.LogDebug("Max buffer hash {Hash}.", hash);

            sender.SendData(0, 0, buffer);

            frameReady.WaitHandle.WaitOne(10000, true);

            Assert.False(frame.IsEmpty());
            Assert.Equal(RTCSctpTransport.SCTP_DEFAULT_MAX_MESSAGE_SIZE, (uint)frame.UserData.Length);
            Assert.Equal(hash, Crypto.GetSHA256Hash(frame.UserData));
        }

        /// <summary>
        /// Tests that a buffer with a length of 10 x MTU gets
        /// processed correctly when sends are randomly discarded.
        /// </summary>
        [Fact]
        public void MediumBufferSendWithRandomDrops()
        {
            ushort mtu = 1400;
            SctpDataReceiver receiver = new SctpDataReceiver(1000, mtu, 0);
            SctpDataSender sender = new SctpDataSender("dummy", null, mtu, 0, 1000);
            sender._burstPeriodMilliseconds = 1;
            sender._rtoInitialMilliseconds = 1;
            sender._rtoMinimumMilliseconds = 1;
            sender.StartSending();

            SctpDataFrame frame = SctpDataFrame.Empty;
            ManualResetEventSlim frameReady = new ManualResetEventSlim(false);

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK. 
            Action<SctpDataChunk> doSend = async (chunk) =>
            {
                if (chunk.SendCount == 1 && Crypto.GetRandomInt(0, 99) % 5 == 0)
                {
                    logger.LogDebug("Data chunk {TSN} dropped.", chunk.TSN);
                }
                else
                {
                    logger.LogDebug("Data chunk {TSN} provided to receiver.", chunk.TSN);
                    var frames = receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());

                    if (frames.Count > 0)
                    {
                        logger.LogDebug("Receiver got frame of length {Length}.", frames.First().UserData?.Length);
                        frame = frames.First();
                        frameReady.Set();
                    }
                }

                await Task.Delay(1);
            };
            sender._sendDataChunk = doSend;

            byte[] buffer = new byte[10 * mtu];
            Crypto.GetRandomBytes(buffer);
            string hash = Crypto.GetSHA256Hash(buffer);

            logger.LogDebug("Medium buffer hash {Hash}.", hash);

            sender.SendData(0, 0, buffer);

            frameReady.WaitHandle.WaitOne(5000, true);

            Assert.False(frame.IsEmpty());
            Assert.Equal(buffer.Length, frame.UserData.Length);
            Assert.Equal(hash, Crypto.GetSHA256Hash(frame.UserData));
        }

        /// <summary>
        /// Tests that a buffer with length of the default maximum data channel size gets
        /// processed correctly when sends are randomly discarded.
        /// </summary>
        [Fact(Skip = "Frequently fails to start the sending thread.")]
        public async Task MaxBufferSendWithRandomDrops()
        {
            SctpDataReceiver receiver = new SctpDataReceiver(1000, 1400, 0);
            SctpDataSender sender = new SctpDataSender("dummy", null, 1400, 0, 1000);
            sender._burstPeriodMilliseconds = 1;
            sender._rtoInitialMilliseconds = 1;
            sender._rtoMinimumMilliseconds = 1;
            sender.StartSending();

            SctpDataFrame frame = SctpDataFrame.Empty;
            ManualResetEventSlim frameReady = new ManualResetEventSlim(false);

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK. 
            Action<SctpDataChunk> doSend = async (chunk) =>
            {
                if (chunk.SendCount == 1 && Crypto.GetRandomInt(0, 99) % 5 == 0)
                {
                    logger.LogDebug("Data chunk {TSN} dropped.", chunk.TSN);
                }
                else
                {
                    logger.LogDebug("Data chunk {TSN} provided to receiver.", chunk.TSN);
                    var frames = receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());

                    if (frames.Count > 0)
                    {
                        logger.LogDebug("Receiver got frame of length {Length}.", frames.First().UserData?.Length);
                        frame = frames.First();
                        frameReady.Set();
                    }
                }

                await Task.Delay(1);
            };
            sender._sendDataChunk = doSend;

            byte[] buffer = new byte[RTCSctpTransport.SCTP_DEFAULT_MAX_MESSAGE_SIZE];
            //byte[] buffer = new byte[2000];
            Crypto.GetRandomBytes(buffer);
            string hash = Crypto.GetSHA256Hash(buffer);

            logger.LogDebug("Max buffer hash {Hash}.", hash);

            await Task.Delay(50);

            sender.SendData(0, 0, buffer);

            frameReady.WaitHandle.WaitOne(10000, true);

            Assert.False(frame.IsEmpty());
            Assert.Equal(buffer.Length, frame.UserData.Length);
            Assert.Equal(hash, Crypto.GetSHA256Hash(frame.UserData));
        }
    }
}
