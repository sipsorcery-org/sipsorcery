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

using System;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorcery.UnitTests;
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

            Assert.True(SctpTransportCookie.TryParse(initAckChunk.StateCookie, out var cookie));

            logger.LogDebug("Cookie: {Cookie}", cookie);

            Assert.NotEqual(default(DateTime), cookie.CreatedAt);
            Assert.NotNull(cookie.HMAC);
            Assert.Equal(cookie.HMAC, sctpTransport.GetCookieHMAC(initAckChunk.StateCookie));
        }

        /// <summary>
        /// Tests that every field of a state cookie survives the round trip through the
        /// binary layout, including an IPv6 remote end point.
        /// </summary>
        [Fact]
        public void CookieRoundTripsThroughBinaryLayout()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var hmac = new byte[32];
            Crypto.GetRandomBytes(hmac);

            var cookie = new SctpTransportCookie
            {
                SourcePort = 5000,
                DestinationPort = 5001,
                RemoteTag = 3163987031,
                RemoteTSN = 2502329300,
                RemoteARwnd = 131072,
                RemoteEndPoint = "[fe80::1cd5:e12f:d1a3:2fa9%12]:56400",
                Tag = 987654321,
                TSN = 123456789,
                ARwnd = SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW,
                CreatedAt = new DateTime(2026, 9, 7, 15, 25, 18, DateTimeKind.Utc),
                Lifetime = 60,
                HMAC = hmac
            };

            var buffer = cookie.GetBytes();

            // 42 bytes of fixed fields, then the UTF-8 end point, then the HMAC.
            Assert.Equal(42 + Encoding.UTF8.GetByteCount(cookie.RemoteEndPoint) + 32, buffer.Length);

            Assert.True(SctpTransportCookie.TryParse(buffer, out var parsed));

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
            Assert.Equal(DateTimeKind.Utc, parsed.CreatedAt.Kind);
            Assert.Equal(cookie.Lifetime, parsed.Lifetime);
            Assert.Equal(cookie.HMAC, parsed.HMAC);
        }

        /// <summary>
        /// Tests that a cookie with no remote end point, which is the case for transports
        /// that are not carried directly over IP such as the WebRTC one, round trips.
        /// </summary>
        [Fact]
        public void CookieRoundTripsWithoutARemoteEndPoint()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var cookie = new SctpTransportCookie
            {
                SourcePort = 5000,
                DestinationPort = 5000,
                RemoteEndPoint = string.Empty,
                CreatedAt = DateTime.UtcNow,
                Lifetime = SctpTransport.DEFAULT_COOKIE_LIFETIME_SECONDS
            };

            var buffer = cookie.GetBytes();

            Assert.Equal(42 + 32, buffer.Length);
            Assert.True(SctpTransportCookie.TryParse(buffer, out var parsed));
            Assert.Equal(string.Empty, parsed.RemoteEndPoint);
            Assert.Equal(cookie.CreatedAt, parsed.CreatedAt);
        }

        /// <summary>
        /// Tests that serialising the same cookie twice produces the same bytes. The HMAC is
        /// computed over the serialised form so the layout being deterministic is load bearing
        /// rather than cosmetic.
        /// </summary>
        [Fact]
        public void CookieSerialisationIsDeterministic()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var cookie = new SctpTransportCookie
            {
                SourcePort = 5000,
                DestinationPort = 5001,
                RemoteTag = Crypto.GetRandomUInt(),
                RemoteTSN = Crypto.GetRandomUInt(),
                RemoteARwnd = 131072,
                RemoteEndPoint = "192.168.1.42:56400",
                Tag = Crypto.GetRandomUInt(),
                TSN = Crypto.GetRandomUInt(),
                ARwnd = 131072,
                CreatedAt = DateTime.UtcNow,
                Lifetime = 60
            };

            Assert.Equal(cookie.GetBytes(), cookie.GetBytes());
        }

        /// <summary>
        /// Tests that buffers that do not match the cookie layout are rejected rather than
        /// throwing. A remote peer controls these bytes.
        /// </summary>
        [Fact]
        public void CookieParseRejectsMalformedBuffers()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Assert.False(SctpTransportCookie.TryParse(null, out _));
            Assert.False(SctpTransportCookie.TryParse(Array.Empty<byte>(), out _));
            Assert.False(SctpTransportCookie.TryParse(new byte[73], out _));
            Assert.False(SctpTransportCookie.TryParse(Encoding.UTF8.GetBytes("{\"SourcePort\":5000}"), out _));

            var cookie = new SctpTransportCookie { RemoteEndPoint = "192.168.1.42:56400", CreatedAt = DateTime.UtcNow };
            var buffer = cookie.GetBytes();

            // A length prefix that disagrees with the buffer length is a malformed cookie.
            var truncated = new byte[buffer.Length - 1];
            Buffer.BlockCopy(buffer, 0, truncated, 0, truncated.Length);
            Assert.False(SctpTransportCookie.TryParse(truncated, out _));

            // So is a created at that is not a representable DateTime.
            var badCreatedAt = (byte[])buffer.Clone();
            for (int i = 28; i < 36; i++)
            {
                badCreatedAt[i] = 0xff;
            }
            Assert.False(SctpTransportCookie.TryParse(badCreatedAt, out _));
        }

        /// <summary>
        /// Tests that altering a serialised cookie invalidates its HMAC. This is what stops a
        /// remote peer manufacturing a cookie of its own, see
        /// https://tools.ietf.org/html/rfc4960#section-5.1.3.
        /// </summary>
        [Fact]
        public void CookieHMACRejectsATamperedCookie()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var sctpTransport = new MockSctpTransport();

            SctpPacket init = new SctpPacket(5000, 5000, 0);
            init.AddChunk(new SctpInitChunk(SctpChunkType.INIT, Crypto.GetRandomUInt(), Crypto.GetRandomUInt(),
                SctpAssociation.DEFAULT_ADVERTISED_RECEIVE_WINDOW, SctpAssociation.DEFAULT_NUMBER_OUTBOUND_STREAMS,
                SctpAssociation.DEFAULT_NUMBER_INBOUND_STREAMS));

            var stateCookie = (sctpTransport.GetInitAck(init).Chunks.Single() as SctpInitChunk).StateCookie;

            Assert.True(SctpTransportCookie.TryParse(stateCookie, out var cookie));
            Assert.Equal(cookie.HMAC, sctpTransport.GetCookieHMAC(stateCookie));

            // Bump the verification tag the peer would get handed back.
            stateCookie[16] ^= 0x01;

            Assert.NotEqual(cookie.HMAC, sctpTransport.GetCookieHMAC(stateCookie));
        }

        /// <summary>
        /// Tests that a COOKIE ECHO chunk with no chunk value is ignored. An SCTP chunk that is
        /// only a header leaves the chunk value null, see SctpChunk.ParseBaseChunk, so this is
        /// something any remote peer can send.
        /// </summary>
        [Fact]
        public void GetCookieIgnoresCookieEchoWithNoValue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var sctpTransport = new MockSctpTransport();

            SctpPacket cookieEcho = new SctpPacket(5000, 5000, 0);
            cookieEcho.AddChunk(new SctpChunk(SctpChunkType.COOKIE_ECHO));

            Assert.True(sctpTransport.GetCookie(cookieEcho).IsEmpty());
        }

        /// <summary>
        /// Tests that a COOKIE ECHO chunk carrying something that is not a cookie is ignored.
        /// </summary>
        [Fact]
        public void GetCookieIgnoresCookieEchoWithMalformedValue()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var sctpTransport = new MockSctpTransport();

            SctpPacket cookieEcho = new SctpPacket(5000, 5000, 0);
            cookieEcho.AddChunk(new SctpChunk(SctpChunkType.COOKIE_ECHO)
            {
                ChunkValue = Encoding.UTF8.GetBytes("not a state cookie")
            });

            Assert.True(sctpTransport.GetCookie(cookieEcho).IsEmpty());
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

        public new byte[] GetCookieHMAC(byte[] buffer)
        {
            return base.GetCookieHMAC(buffer);
        }

        public new SctpTransportCookie GetCookie(SctpPacket sctpPacket)
        {
            return base.GetCookie(sctpPacket);
        }

        public override void Send(string associationID, byte[] buffer, int offset, int length)
        { }
    }
}
