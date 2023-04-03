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
        private ILogger logger = null;

        public SctpDataSendRecvUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a SACK chunk can trigger a retransmit after being reported missing 3 times (RFC4960 7.2.4).
        /// </summary>
        [Fact]
        public async Task SACKChunkRetransmit()
        {
            uint arwnd = 131072;
            ushort mtu = 1400;
            uint initialTSN = Crypto.GetRandomUInt(true);

            SctpDataReceiver receiver = new SctpDataReceiver(arwnd, mtu, initialTSN);
            SctpDataSender sender = new SctpDataSender("dummy", null, mtu, initialTSN, arwnd);
            sender.StartSending();

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK. 
            Action<SctpDataChunk> doSend = (chunk) =>
            {
                receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());
            };

            Action<SctpDataChunk> dontSend = (chunk) => { };

            sender._sendDataChunk = doSend;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 1, sender.TSN);
            await Task.Delay(100);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // This send to the receiver is blocked so the receivers ACK TSN should stay the same.
            sender._sendDataChunk = dontSend;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 2, sender.TSN);
            await Task.Delay(100);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // Unblock. Receiver's ACK TSN should not advance as it is missing chunk #2.
            sender._sendDataChunk = doSend;
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 3, sender.TSN);
            await Task.Delay(100);
            Assert.Equal(initialTSN, receiver.CumulativeAckTSN);

            // When the chunk is reported missing a further two times, the sender 
            // enters fast-recovery mode and retransmits the missing block.
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            sender.SendData(0, 0, new byte[] { 0x00, 0x01, 0x02 });
            Assert.Equal(initialTSN + 5, sender.TSN);
            await Task.Delay(250);
            // Fast recovery has been entered and missing chunk has been sent.
            // Sender and receiver are both up-to-date.
            Assert.Equal(initialTSN + 4, receiver.CumulativeAckTSN);
        }

        /// <summary>
        /// Tests that a send/receive works correctly if the initial DATA chunk is dropped.
        /// </summary>
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

            // This local function replicates a data chunk being sent from a data
            // sender to the receiver of a remote peer and the return of the SACK. 
            Action<SctpDataChunk> doSend = (chunk) =>
            {
                if (chunk.TSN == initialTSN && chunk.SendCount == 1)
                {
                    logger.LogDebug($"Data chunk {chunk.TSN} dropped.");
                }
                else
                {
                    receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());
                }
            };
            sender._sendDataChunk = doSend;

            sender.SendData(0, 0, new byte[] { 0x55 });
            Assert.Equal(initialTSN + 1, sender.TSN);
            await Task.Delay(100);
            Assert.Null(receiver.CumulativeAckTSN);

            await Task.Delay(500);

            sender.SendData(0, 0, new byte[] { 0x55 });
            Assert.Equal(initialTSN + 2, sender.TSN);
            await Task.Delay(1250);
            Assert.Equal(initialTSN + 1, receiver.CumulativeAckTSN);

            await Task.Delay(500);

            sender.SendData(0, 0, new byte[] { 0x55 });
            Assert.Equal(initialTSN + 3, sender.TSN);
            await Task.Delay(100);
            Assert.Equal(initialTSN + 2, receiver.CumulativeAckTSN);
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
                logger.LogDebug($"Data chunk {chunk.TSN} provided to receiver.");
                var frames = receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());

                if (frames.Count > 0)
                {
                    logger.LogDebug($"Receiver got frame of length {frames.First().UserData?.Length}.");
                    frame = frames.First();
                    frameReady.Set();
                }

            };
            sender._sendDataChunk = doSend;

            byte[] buffer = new byte[10 * mtu];
            Crypto.GetRandomBytes(buffer);
            string hash = Crypto.GetSHA256Hash(buffer);

            logger.LogDebug($"Medium buffer hash {hash}.");

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
                logger.LogDebug($"Data chunk {chunk.TSN} provided to receiver.");
                var frames = receiver.OnDataChunk(chunk);
                sender.GotSack(receiver.GetSackChunk());
                
                if (frames.Count > 0)
                {
                    logger.LogDebug($"Receiver got frame of length {frames.First().UserData?.Length}.");
                    frame = frames.First();
                    frameReady.Set();
                }

            };
            sender._sendDataChunk = doSend;

            byte[] buffer = new byte[RTCSctpTransport.SCTP_DEFAULT_MAX_MESSAGE_SIZE];
            Crypto.GetRandomBytes(buffer);
            string hash = Crypto.GetSHA256Hash(buffer);

            logger.LogDebug($"Max buffer hash {hash}.");

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
                    logger.LogDebug($"Data chunk {chunk.TSN} dropped.");
                }
                else
                {
                    logger.LogDebug($"Data chunk {chunk.TSN} provided to receiver.");
                    var frames = receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());

                    if (frames.Count > 0)
                    {
                        logger.LogDebug($"Receiver got frame of length {frames.First().UserData?.Length}.");
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

            logger.LogDebug($"Medium buffer hash {hash}.");

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
                    logger.LogDebug($"Data chunk {chunk.TSN} dropped.");
                }
                else
                {
                    logger.LogDebug($"Data chunk {chunk.TSN} provided to receiver.");
                    var frames = receiver.OnDataChunk(chunk);
                    sender.GotSack(receiver.GetSackChunk());

                    if (frames.Count > 0)
                    {
                        logger.LogDebug($"Receiver got frame of length {frames.First().UserData?.Length}.");
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

            logger.LogDebug($"Max buffer hash {hash}.");

            await Task.Delay(50);

            sender.SendData(0, 0, buffer);

            frameReady.WaitHandle.WaitOne(10000, true);

            Assert.False(frame.IsEmpty());
            Assert.Equal(buffer.Length, frame.UserData.Length);
            Assert.Equal(hash, Crypto.GetSHA256Hash(frame.UserData));
        }
    }
}
