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
