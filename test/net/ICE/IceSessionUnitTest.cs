//-----------------------------------------------------------------------------
// Filename: IceSessionUnitTest.cs
//
// Description: Unit tests for the IceSession class.
//
// History:
// 21 Mar 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class IceSessionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public IceSessionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that creating a new IceSession instance works correctly.
        /// </summary>
        [Fact]
        public void CreateInstanceUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession rtpSession = new RTPSession(true, true, true);
            
            // Add a track to the session in order to initialise the RTPChannel.
            MediaStreamTrack dummyTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            rtpSession.addTrack(dummyTrack);

            var iceSession = new IceSession(rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio));

            Assert.NotNull(iceSession);
        }

        /// <summary>
        /// Tests that creating a new IceSession instance and requesting the host candidates works correctly.
        /// </summary>
        [Fact]
        public void GetHostCandidatesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession rtpSession = new RTPSession(true, true, true);

            // Add a track to the session in order to initialise the RTPChannel.
            MediaStreamTrack dummyTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            rtpSession.addTrack(dummyTrack);

            var iceSession = new IceSession(rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio));

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.HostCandidates);

            foreach(var hostCandidate in iceSession.HostCandidates)
            {
                logger.LogDebug(hostCandidate.ToString());
            }
        }
    }
}
