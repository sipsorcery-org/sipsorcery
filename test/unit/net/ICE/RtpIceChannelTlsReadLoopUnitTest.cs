//-----------------------------------------------------------------------------
// Filename: RtpIceChannelTlsReadLoopUnitTest.cs
//
// Description: Unit tests for the TURNS / STUNS read loop in RtpIceChannel.
//
// The loop is driven over a plain loopback stream rather than a TLS one. Nothing
// in the loop is TLS specific - the caller owns the handshake - so a socket pair
// exercises the real framing and dispatch without a certificate.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
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
    public class RtpIceChannelTlsReadLoopUnitTest
    {
        private const int TEST_TIMEOUT_SECONDS = 5;

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RtpIceChannelTlsReadLoopUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Regression test for a remotely triggerable denial of service. A packet that threw while being
        /// processed used to unwind to the loop's single catch, which exits. The finally then drops the TLS
        /// stream and nothing restarts a loop that was started fire and forget, so relayed media and ICE
        /// connectivity on that leg stopped for the rest of the session. The loop must drop the offending
        /// packet and go on reading, the way the other ICE receive loops do. See GHSA-6848-qmp4-652w.
        /// </summary>
        [Fact]
        public async Task TlsReadLoopDropsAThrowingPacketAndKeepsReading()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var acceptTask = listener.AcceptTcpClientAsync();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

            using var server = await acceptTask;

            var channel = new ThrowOnFirstPacketIceChannel();
            var uri = new STUNUri(STUNSchemesEnum.turns, IPAddress.Loopback.ToString(), 5349);

            try
            {
                var readLoop = channel.StartTlsReadLoop(uri, client.GetStream(), new IPEndPoint(IPAddress.Loopback, 5349));

                // A binding request with no attributes is exactly the 20 bytes of a STUN header, and its
                // body length of zero frames it as one packet, so two of them are two dispatches.
                byte[] packet = new STUNMessage(STUNMessageTypesEnum.BindingRequest).ToByteBuffer(null, false);

                Assert.Equal(STUNHeader.STUN_HEADER_LENGTH, packet.Length);

                var serverStream = server.GetStream();
                await serverStream.WriteAsync(packet, 0, packet.Length);
                await serverStream.WriteAsync(packet, 0, packet.Length);
                await serverStream.FlushAsync();

                bool gotSecondPacket = channel.SecondPacketReceived.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

                Assert.True(gotSecondPacket, "The TLS read loop stopped after a packet threw while being processed.");

                // Closing the channel and the stream is what ends the loop normally.
                channel.Close("normal");
                client.Close();

                await Task.WhenAny(readLoop, Task.Delay(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS)));
            }
            finally
            {
                channel.Close("normal");
                listener.Stop();
            }

            logger.LogDebug("Test complete.");
        }

        /// <summary>
        /// Stands in for any packet handler failure. The first packet throws the exception the advisory's
        /// short relayed payload produced; the second records that the loop was still reading.
        /// </summary>
        private sealed class ThrowOnFirstPacketIceChannel : RtpIceChannel
        {
            private int _packetCount;

            public readonly ManualResetEventSlim SecondPacketReceived = new ManualResetEventSlim(false);

            protected override void OnRTPPacketReceived(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet)
            {
                if (Interlocked.Increment(ref _packetCount) == 1)
                {
                    throw new NullReferenceException("Object reference not set to an instance of an object.");
                }

                SecondPacketReceived.Set();
            }
        }
    }
}
