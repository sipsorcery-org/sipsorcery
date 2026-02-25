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
using System.Threading;
using System.Threading.Tasks;
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

        /// <summary>
        /// A throw from the packet handler (e.g. a malformed TURN data indication) must not escape the
        /// framing loop. If it did it would unwind into EndReceiveFrom, close the receiver and permanently
        /// kill the ICE-over-TCP path for the session. The bad packet is dropped, the framing state stays
        /// consistent and the next message on the stream is still delivered.
        /// </summary>
        [Fact]
        public void ThrowingPacketHandler_DoesNotBreakFramingOrCloseReceiver()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

            try
            {
                var receiver = new TestableIceTcpReceiver(socket);
                var delivered = new List<byte[]>();
                receiver.OnPacketReceived += (r, port, ep, pkt) =>
                {
                    delivered.Add(pkt);
                    if (delivered.Count == 1)
                    {
                        throw new NullReferenceException("Simulated malformed packet.");
                    }
                };

                var bad = StunMessage("bad");
                var good = StunMessage("good");

                int extracted = receiver.Feed(bad.Concat(good).ToArray());

                Assert.Equal(2, extracted);
                Assert.Equal(2, delivered.Count);         // the throw did not abort the framing loop.
                Assert.Equal(good, delivered[1]);
                Assert.Equal(0, receiver.CachedOffset);   // fragmentation bookkeeping still ran.
                Assert.False(receiver.IsClosed);          // receiver survives a bad packet.
            }
            finally { socket.Close(); }
        }

        /// <summary>
        /// A zero byte receive on a stream socket is the end of stream indication, not an empty datagram.
        /// Re-arming on it recursed (the re-arm completes synchronously with zero bytes and calls straight
        /// back into EndReceiveFrom) until the process died with a StackOverflowException, which is not
        /// catchable. Any TURN over TCP server closing the connection gracefully - idle timeout, restart -
        /// was enough to trigger it. The receiver must instead go idle, staying open so that the reconnect
        /// in RtpIceChannel.SendOverTCP can resume it.
        ///
        /// The harness caps the re-arm count rather than asserting on it after the fact, so a regression
        /// fails this test instead of taking the test host down with it.
        /// </summary>
        [Fact]
        public async Task RemoteGracefulClose_StopsReArmingWithoutClosing()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            const int REARM_CAP = 50;

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket server = null;

            try
            {
                client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                client.Connect((IPEndPoint)listener.LocalEndpoint);
                server = listener.AcceptSocket();

                var receiver = new CappedReceiver(client, REARM_CAP);
                receiver.BeginReceiveFrom();
                await Task.Delay(100);

                // Graceful close from the far end - the receive completes with zero bytes.
                server.Shutdown(SocketShutdown.Both);
                server.Close();
                server = null;
                await Task.Delay(500);

                Assert.True(receiver.ReArmCount < REARM_CAP, $"The receive loop re-armed {receiver.ReArmCount} times after the remote closed; it is spinning.");
                Assert.False(receiver.IsRunningReceive);   // gone idle.
                Assert.False(receiver.IsClosed);           // but still open so a reconnect can resume it.
                Assert.True(receiver.IsEndOfStream);       // and the reason is visible to the send path.

                // Socket.Connected is why the send path cannot work this out for itself. It still reports
                // true even though the remote end has gone, which is what left SendOverTCP unable to tell a
                // half closed connection from a live one.
                Assert.True(client.Connected);
            }
            finally
            {
                server?.Close();
                client.Close();
                listener.Stop();
            }
        }

        /// <summary>
        /// Pins the platform behaviour the end of stream handling is built on: a connected socket cannot be
        /// reused. Socket.Connect throws InvalidOperationException after Socket.Disconnect whatever endpoint
        /// is passed, so there is no recovering the connection in place and RtpIceChannel.SendOverTCP has to
        /// report the failure rather than attempt a reconnect. If a future runtime relaxes this, this test
        /// flags it and the send path could be revisited.
        /// </summary>
        [Fact]
        public void DisconnectedSocket_CannotBeReconnectedInPlace()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var other = new TcpListener(IPAddress.Loopback, 0);
            other.Start();

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                socket.Connect((IPEndPoint)listener.LocalEndpoint);
                listener.AcceptSocket().Close();

                socket.Disconnect(true);   // reuseSocket: true, which still does not permit a sync reconnect.

                Assert.Throws<InvalidOperationException>(() => socket.Connect((IPEndPoint)listener.LocalEndpoint));
                Assert.Throws<InvalidOperationException>(() => socket.Connect((IPEndPoint)other.LocalEndpoint));
            }
            finally
            {
                socket.Close();
                listener.Stop();
                other.Stop();
            }
        }

        /// <summary>
        /// Counts re-arms and refuses to issue any past the cap, so an unbounded re-arm loop is contained
        /// instead of overflowing the stack.
        /// </summary>
        private sealed class CappedReceiver : IceTcpReceiver
        {
            private readonly int _cap;
            private int _reArmCount;

            public CappedReceiver(Socket socket, int cap) : base(socket) { _cap = cap; }

            public int ReArmCount => _reArmCount;

            public override void BeginReceiveFrom()
            {
                if (Interlocked.Increment(ref _reArmCount) > _cap)
                {
                    return;
                }

                base.BeginReceiveFrom();
            }
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
