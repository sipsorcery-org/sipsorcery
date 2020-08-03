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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
        public async void RtpChannelLoopbackUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel channel1 = new RTPChannel(false, null);

            bool testResult = false;
            ManualResetEventSlim testCompleteEvent = new ManualResetEventSlim(false);

            RTPChannel channel2 = new RTPChannel(false, null);
            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug($"RTP data receive packet length {pkt.Length}.");
                testResult = true;
                testCompleteEvent.Set();
            };

            channel1.Start();
            channel2.Start();

            // Give the socket receive tasks time to fire up.
            await Task.Delay(1000);

            IPAddress channel2Address = (channel2.RTPLocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Loopback : IPAddress.Loopback;
            IPEndPoint channel2Dst = new IPEndPoint(channel2Address, channel2.RTPPort);

            logger.LogDebug($"Attempting to send packet from {channel1.RTPLocalEndPoint} to {channel2Dst}.");

            var sendResult = await channel1.SendAsync(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x00 }).ConfigureAwait(false);

            logger.LogDebug($"Send result {sendResult}.");

            testCompleteEvent.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

            Assert.True(testResult);

            channel1.Close("normal");
            channel2.Close("normal");

            logger.LogDebug($"Test complete.");
        }

        /// <summary>
        /// Tests that two RTP channels can communicate when they are instantiated with a
        /// specific IPv4 bind address.
        /// </summary>
        [Fact]
        public async void RtpChannelWithIPv4BindAddressLoopbackUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel channel1 = new RTPChannel(false, IPAddress.Loopback);

            bool testResult = false;
            ManualResetEventSlim testCompleteEvent = new ManualResetEventSlim(false);

            RTPChannel channel2 = new RTPChannel(false, IPAddress.Loopback);
            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug($"RTP data receive packet length {pkt.Length}.");
                testResult = true;
                testCompleteEvent.Set();
            };

            channel1.Start();
            channel2.Start();

            // Give the socket receive tasks time to fire up.
            await Task.Delay(1000);

            IPEndPoint channel2Dst = new IPEndPoint(IPAddress.Loopback, channel2.RTPPort);

            logger.LogDebug($"Attempting to send packet from {channel1.RTPLocalEndPoint} to {channel2Dst}.");

            var sendResult = await channel1.SendAsync(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x00 }).ConfigureAwait(false);

            logger.LogDebug($"Send result {sendResult}.");

            testCompleteEvent.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

            Assert.True(testResult);

            channel1.Close("normal");
            channel2.Close("normal");

            logger.LogDebug($"Test complete.");
        }

        /// <summary>
        /// Tests that two RTP channels can communicate when they are instantiated with a
        /// specific IPv6 bind address.
        /// </summary>
        [Fact]
        public async void RtpChannelWithIPv6BindAddressLoopbackUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel channel1 = new RTPChannel(false, IPAddress.IPv6Loopback);

            bool testResult = false;
            ManualResetEventSlim testCompleteEvent = new ManualResetEventSlim(false);

            RTPChannel channel2 = new RTPChannel(false, IPAddress.IPv6Loopback);
            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug($"RTP data receive packet length {pkt.Length}.");
                testResult = true;
                testCompleteEvent.Set();
            };

            channel1.Start();
            channel2.Start();

            // Give the socket receive tasks time to fire up.
            await Task.Delay(1000);

            IPEndPoint channel2Dst = new IPEndPoint(IPAddress.IPv6Loopback, channel2.RTPPort);

            logger.LogDebug($"Attempting to send packet from {channel1.RTPLocalEndPoint} to {channel2Dst}.");

            var sendResult = await channel1.SendAsync(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x00 }).ConfigureAwait(false);

            logger.LogDebug($"Send result {sendResult}.");

            testCompleteEvent.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

            Assert.True(testResult);

            channel1.Close("normal");
            channel2.Close("normal");

            logger.LogDebug($"Test complete.");
        }
    }
}
