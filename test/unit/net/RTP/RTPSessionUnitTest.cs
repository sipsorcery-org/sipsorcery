﻿//-----------------------------------------------------------------------------
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
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTPSessionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Checks that the RTP media state gets set correctly when:
        ///  - Audio only,
        ///  - Offer was generated locally,
        ///  - Remote party provided the answer.
        /// </summary>
        [Fact]
        public void AudioOnlyOfferAnswerTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            // Generate the offer to send to the remote party.
            var offer = localSession.CreateOffer(IPAddress.Loopback);

            logger.LogDebug("Local offer: " + offer.ToString());

            RTPSession remoteSession = new RTPSession(false, false, false);
            // The track for the track for the remote session is still local relative to the session it's being added to.
            MediaStreamTrack remoteAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            remoteSession.addTrack(remoteAudioTrack);

            var result = remoteSession.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on remote session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // Get the answer from the remote session.
            var answer = remoteSession.CreateAnswer(IPAddress.Loopback);

            logger.LogDebug("Remote answer: " + offer.ToString());

            // Provide the answer back to the local session.
            result = localSession.SetRemoteDescription(SIP.App.SdpType.answer, answer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            localSession.Close("normal");
            remoteSession.Close("normal");
        }

        /// <summary>
        /// Checks that setting the remote description returns the correct error status when
        /// an attempt is made to the remote description on a session with no tracks.
        /// </summary>
        [Fact]
        public void NoLocalTracksTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            // A local session is created but NO media tracks are added to it.
            RTPSession localSession = new RTPSession(false, false, false);

            // Create a remote session WITH an audio track.
            RTPSession remoteSession = new RTPSession(false, false, false);
            // The track for the track for the remote session is still local relative to the session it's being added to.
            MediaStreamTrack remoteAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            remoteSession.addTrack(remoteAudioTrack);

            var offer = remoteSession.CreateOffer(IPAddress.Loopback);

            // Give the offer to the local session that is missing any media tracks.
            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.NoMatchingMediaType, result);

            localSession.Close("normal");
            remoteSession.Close("normal");
        }

        /// <summary>
        /// Checks that the correct failure condition is returned when a remote description is provided
        /// with no media announcements.
        /// </summary>
        [Fact]
        public void NoRemoteMediaTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            var remoteOffer = new SDP();

            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, remoteOffer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.NoRemoteMedia, result);

            localSession.Close("normal");
        }

        /// <summary>
        /// Checks that the correct failure condition is returned when the local session has a single
        /// media track which is a different type to a single remote media announcement.
        /// </summary>
        [Fact]
        public void NoMatchingMediaTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            RTPSession remoteSession = new RTPSession(false, false, false);
            // The track for the track for the remote session is still local relative to the session it's being added to.
            MediaStreamTrack remoteVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            remoteSession.addTrack(remoteVideoTrack);

            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, remoteSession.CreateOffer(IPAddress.Loopback));

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.NoMatchingMediaType, result);

            localSession.Close("normal");
            remoteSession.Close("normal");
        }

        /// <summary>
        /// Checks that the correct failure condition is returned when a remote description is provided
        /// with an invalid connection port.
        /// </summary>
        [Fact]
        public void InvalidPortInRemoteOfferTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            var remoteOffer = new SDP();
            remoteOffer.SessionId = Crypto.GetRandomInt(5).ToString();

            remoteOffer.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
                66000,
                new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTPSession.RTP_MEDIA_PROFILE;

            remoteOffer.Media.Add(audioAnnouncement);

            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, remoteOffer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.InvalidAudioPort, result);

            localSession.Close("normal");
        }

        /// <summary>
        /// Checks that the SDP offer gets created with the correct connection address when the RTPSession
        /// is instantiated with a specific IPv4 bind address.
        /// </summary>
        [Fact]
        public void CheckCreateOfferWithIPv4BindAddressAnswerTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            RTPSession localSession = new RTPSession(false, false, false, IPAddress.Loopback);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            // Generate the offer to send to the remote party.
            var offer = localSession.CreateOffer(null);

            logger.LogDebug("Local offer: " + offer.ToString());

            Assert.True(IPAddress.Loopback.Equals(IPAddress.Parse(offer.Connection.ConnectionAddress)));

            localSession.Close("normal");
        }

        /// <summary>
        /// Checks that the SDP offer gets created with the correct connection address when the RTPSession
        /// is instantiated with a specific IPv6  bind address.
        /// </summary>
        [Fact]
        public void CheckCreateOfferWithIPv6BindAddressAnswerTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            RTPSession localSession = new RTPSession(false, false, false, IPAddress.IPv6Loopback);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            // Generate the offer to send to the remote party.
            var offer = localSession.CreateOffer(null);

            logger.LogDebug("Local offer: " + offer.ToString());

            Assert.True(IPAddress.IPv6Loopback.Equals(IPAddress.Parse(offer.Connection.ConnectionAddress)));

            localSession.Close("normal");
        }

        /// <summary>
        /// Checks that setting the remote description gets accepted when the remote offer has audio
        /// and video but the local session only has an audio track.
        /// </summary>
        [Fact]
        public void AudioVideoOfferNoLocalVideoUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            // A local session is created but only has an audio track added to it.
            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            // Create a remote session with both audio and video tracks.
            RTPSession remoteSession = new RTPSession(false, false, false);
            // The track for the track for the remote session is still local relative to the session it's being added to.
            MediaStreamTrack remoteAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            remoteSession.addTrack(remoteAudioTrack);
            MediaStreamTrack remoteVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            remoteSession.addTrack(remoteVideoTrack);

            var offer = remoteSession.CreateOffer(IPAddress.Loopback);

            // Give the offer to the local session that is missing a video tracks.
            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = localSession.CreateAnswer(null);

            Assert.Equal(MediaStreamStatusEnum.SendRecv, answer.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().MediaStreamStatus);
            Assert.Equal(MediaStreamStatusEnum.Inactive, answer.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaStreamStatus);

            localSession.Close("normal");
            remoteSession.Close("normal");
        }

        /// <summary>
        /// Checks that setting the remote description gets accepted when the remote offer has audio
        /// and video but the local session only has a video track.
        /// </summary>
        [Fact]
        public void AudioVideoOfferNoLocalAudioUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            // A local session is created but only has a video track added to it.
            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            localSession.addTrack(localVideoTrack);

            // Create a remote session with both audio and video tracks.
            RTPSession remoteSession = new RTPSession(false, false, false);
            // The track for the track for the remote session is still local relative to the session it's being added to.
            MediaStreamTrack remoteAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            remoteSession.addTrack(remoteAudioTrack);
            MediaStreamTrack remoteVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            remoteSession.addTrack(remoteVideoTrack);

            var offer = remoteSession.CreateOffer(IPAddress.Loopback);

            // Give the offer to the local session that is missing a video tracks.
            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = localSession.CreateAnswer(null);

            Assert.Equal(MediaStreamStatusEnum.Inactive, answer.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().MediaStreamStatus);
            Assert.Equal(MediaStreamStatusEnum.SendRecv, answer.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaStreamStatus);

            localSession.Close("normal");
            remoteSession.Close("normal");
        }

        /// <summary>
        /// Checks that if an RTP session is requested with a specific port and it's already in use an exception is thrown.
        /// </summary>
        [Fact]
        public void CheckDuplicateBindPortFailsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create two RTP sessions. First one acts as the local session to generate the offer.
            // Second one acts as the remote session to generate the answer.

            RTPSession localSession = new RTPSession(false, false, false, IPAddress.Loopback);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);

            var rtpEndPoint = localSession.GetRtpChannel(SDPMediaTypesEnum.audio).RTPLocalEndPoint;

            logger.LogDebug($"RTP session local end point {rtpEndPoint}.");

            // Now attempt to create a second RTP session on the same port as the previous one.

            RTPSession duplicateSession = new RTPSession(false, false, false, IPAddress.Loopback, rtpEndPoint.Port);
            MediaStreamTrack duplicateTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            Assert.Throws<ApplicationException>(() => duplicateSession.addTrack(duplicateTrack));

            localSession.Close(null);
        }

        /// <summary>
        /// Checks that the order of the media announcements in the answer matches the order from
        /// the remote offer.
        /// </summary>
        [Fact]
        public void MediaOrderMatchesRemoteOfferUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // By default offers made by us always put audio first. Create a remote SDP offer 
            // with the video first.
            string remoteSdp =
            @"v=0
o=- 1986548327 0 IN IP4 127.0.0.1
s=-
c=IN IP4 127.0.0.1
t=0 0
m=video 60638 RTP/AVP 100
a=rtpmap:100 VP8/90000
a=sendrecv
m=audio 60640 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv";

            // Create a local session and add the video track first.
            RTPSession localSession = new RTPSession(false, false, false);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            localSession.addTrack(localAudioTrack);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            localSession.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug($"Remote offer: {offer}");

            var result = localSession.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = localSession.CreateAnswer(null);

            logger.LogDebug($"Local answer: {answer}");

            Assert.Equal(offer.Media.First().Media, answer.Media.First().Media);
            Assert.Equal(offer.Media.Last().Media, answer.Media.Last().Media);

            localSession.Close("normal");
        }
    }
}
