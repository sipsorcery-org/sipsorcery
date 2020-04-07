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

            var iceSession = new IceSession(rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio), RTCIceComponent.rtp);

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

            var iceSession = new IceSession(rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio), RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);

            foreach(var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug(hostCandidate.ToString());
            }
        }

        /// <summary>
        /// Tests that once remote candidates are added to the ICE session the checklist stays
        /// in priority sorted order.
        /// </summary>
        [Fact]
        public void SortChecklitUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession rtpSession = new RTPSession(true, true, true);
            MediaStreamTrack dummyTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            rtpSession.addTrack(dummyTrack);

            var iceSession = new IceSession(rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio), RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);

            foreach (var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug(hostCandidate.ToString());
            }

            var remoteCandidate = RTCIceCandidate.Parse("candidate:408132416 1 udp 2113937151 192.168.11.50 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate);

            var remoteCandidate2 = RTCIceCandidate.Parse("candidate:408132417 1 udp 2113937150 192.168.11.50 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate2);

            foreach (var entry in iceSession._checklist)
            {
                logger.LogDebug($"checklist entry priority {entry.Priority}.");
            }
        }
    }
}
