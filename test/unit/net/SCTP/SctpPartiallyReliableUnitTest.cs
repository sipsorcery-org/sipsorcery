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
    public class SctpPartiallyReliableUnitTest
    {
        private ILogger logger = null;

        public SctpPartiallyReliableUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that the SCTP association correctly sets the PartiallyReliable extension as supported  
        /// when both endpoints support it
        /// </summary>
        [Fact]
        public void ForwardTSNSupportedSet()
        {
            (var aAssoc, var bAssoc) = AssociationTestHelper.GetConnectedAssociations(logger, 1400);
            Assert.True(aAssoc.SupportsPartiallyReliable);
            Assert.True(bAssoc.SupportsPartiallyReliable);
        }

        /// <summary>
        /// Tests that the SCTP association correctly serializes and deserializes the ForwardTSNSupported optional parameter  
        /// when serializing INIT, INIT-ACK and state cookies.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ForwardTSNSerializedInitAck(bool supported)
        {
            SctpPacket initPacket = new SctpPacket(5000, 5000, 0);
            uint remoteTag = Crypto.GetRandomUInt();
            uint remoteTSN = Crypto.GetRandomUInt();
            uint remoteARwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;

            SctpInitChunk initChunk = new SctpInitChunk(SctpChunkType.INIT, remoteTag, remoteTSN, remoteARwnd,
                SctpAssociation.DEFAULT_NUMBER_OUTBOUND_STREAMS, SctpAssociation.DEFAULT_NUMBER_INBOUND_STREAMS);
            Assert.True(initChunk.ForwardTSNSupported);
            initChunk.ForwardTSNSupported = supported;
            Assert.Equal(supported, initChunk.ForwardTSNSupported);
            initPacket.AddChunk(initChunk);

            var serializedInit = initPacket.GetBytes();
            var deserializedInit = SctpPacket.Parse(serializedInit, 0, serializedInit.Length);

            Assert.Equal(supported, (deserializedInit.Chunks.First() as SctpInitChunk).ForwardTSNSupported);

            var transport = new MockSctpTransport();
            var initAckPacket = transport.GetInitAck(initPacket);
            var initAck = initAckPacket.Chunks.First() as SctpInitChunk;

            Assert.Equal(supported, initAck.ForwardTSNSupported);

            var cookie = TinyJson.JSONParser.FromJson<SctpTransportCookie>(System.Text.Encoding.UTF8.GetString(initAck.StateCookie));

            Assert.Equal(supported, cookie.ForwardTSNSupported);
        }

        /// <summary>
        /// Tests that the FORWARD-TSN message serializes and deserializes
        /// </summary>
        [Fact]
        public void ForwardTSNSerialized()
        {
            SctpPacket initPacket = new SctpPacket(5000, 5000, 0);

            SctpForwardCumulativeTSNChunk fwdTsnChunk = new SctpForwardCumulativeTSNChunk(7213U)
            {
                StreamSequenceAssociations = new System.Collections.Generic.Dictionary<ushort, ushort>()
                {
                    {123,456},
                    {543,729},
                    {920,102}
                }
            };

            initPacket.AddChunk(fwdTsnChunk);

            var serializedInit = initPacket.GetBytes();
            var deserializedInit = SctpPacket.Parse(serializedInit, 0, serializedInit.Length);

            Assert.Equal(fwdTsnChunk.NewCumulativeTSN, (deserializedInit.Chunks.First() as SctpForwardCumulativeTSNChunk).NewCumulativeTSN);
            Assert.Equal(fwdTsnChunk.StreamSequenceAssociations.Count, (deserializedInit.Chunks.First() as SctpForwardCumulativeTSNChunk).StreamSequenceAssociations.Count);

            foreach (var kvp in (deserializedInit.Chunks.First() as SctpForwardCumulativeTSNChunk).StreamSequenceAssociations)
            {
                Assert.Equal(fwdTsnChunk.StreamSequenceAssociations[kvp.Key], kvp.Value);
            }

        }

        /// <summary>
        /// Tests that the SCTP association correctly sets the PartiallyReliable extension as supported
        /// <summary>
        /// Tests that a message can be abandoned by timeout or retransmit limitations for both ordered and unordered modes
        /// </summary>
        [Theory]
        [InlineData(true, 50, uint.MaxValue)]
        [InlineData(true, uint.MaxValue, 0)]
        [InlineData(false, 50, uint.MaxValue)]
        [InlineData(false, uint.MaxValue, 0)]
        public async void MessageAbandoned(bool ordered, uint timeoutMillis, uint maxRetransmits)
        {
            uint initialTSN = 0;

            SctpDataReceiver receiver = new SctpDataReceiver(SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW, null, null, 1400, initialTSN);
            SctpDataSender sender = new SctpDataSender("dummy", null, 1400, initialTSN, SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW);
            sender._burstPeriodMilliseconds = 1;
            sender._rtoInitialMilliseconds = 1;
            sender._rtoMinimumMilliseconds = 1;

            sender._supportsPartialReliabilityExtension = true;
            sender.StartSending();

            int framesSent = 0;

            int forwardTSNCount = 0;

            Action<SctpDataChunk> senderSendData = (chunk) =>
            {
                if (chunk.UserData[0] == 0x02)
                {
                    logger.LogDebug($"Data chunk {chunk.TSN} NOT provided to receiver.");
                }
                else
                {
                    logger.LogDebug($"Data chunk {chunk.TSN} provided to receiver.");
                    receiver.OnDataChunk(chunk);
                    logger.LogDebug($"SACK chunk {chunk.TSN} provided to sender.");
                    sender.GotSack(receiver.GetSackChunk());
                }
                framesSent++;
            };

            Action<SctpForwardCumulativeTSNChunk> senderSendForwardTSN = (chunk) =>
            {
                logger.LogDebug($"Forward TSN chunk {chunk.NewCumulativeTSN} provided to receiver.");
                receiver.OnForwardCumulativeTSNChunk(chunk);
                forwardTSNCount++;
            };

            Action<SctpSackChunk> receiverSendSack = (chunk) =>
            {
                logger.LogDebug($"SACK chunk {chunk.CumulativeTsnAck} provided to the sender.");
                sender.GotSack(chunk);
            };

            int framesReceived = 0;

            Action<SctpDataFrame> receiverOnFrame = (f) =>
            {
                logger.LogDebug($"Receiver got frame of length {f.UserData?.Length}.");
                framesReceived++;
            };

            receiver._onFrameReady = receiverOnFrame;
            receiver._sendSackChunk = receiverSendSack;
            sender._sendDataChunk = senderSendData;
            sender._sendForwardTsn = senderSendForwardTSN;

            sender.SendData(0, 0, new byte[] { 0x01 }, ordered);
            sender.SendData(0, 0, new byte[] { 0x02 }, ordered, timeoutMillis, maxRetransmits);

            if (timeoutMillis < uint.MaxValue)
            {
                // If we are testing message timeout
                // delay so that message 0x02 times out and becomes abandoned
                await Task.Delay((int)timeoutMillis + sender._burstPeriodMilliseconds);
            }

            sender.SendData(0, 0, new byte[] { 0x03 }, ordered);
            await Task.Delay(sender._burstPeriodMilliseconds * 10);

            Assert.Equal(3, framesSent);
            Assert.Equal(2, framesReceived);
            Assert.Equal(initialTSN + 2, sender.AdvancedPeerAckPoint);
            Assert.Equal(initialTSN + 3, sender.TSN);
            Assert.Equal(initialTSN + 2, receiver.CumulativeAckTSN);
            Assert.Equal(0, receiver.ForwardTSNCount);
            Assert.Equal(1, forwardTSNCount);
        }
        /// </summary>
        [Fact]
        public void ForwardTSNSupportedSet()
        {
            (var aAssoc, var bAssoc) = AssociationTestHelper.GetConnectedAssociations(logger, 1400);
            Assert.True(aAssoc.SupportsPartiallyReliable);
            Assert.True(bAssoc.SupportsPartiallyReliable);
        }
    }
}
