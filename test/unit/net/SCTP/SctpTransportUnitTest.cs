//-----------------------------------------------------------------------------
// Filename: SctpTransportUnitTest.cs
//
// Description: Unit tests for the SctpTransport class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 22 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
using TinyJson;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpTransportUnitTest
    {
        private readonly ILogger logger;

        public SctpTransportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests getting an INIT ACK packet in response to an INIT packet
        /// works correctly and generates a usable state cookie.
        /// </summary>
        /// <summary>
        /// Tests that the state cookie serialises to the same JSON the reflection based
        /// serialiser produced. The HMAC in GetCookieHMAC is computed over these exact
        /// bytes, so member order and formatting are load-bearing rather than cosmetic.
        /// </summary>
        [Fact]
        public void CookieSerialisesToTheSameJsonAsBefore()
        {
            logger.LogDebug("--> {method}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var cookie = new SctpTransportCookie
            {
                SourcePort = 5000,
                DestinationPort = 5001,
                RemoteTag = 3163987031,
                RemoteTSN = 2502329300,
                RemoteARwnd = 131072,
                RemoteEndPoint = "192.168.1.42:56400",
                Tag = 987654321,
                TSN = 123456789,
                ARwnd = 131072,
                CreatedAt = "2026-09-07T15:25:18.6826960-07:00",
                Lifetime = 60,
                HMAC = string.Empty
            };

            Assert.Equal(JSONWriter.ToJson(cookie), cookie.ToJson());
        }

        /// <summary>
        /// Tests that a state cookie survives a round trip. This is what fails under
        /// Native AOT with a reflection based serialiser: the members are trimmed, the
        /// cookie does not survive, and the SCTP association stalls in CookieEchoed.
        /// </summary>
        [Fact]
        public void CookieRoundTripsThroughJson()
        {
            logger.LogDebug("--> {method}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var cookie = new SctpTransportCookie
            {
                SourcePort = 5000,
                DestinationPort = 5001,
                RemoteTag = 3163987031,
                RemoteTSN = 2502329300,
                RemoteARwnd = 131072,
                RemoteEndPoint = "[fe80::1%en0]:56400",
                Tag = 987654321,
                TSN = 123456789,
                ARwnd = 131072,
                CreatedAt = "2026-09-07T15:25:18.6826960-07:00",
                Lifetime = 60,
                HMAC = "abc123"
            };

            var parsed = SctpTransportCookie.FromJson(cookie.ToJson());

            Assert.False(parsed.IsEmpty());
            Assert.Equal(cookie.SourcePort, parsed.SourcePort);
            Assert.Equal(cookie.DestinationPort, parsed.DestinationPort);
            Assert.Equal(cookie.RemoteTag, parsed.RemoteTag);
            Assert.Equal(cookie.RemoteTSN, parsed.RemoteTSN);
            Assert.Equal(cookie.RemoteARwnd, parsed.RemoteARwnd);
            Assert.Equal(cookie.RemoteEndPoint, parsed.RemoteEndPoint);
            Assert.Equal(cookie.Tag, parsed.Tag);
            Assert.Equal(cookie.TSN, parsed.TSN);
            Assert.Equal(cookie.ARwnd, parsed.ARwnd);
            Assert.Equal(cookie.CreatedAt, parsed.CreatedAt);
            Assert.Equal(cookie.Lifetime, parsed.Lifetime);
            Assert.Equal(cookie.HMAC, parsed.HMAC);
            Assert.Equal(cookie.ToJson(), parsed.ToJson());
        }

        /// <summary>
        /// Tests that a malformed cookie is an empty cookie rather than an exception. A
        /// remote peer controls these bytes, and the previous behaviour threw an
        /// ArgumentNullException inside the SCTP receive loop.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("{\"SourcePort\":50")]
        [InlineData("{}")]
        [InlineData("{\"SourcePort\":5000}")]
        public void MalformedCookieIsEmptyRatherThanAnException(string json)
        {
            logger.LogDebug("--> {method}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.True(SctpTransportCookie.FromJson(json).IsEmpty());
        }

        /// <summary>
        /// Tests that a COOKIE ECHO chunk carrying no cookie is ignored rather than
        /// throwing. This is the packet that arrives when the peer's serialiser produced
        /// nothing, and it reached Encoding.GetString(null).
        /// </summary>
        [Fact]
        public void CookieEchoWithNoCookieIsIgnored()
        {
            logger.LogDebug("--> {method}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var transport = new MockSctpTransport();
            var packet = new SctpPacket(5000, 5001, 0);
            packet.AddChunk(new SctpChunk(SctpChunkType.COOKIE_ECHO) { ChunkValue = null });

            Assert.True(transport.GetCookieForTest(packet).IsEmpty());
        }

        [Fact]
        public void GetInitAckPacket()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var sctpTransport = new MockSctpTransport();

            uint remoteTag = Crypto.GetRandomUInt();
            uint remoteTSN = Crypto.GetRandomUInt();
            uint remoteARwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW;

            SctpPacket init = new SctpPacket(5000, 5000, 0);
            SctpInitChunk initChunk = new SctpInitChunk(SctpChunkType.INIT, remoteTag, remoteTSN, remoteARwnd, 
                SctpAssociation.DEFAULT_NUMBER_OUTBOUND_STREAMS, SctpAssociation.DEFAULT_NUMBER_INBOUND_STREAMS);
            init.AddChunk(initChunk);

            var initAck = sctpTransport.GetInitAck(init);

            Assert.NotNull(initAck);

            var initAckChunk = initAck.Chunks.Single() as SctpInitChunk;

            Assert.NotNull(initAckChunk);
            Assert.NotNull(initAckChunk.StateCookie);

            var cookie = JSONParser.FromJson<SctpTransportCookie>(Encoding.UTF8.GetString(initAckChunk.StateCookie));

            logger.LogDebug("Cookie: {Cookie}", cookie.ToJson());

            Assert.NotNull(cookie.CreatedAt);
            Assert.NotNull(cookie.HMAC);
            Assert.Equal(cookie.HMAC, sctpTransport.GetCookieHMAC(initAckChunk.StateCookie));
        }
    }

    /// <summary>
    /// This mock class is used to get access to the SctpTransport protected methods.
    /// </summary>
    internal class MockSctpTransport : SctpTransport
    {
        public MockSctpTransport()
        { }

        public SctpPacket GetInitAck(SctpPacket initPacket)
        {
            return base.GetInitAck(initPacket, null);
        }

        public new string GetCookieHMAC(byte[] buffer)
        {
            return base.GetCookieHMAC(buffer);
        }

        public SctpTransportCookie GetCookieForTest(SctpPacket packet)
        {
            return base.GetCookie(packet);
        }

        public override void Send(string associationID, byte[] buffer, int offset, int length)
        { }
    }
}
