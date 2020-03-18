//-----------------------------------------------------------------------------
// Filename: RtpSessionLight.cs
//
// Description: Lightweight RTP session suitable for load testing.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public class RtpSessionLight : IMediaSession
    {
        private const string RTP_MEDIA_PROFILE = "RTP/AVP";

        public RTCSessionDescription localDescription { get; private set; }
        public RTCSessionDescription remoteDescription { get; private set; }

        public bool IsClosed { get; private set; }
        public bool HasAudio => true;
        public bool HasVideo => false;

        public event Action<byte[], uint, uint, int> OnVideoSampleReady;
        public event Action<string> OnRtpClosed;
        public event Action<SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;
        public event Action<RTPEvent> OnRtpEvent;
        public event Action<Complex[]> OnAudioScopeSampleReady;
        public event Action<Complex[]> OnHoldAudioScopeSampleReady;

        public void Close(string reason)
        {
            IsClosed = true;
        }

        public Task<SDP> createAnswer(RTCAnswerOptions options)
        {
            SDP answerSdp = new SDP(IPAddress.Loopback);
            answerSdp.SessionId = Crypto.GetRandomInt(5).ToString();

            answerSdp.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
               1234,
               new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

            answerSdp.Media.Add(audioAnnouncement);

            return Task.FromResult(answerSdp);
        }

        public Task<SDP> createOffer(RTCOfferOptions options)
        {
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = Crypto.GetRandomInt(5).ToString();

            offerSdp.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
               1234,
               new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

            offerSdp.Media.Add(audioAnnouncement);

            return Task.FromResult(offerSdp);
        }

        public Task SendDtmf(byte tone, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public void SendMedia(SDPMediaTypesEnum mediaType, uint samplePeriod, byte[] sample)
        {
            throw new NotImplementedException();
        }

        public void setLocalDescription(RTCSessionDescription sessionDescription)
        {
            localDescription = sessionDescription;
        }

        public void setRemoteDescription(RTCSessionDescription sessionDescription)
        {
            remoteDescription = sessionDescription;
        }

        public Task Start()
        {
            return Task.CompletedTask;
        }
    }
}
