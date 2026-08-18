//-----------------------------------------------------------------------------
// Filename: HepPacketUnitTest.cs
//
// Description: Unit tests for the Homer Encapsulation Protocol packet
// serialisation.
//
// The decoder used by these tests is written from the published HEPv3 layout
// rather than from the encoder's own constants. Sharing the constants is how a
// hand rolled binary format passes its own tests and is then rejected by a real
// collector.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Aug 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using SIPSorcery.SIP;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class HepPacketUnitTest
    {
        private const string SIP_MESSAGE =
            "OPTIONS sip:bob@example.com SIP/2.0\r\nCall-ID: abc123\r\nContent-Length: 0\r\n\r\n";

        private static byte[] GetPacket(
            string src = "192.0.2.1:5060",
            string dst = "198.51.100.1:5060",
            SIPProtocolsEnum protocol = SIPProtocolsEnum.udp,
            uint agentID = 7,
            string password = null,
            string payload = SIP_MESSAGE)
            => HepPacket.GetBytes(
                new SIPEndPoint(protocol, ParseEndPoint(src)),
                new SIPEndPoint(protocol, ParseEndPoint(dst)),
                new DateTime(2026, 8, 17, 12, 0, 0, 250, DateTimeKind.Utc),
                agentID, password, payload);

        /// <summary>
        /// The test project targets frameworks without IPEndPoint.Parse.
        /// </summary>
        private static IPEndPoint ParseEndPoint(string value)
        {
            if (value.StartsWith("["))
            {
                int close = value.IndexOf(']');
                return new IPEndPoint(
                    IPAddress.Parse(value.Substring(1, close - 1)),
                    int.Parse(value.Substring(close + 2)));
            }

            int colon = value.LastIndexOf(':');
            return new IPEndPoint(
                IPAddress.Parse(value.Substring(0, colon)),
                int.Parse(value.Substring(colon + 1)));
        }

        /// <summary>
        /// Walks the packet the way a collector does and returns the chunk values by type.
        /// Throws if the framing does not hold together.
        /// </summary>
        private static Dictionary<int, byte[]> Decode(byte[] packet)
        {
            Assert.Equal("HEP3", Encoding.ASCII.GetString(packet, 0, 4));
            Assert.Equal(packet.Length, (packet[4] << 8) | packet[5]);

            var chunks = new Dictionary<int, byte[]>();
            int offset = 6;

            while (offset < packet.Length)
            {
                int vendor = (packet[offset] << 8) | packet[offset + 1];
                int type = (packet[offset + 2] << 8) | packet[offset + 3];
                int length = (packet[offset + 4] << 8) | packet[offset + 5];

                Assert.Equal(0, vendor);
                Assert.True(length >= 6, $"Chunk 0x{type:x4} declared {length} bytes, less than its own header.");
                Assert.True(offset + length <= packet.Length, $"Chunk 0x{type:x4} runs past the end of the packet.");

                byte[] value = new byte[length - 6];
                Buffer.BlockCopy(packet, offset + 6, value, 0, value.Length);
                chunks[type] = value;
                offset += length;
            }

            Assert.Equal(packet.Length, offset);
            return chunks;
        }

        private static uint ReadUInt32(byte[] b)
            => ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];

        [Fact]
        public void SerialisedPacketIsWellFormedUnitTest()
        {
            var chunks = Decode(GetPacket());

            foreach (int required in new int[] { 0x01, 0x02, 0x03, 0x04, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0f })
            {
                Assert.True(chunks.ContainsKey(required), $"Chunk 0x{required:x4} was missing.");
            }
        }

        [Fact]
        public void AddressesAndPortsRoundTripUnitTest()
        {
            var chunks = Decode(GetPacket("192.0.2.1:5060", "198.51.100.1:5080"));

            Assert.Equal("192.0.2.1", new IPAddress(chunks[0x03]).ToString());
            Assert.Equal("198.51.100.1", new IPAddress(chunks[0x04]).ToString());
            Assert.Equal(5060, ((chunks[0x07][0] << 8) | chunks[0x07][1]));
            Assert.Equal(5080, ((chunks[0x08][0] << 8) | chunks[0x08][1]));
        }

        /// <summary>
        /// HEP uses the Unix address family values. IPv4 happens to agree with .NET's
        /// AddressFamily at 2, which is why using the enum directly went unnoticed.
        /// </summary>
        [Fact]
        public void IPv4FamilyIsUnixValueUnitTest()
            => Assert.Equal(2, Decode(GetPacket())[0x01][0]);

        /// <summary>
        /// .NET numbers InterNetworkV6 as 23, where HEP expects AF_INET6, which is 10.
        /// </summary>
        [Fact]
        public void IPv6FamilyIsUnixValueUnitTest()
        {
            var chunks = Decode(GetPacket("[2001:db8::1]:5060", "[2001:db8::2]:5060"));

            Assert.Equal(10, chunks[0x01][0]);
            Assert.True(chunks.ContainsKey(0x05), "IPv6 source chunk was missing.");
            Assert.True(chunks.ContainsKey(0x06), "IPv6 destination chunk was missing.");
            Assert.False(chunks.ContainsKey(0x03), "An IPv4 chunk should not be present as well.");
            Assert.Equal(16, chunks[0x05].Length);
        }

        /// <summary>
        /// A dual stack capture can see one address of each family. Emitting an IPv4 source
        /// chunk beside an IPv6 destination chunk leaves the collector to guess, so the
        /// pair settles on IPv6 with the v4 address mapped.
        /// </summary>
        [Fact]
        public void MixedAddressFamilyPairIsMappedToIPv6UnitTest()
        {
            var chunks = Decode(GetPacket("192.0.2.1:5060", "[2001:db8::2]:5060"));

            Assert.Equal(10, chunks[0x01][0]);
            Assert.Equal(16, chunks[0x05].Length);
            Assert.Equal(16, chunks[0x06].Length);
            Assert.Equal("::ffff:192.0.2.1", new IPAddress(chunks[0x05]).ToString());
        }

        [Fact]
        public void PayloadRoundTripsUnitTest()
            => Assert.Equal(SIP_MESSAGE, Encoding.UTF8.GetString(Decode(GetPacket())[0x0f]));

        [Fact]
        public void TransportProtocolIsRecordedUnitTest()
        {
            Assert.Equal(17, Decode(GetPacket(protocol: SIPProtocolsEnum.udp))[0x02][0]);
            Assert.Equal(6, Decode(GetPacket(protocol: SIPProtocolsEnum.tcp))[0x02][0]);
        }

        [Fact]
        public void CaptureAgentIDRoundTripsUnitTest()
        {
            Assert.Equal(7U, ReadUInt32(Decode(GetPacket(agentID: 7))[0x0c]));
            Assert.Equal(70000U, ReadUInt32(Decode(GetPacket(agentID: 70000))[0x0c]));
        }

        [Fact]
        public void AuthenticationKeyOnlyPresentWhenSuppliedUnitTest()
        {
            Assert.False(Decode(GetPacket()).ContainsKey(0x0e));
            Assert.Equal("s3cret", Encoding.UTF8.GetString(Decode(GetPacket(password: "s3cret"))[0x0e]));
        }

        /// <summary>
        /// A SIP request carrying SDP can exceed the maximum packet size. Truncating is
        /// acceptable; leaving the payload chunk's length field describing bytes that are
        /// not in the packet is not, because the chunk then runs past the end of it.
        /// </summary>
        [Fact]
        public void OversizePayloadIsTruncatedButStillWellFramedUnitTest()
        {
            string oversize = SIP_MESSAGE + new string('x', 3000);

            var chunks = Decode(GetPacket(payload: oversize));

            string captured = Encoding.UTF8.GetString(chunks[0x0f]);
            Assert.True(captured.Length < oversize.Length, "The payload should have been truncated.");
            Assert.StartsWith(captured, oversize);
        }

        [Fact]
        public void PayloadTypeIsSipUnitTest()
            => Assert.Equal((byte)CaptureProtocolTypeEnum.SIP, Decode(GetPacket())[0x0b][0]);
    }
}
