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
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
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

            RTPChannel rtpChannel = rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio);

            logger.LogDebug($"RTP channel RTP socket local end point {rtpChannel.RTPLocalEndPoint}.");

            var iceSession = new IceSession(rtpChannel, RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);

            foreach (var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug(hostCandidate.ToString());
            }
        }

        /// <summary>
        /// Tests that creating a new IceSession instance and requesting the host candidates works correctly
        /// when the RTP channel was bound to a single IP address.
        /// </summary>
        [Fact]
        public void GetHostCandidatesForRTPBindUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.InternetDefaultAddress;
            RTPSession rtpSession = new RTPSession(true, true, true, localAddress);

            // Add a track to the session in order to initialise the RTPChannel.
            MediaStreamTrack dummyTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            rtpSession.addTrack(dummyTrack);

            RTPChannel rtpChannel = rtpSession.GetRtpChannel(SDPMediaTypesEnum.audio);

            logger.LogDebug($"RTP channel RTP socket local end point {rtpChannel.RTPLocalEndPoint}.");

            var iceSession = new IceSession(rtpChannel, RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);
            Assert.True(localAddress.Equals(IPAddress.Parse(iceSession.Candidates.Single().address)));

            foreach (var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug(hostCandidate.ToString());
            }
        }

        /// <summary>
        /// Tests that once remote candidates are added to the ICE session the checklist stays
        /// in priority sorted order.
        /// </summary>
        [Fact]
        public void SortChecklistUnitTest()
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

            var remoteCandidate2 = RTCIceCandidate.Parse("candidate:408132417 1 udp 2113937150 192.168.11.51 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate2);

            foreach (var entry in iceSession._checklist)
            {
                logger.LogDebug($"checklist entry priority {entry.Priority}.");
            }
        }

        /// <summary>
        /// Tests that checklist entries get added correctly and duplicates are excluded.
        /// </summary>
        [Fact]
        public void ChecklistConstructionUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel rtpChannel = new RTPChannel(false, null);

            var iceSession = new IceSession(rtpChannel, RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);

            foreach (var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug($"host candidate: {hostCandidate}");
            }

            var remoteCandidate = RTCIceCandidate.Parse("candidate:408132416 1 udp 2113937151 192.168.11.50 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate);

            var remoteCandidate2 = RTCIceCandidate.Parse("candidate:408132417 1 udp 2113937150 192.168.11.50 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate2);

            foreach (var entry in iceSession._checklist)
            {
                logger.LogDebug($"checklist entry: {entry.LocalCandidate} -> {entry.RemoteCandidate}");
            }

            Assert.Single(iceSession._checklist);
        }

        /// <summary>
        /// Tests that checklist gets processed and the status of the entry's gets updated as expected.
        /// </summary>
        [Fact]
        public async void ChecklistProcessingUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel rtpChannel = new RTPChannel(false, null);

            var iceSession = new IceSession(rtpChannel, RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);

            foreach (var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug($"host candidate: {hostCandidate}");
            }

            var remoteCandidate = RTCIceCandidate.Parse("candidate:408132416 1 udp 2113937151 192.168.11.50 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate);

            iceSession.SetRemoteCredentials("CI7o", "xxxxxxxxxxxx");
            iceSession.StartGathering();

            await Task.Delay(1000);

            Assert.Equal(IceSession.ChecklistEntryState.InProgress, iceSession._checklist.Single().State);
        }

        /// <summary>
        /// Tests that checklist gets processed and an entry that gets no response ends up in the failed state.
        /// </summary>
        [Fact]
        public async void ChecklistProcessingToFailStateUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPChannel rtpChannel = new RTPChannel(false, null);

            var iceSession = new IceSession(rtpChannel, RTCIceComponent.rtp);

            Assert.NotNull(iceSession);
            Assert.NotEmpty(iceSession.Candidates);

            foreach (var hostCandidate in iceSession.Candidates)
            {
                logger.LogDebug($"host candidate: {hostCandidate}");
            }

            var remoteCandidate = RTCIceCandidate.Parse("candidate:408132416 1 udp 2113937151 192.168.11.50 51268 typ host generation 0 ufrag CI7o network-cost 999");
            iceSession.AddRemoteCandidate(remoteCandidate);

            iceSession.SetRemoteCredentials("CI7o", "xxxxxxxxxxxx");
            iceSession.StartGathering();

            logger.LogDebug($"ICE session retry interval {iceSession.RTO}ms.");

            // The defaults are 5 STUN requests and for a checklist with one entry they will be 500ms apart.
            await Task.Delay(4000);

            Assert.Equal(IceSession.ChecklistEntryState.Failed, iceSession._checklist.Single().State);
            Assert.Equal(IceSession.ChecklistState.Failed, iceSession._checklistState);
            Assert.Equal(RTCIceConnectionState.failed, iceSession.ConnectionState);
        }
    }
}
