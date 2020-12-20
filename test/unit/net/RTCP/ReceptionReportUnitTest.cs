//-----------------------------------------------------------------------------
// Filename: ReceptionReportUnitTest.cs
//
// Description: Unit tests for the ReceptionReport class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Dec 2020  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.UnitTests.Net
{
    [Trait("Category", "unit")]
    public class ReceptionReportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public ReceptionReportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Checks that the lost packet count and sequence number are correctly sampled for
        /// a single RTP packet received.
        /// </summary>
        [Fact]
        public void CheckSinglePacketReportUnitTest()
        {
            uint ssrc = 1234;

            ReceptionReport rep = new ReceptionReport(ssrc);

            var sample0 = rep.GetSample(0);

            Assert.NotNull(sample0);
            Assert.Equal(0, sample0.PacketsLost);
            Assert.Equal(0U, sample0.ExtendedHighestSequenceNumber);
        }

        /// <summary>
        /// Checks that the lost packet count and sequence number are correctly sampled for
        /// three in order RTP packets being received.
        /// </summary>
        [Fact]
        public void CheckThreePacketsReportUnitTest()
        {
            uint ssrc = 1234;
            ushort seq = 0;

            ReceptionReport rep = new ReceptionReport(ssrc);

            rep.RtpPacketReceived(seq++, 0, 0);
            rep.RtpPacketReceived(seq++, 0, 0);
            rep.RtpPacketReceived(seq++, 0, 0);

            var sample3 = rep.GetSample(0);

            Assert.NotNull(sample3);
            Assert.Equal(0, sample3.PacketsLost);
            Assert.Equal(2U, sample3.ExtendedHighestSequenceNumber);
        }
    }
}
