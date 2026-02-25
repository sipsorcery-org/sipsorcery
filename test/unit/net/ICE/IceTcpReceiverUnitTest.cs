//-----------------------------------------------------------------------------
// Filename: IceTcpReceiverUnitTest.cs
//
// Description: Characterization tests for the STUN-over-TCP framing in
// IceTcpReceiver.ProcessRawBuffer. ICE over TCP delivers a byte stream rather
// than datagrams, so the receiver has to split the stream back into individual
// STUN messages, buffering partial messages across reads. These tests pin that
// framing (single/back-to-back/fragmented/partial-header messages and the
// header-length boundary) ahead of any refactor of the receive path.
//
// The framing is exercised via a small test subclass that drives the protected
// ProcessRawBuffer directly (no live socket traffic), so no production change is
// required.
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Jun 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class IceTcpReceiverUnitTest
    {
        private readonly Microsoft.Extensions.Logging.ILogger logger;

        public IceTcpReceiverUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Test harness that feeds raw bytes through the protected framing method exactly the way the real
        /// socket receive loop does (appending at the cached offset and passing the total byte count).
        /// </summary>
        private sealed class TestableIceTcpReceiver : IceTcpReceiver
        {
            public TestableIceTcpReceiver(Socket socket) : base(socket) { }

            public int Feed(byte[] data)
            {
                Buffer.BlockCopy(data, 0, m_recvBuffer, m_recvOffset, data.Length);
                return ProcessRawBuffer(data.Length + m_recvOffset, new IPEndPoint(IPAddress.Loopback, 9));
            }

            public int CachedOffset => m_recvOffset;
        }

        private static TestableIceTcpReceiver CreateReceiver(out List<byte[]> packets, out Socket socket)
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var receiver = new TestableIceTcpReceiver(socket);
            var captured = new List<byte[]>();
            receiver.OnPacketReceived += (r, port, ep, pkt) => captured.Add(pkt);
            packets = captured;
            return receiver;
        }

        // A STUN binding request with a username attribute - total length is > STUN_HEADER_LENGTH so the
        // framing loop will extract it.
        private static byte[] StunMessage(string username = "user1234")
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            msg.AddUsernameAttribute(username);
            return msg.ToByteBuffer(null, false);
        }

        // A bare STUN header (no attributes) - exactly STUN_HEADER_LENGTH bytes.
        private static byte[] StunHeaderOnly() =>
            new STUNMessage(STUNMessageTypesEnum.BindingRequest).ToByteBuffer(null, false);

        private static byte[] Slice(byte[] src, int start, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(src, start, result, 0, count);
            return result;
        }

        [Fact]
        public void SingleCompleteMessage_ExtractedOnce()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var receiver = CreateReceiver(out var packets, out var socket);
            try
            {
                var msg = StunMessage();
                Assert.True(msg.Length > STUNHeader.STUN_HEADER_LENGTH);

                var extracted = receiver.Feed(msg);

                Assert.Equal(1, extracted);
                Assert.Single(packets);
                Assert.Equal(msg, packets[0]);
                Assert.Equal(0, receiver.CachedOffset);   // nothing left buffered.
            }
            finally { socket.Close(); }
        }

        [Fact]
        public void TwoBackToBackMessages_ExtractedTwice()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var receiver = CreateReceiver(out var packets, out var socket);
            try
            {
                var a = StunMessage("aaaa");
                var b = StunMessage("bbbb");
                var combined = a.Concat(b).ToArray();

                var extracted = receiver.Feed(combined);

                Assert.Equal(2, extracted);
                Assert.Equal(2, packets.Count);
                Assert.Equal(a, packets[0]);
                Assert.Equal(b, packets[1]);
                Assert.Equal(0, receiver.CachedOffset);
            }
            finally { socket.Close(); }
        }

        [Fact]
        public void FragmentedMessage_ReassembledAcrossReads()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var receiver = CreateReceiver(out var packets, out var socket);
            try
            {
                var msg = StunMessage("fragmented");
                // Split partway through the body (past the header so the header parses but the message is incomplete).
                var split = STUNHeader.STUN_HEADER_LENGTH + 2;

                var firstExtract = receiver.Feed(Slice(msg, 0, split));
                Assert.Equal(0, firstExtract);                 // incomplete - nothing extracted yet.
                Assert.Empty(packets);
                Assert.Equal(split, receiver.CachedOffset);    // remembered for the next read.

                var secondExtract = receiver.Feed(Slice(msg, split, msg.Length - split));
                Assert.Equal(1, secondExtract);
                Assert.Single(packets);
                Assert.Equal(msg, packets[0]);                 // reassembled correctly.
                Assert.Equal(0, receiver.CachedOffset);
            }
            finally { socket.Close(); }
        }

        [Fact]
        public void PartialHeader_CachedThenCompleted()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var receiver = CreateReceiver(out var packets, out var socket);
            try
            {
                var msg = StunMessage();

                var firstExtract = receiver.Feed(Slice(msg, 0, 10));   // less than a full header.
                Assert.Equal(0, firstExtract);
                Assert.Empty(packets);
                Assert.Equal(10, receiver.CachedOffset);

                var secondExtract = receiver.Feed(Slice(msg, 10, msg.Length - 10));
                Assert.Equal(1, secondExtract);
                Assert.Single(packets);
                Assert.Equal(msg, packets[0]);
            }
            finally { socket.Close(); }
        }

        /// <summary>
        /// Characterizes the current header-length boundary: the framing loop uses
        /// "Count &gt; STUN_HEADER_LENGTH", so a bare header-only (20 byte, zero-attribute) STUN message is
        /// NOT extracted - it is held as a fragment. This pins current behaviour; changing the comparison to
        /// "&gt;=" would extract it and this test would flag the change.
        /// </summary>
        [Fact]
        public void HeaderOnlyMessage_IsNotExtracted_CurrentBehaviour()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var receiver = CreateReceiver(out var packets, out var socket);
            try
            {
                var msg = StunHeaderOnly();
                Assert.Equal(STUNHeader.STUN_HEADER_LENGTH, msg.Length);

                var extracted = receiver.Feed(msg);

                Assert.Equal(0, extracted);
                Assert.Empty(packets);
            }
            finally { socket.Close(); }
        }

        [Fact]
        public void NonStunData_IsNotExtracted()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var receiver = CreateReceiver(out var packets, out var socket);
            try
            {
                var garbage = new byte[40];
                for (var i = 0; i < garbage.Length; i++) { garbage[i] = 0xEE; }

                var extracted = receiver.Feed(garbage);

                Assert.Equal(0, extracted);
                Assert.Empty(packets);
            }
            finally { socket.Close(); }
        }
    }
}
