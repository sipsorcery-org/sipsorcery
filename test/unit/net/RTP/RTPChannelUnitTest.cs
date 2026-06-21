//-----------------------------------------------------------------------------
// Filename: RTPChannelUnitTest.cs
//
// Description: Unit tests for the RTPChannel class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    public class RTPChannelUnitTest
    {
        private const int TEST_TIMEOUT_SECONDS = 2;

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTPChannelUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that multiple RTP channels can be created during a short space of time.
        /// </summary>
        [Fact]
        public void RtpChannelCreateManyUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            List<RTPChannel> channels = new List<RTPChannel>();

            for (int i = 0; i < 10; i++)
            {
                RTPChannel channel = new RTPChannel(true, null);
                channels.Add(channel);
            }

            Assert.Equal(10, channels.Count);

            foreach (var channel in channels)
            {
                channel.Close("normal");
            }
        }

        /// <summary>
        /// Tests that two RTP channels can communicate.
        /// </summary>
        [Fact]
        public async Task RtpChannelLoopbackUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTPChannel channel1 = new RTPChannel(false, null);

            bool testResult = false;
            ManualResetEventSlim testCompleteEvent = new ManualResetEventSlim(false);

            RTPChannel channel2 = new RTPChannel(false, null);
            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug("RTP data receive packet length {Length}.", pkt.Length);
                testResult = true;
                testCompleteEvent.Set();
            };

            channel1.Start();
            channel2.Start();

            // Give the socket receive tasks time to fire up.
            await Task.Delay(1000);

            IPAddress channel2Address = (channel2.RTPLocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Loopback : IPAddress.Loopback;
            IPEndPoint channel2Dst = new IPEndPoint(channel2Address, channel2.RTPPort);

            logger.LogDebug("Attempting to send packet from {LocalEndPoint} to {RemoteEndPoint}.", channel1.RTPLocalEndPoint, channel2Dst);

            // 12 byte packet (RTP minimum header length) starting 0x02 (0x00 & 0x01 are STUN packets).
            var sendResult = channel1.Send(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            logger.LogDebug("Send result {SendResult}.", sendResult);

            testCompleteEvent.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

            Assert.True(testResult);

            channel1.Close("normal");
            channel2.Close("normal");

            logger.LogDebug("Test complete.");
        }

        /// <summary>
        /// Tests that two RTP channels can communicate when they are instantiated with a
        /// specific IPv4 bind address.
        /// </summary>
        [Fact]
        public async Task RtpChannelWithIPv4BindAddressLoopbackUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTPChannel channel1 = new RTPChannel(false, IPAddress.Loopback);
            RTPChannel channel2 = new RTPChannel(false, IPAddress.Loopback);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug("RTP data receive packet length {Length}.", pkt.Length);
                tcs.TrySetResult(true);
            };

            channel1.Start();
            channel2.Start();

            // Give the socket receive tasks time to fire up.
            await Task.Delay(1000);

            IPEndPoint channel2Dst = new IPEndPoint(IPAddress.Loopback, channel2.RTPPort);

            logger.LogDebug("Attempting to send packet from {LocalEndPoint} to {RemoteEndPoint}.", channel1.RTPLocalEndPoint, channel2Dst);

            // 12 byte packet (RTP minimum header length) starting 0x02 (0x00 & 0x01 are STUN packets).
            var sendResult = channel1.Send(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            logger.LogDebug("Send result {SendResult}.", sendResult);

            // Wait for receive or timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);

            channel1.Close("normal");
            channel2.Close("normal");

            logger.LogDebug("Test complete.");

            // Assert.
            if (completed == timeoutTask)
            {
                Assert.Fail($"RTP packet not received within {TEST_TIMEOUT_SECONDS} seconds.");
            }

            Assert.True(await tcs.Task);
        }

        /// <summary>
        /// Tests that two RTP channels can communicate when they are instantiated with a
        /// specific IPv6 bind address.
        /// </summary>
        [Fact]
        public async Task RtpChannelWithIPv6BindAddressLoopbackUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTPChannel channel1 = new RTPChannel(false, IPAddress.IPv6Loopback);

            bool testResult = false;
            ManualResetEventSlim testCompleteEvent = new ManualResetEventSlim(false);

            RTPChannel channel2 = new RTPChannel(false, IPAddress.IPv6Loopback);
            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug("RTP data receive packet length {Length}.", pkt.Length);
                testResult = true;
                testCompleteEvent.Set();
            };

            channel1.Start();
            channel2.Start();

            // Give the socket receive tasks time to fire up.
            await Task.Delay(1000);

            IPEndPoint channel2Dst = new IPEndPoint(IPAddress.IPv6Loopback, channel2.RTPPort);

            logger.LogDebug("Attempting to send packet from {LocalEndPoint} to {RemoteEndPoint}.", channel1.RTPLocalEndPoint, channel2Dst);

            // 12 byte packet (RTP minimum header length) starting 0x02 (0x00 & 0x01 are STUN packets).
            var sendResult = channel1.Send(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

            logger.LogDebug("Send result {SendResult}.", sendResult);

            testCompleteEvent.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

            Assert.True(testResult);

            channel1.Close("normal");
            channel2.Close("normal");

            logger.LogDebug("Test complete.");
        }

        /// <summary>
        /// Regression test for a remotely exploitable denial-of-service issue. A single malformed inbound
        /// UDP packet (here a 1 byte packet) used to throw in the packet receive handler which the UDP
        /// receive loop converted into a channel Close, terminating the media session. The receive loop
        /// must now drop the offending packet and keep the channel open.
        /// </summary>
        [Fact]
        public async Task RtpChannelMalformedPacketDoesNotCloseChannelUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTPChannel channel = new RTPChannel(false, IPAddress.Loopback);

            string closedReason = null;
            channel.OnClosed += reason => closedReason = reason;

            channel.Start();

            // Give the socket receive task time to fire up.
            await Task.Delay(1000);

            using (var attacker = new UdpClient())
            {
                // A 1 byte 0x00 packet. The old handler read packet[1] after only checking the packet was
                // non-empty, throwing IndexOutOfRangeException which tore the channel down.
                attacker.Send(new byte[] { 0x00 }, 1, new IPEndPoint(IPAddress.Loopback, channel.RTPPort));
            }

            // Allow time for the packet to be received and (previously) close the channel.
            await Task.Delay(1000);

            Assert.False(channel.IsClosed, $"The RTP channel was closed by a malformed packet. Reason: {closedReason ?? "(none)"}.");

            // The channel must still be usable - confirm it can receive a subsequent valid RTP packet.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            channel.OnRTPDataReceived += (lep, rep, pkt) => tcs.TrySetResult(true);

            using (var sender = new UdpClient())
            {
                // A 12 byte packet (RTP minimum header length) starting 0x02 so it is not treated as STUN.
                sender.Send(new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 12, new IPEndPoint(IPAddress.Loopback, channel.RTPPort));
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS)));

            channel.Close("normal");

            Assert.True(completed == tcs.Task && await tcs.Task, "The RTP channel did not receive a valid packet after a malformed packet was dropped.");

            logger.LogDebug("Test complete.");
        }
    }
}
