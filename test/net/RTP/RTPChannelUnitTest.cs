//-----------------------------------------------------------------------------
// Filename: RTPSessionUnitTest.cs
//
// Description: Unit tests for the RTPSession class.
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
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
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
        /// Tests that two RTP channels can communicate.
        /// </summary>
        [Fact]
        public void RtpChannelLoopbackUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel channel1 = new RTPChannel(false);

            bool testResult = false;
            ManualResetEventSlim testCompleteEvent = new ManualResetEventSlim(false);

            RTPChannel channel2 = new RTPChannel(false);
            channel2.OnRTPDataReceived += (lep, rep, pkt) =>
            {
                logger.LogDebug($"RTP data receive packet length {pkt.Length}.");
                testResult = true;
                testCompleteEvent.Set();
            };

            channel1.Start();
            channel2.Start();

            IPEndPoint channel2Dst = new IPEndPoint(IPAddress.Loopback, channel2.RTPPort);

            logger.LogDebug($"Attempting to send packet from {channel1.RTPLocalEndPoint} to {channel2Dst}.");

            var sendResult = channel1.SendAsync(RTPChannelSocketsEnum.RTP, channel2Dst, new byte[] { 0x00 });

            logger.LogDebug($"Send result {sendResult}.");

            testCompleteEvent.Wait(TimeSpan.FromSeconds(TEST_TIMEOUT_SECONDS));

            channel1.Close("normal");
            channel2.Close("normal");

            Assert.True(testResult);
        }
    }
}
