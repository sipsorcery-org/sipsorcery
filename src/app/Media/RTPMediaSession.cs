using System;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App.Media
{
    public class RTPMediaSession : IMediaSession
    {
        private static ILogger logger = Log.Logger;
        private readonly RTPSession session;
        private ushort remoteDtmfDuration;

        public RTPMediaSession(RTPSession rtpSession)
        {
            session = rtpSession;
            session.OnRtpEvent += OnRemoteRtpEvent;
        }

        public event Action<byte> DtmfCompleted;

        public void Close()
        {
            session.Close();
            session.OnRtpEvent -= OnRemoteRtpEvent;
        }

        public SDP GetAnswerSDP(IPAddress destinationAddress)
        {
            return GetOfferSDP(destinationAddress);
        }

        public SDP GetOfferSDP(IPAddress destinationAddress)
        {
            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(destinationAddress);
            return session.GetSDP(localIPAddress);
        }

        public SDP ReInvite(SDP remoteSDP)
        {
            IPEndPoint dstRtpEndPoint = remoteSDP.GetSDPRTPEndPoint();

            bool newEndpoint = session.DestinationEndPoint != dstRtpEndPoint;

            var localSDP = GetAnswerSDP(dstRtpEndPoint.Address);

            if (newEndpoint)
            {
                logger.LogDebug($"Remote call party RTP end point changed from {session.DestinationEndPoint} to {dstRtpEndPoint}.");
            }

            // Check for remote party putting us on and off hold.
            var mediaStreamStatus = remoteSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);
            var oldMediaStreamStatus = session.RemoteSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);

            SetRemoteSDP(remoteSDP);

            if (mediaStreamStatus == MediaStreamStatusEnum.SendOnly)
            {
                ProcessRemoteHoldRequest(localSDP, MediaStreamStatusEnum.SendRecv);
            }
            else if (mediaStreamStatus == MediaStreamStatusEnum.SendRecv
                  && oldMediaStreamStatus == MediaStreamStatusEnum.SendOnly)
            {
                ProcessRemoteHoldRequest(localSDP, MediaStreamStatusEnum.SendRecv);
            }

            return localSDP;
        }

        private void ProcessRemoteHoldRequest(SDP localSDP, MediaStreamStatusEnum localMediaStreamStatus)
        {
            localSDP.Media.First(x => x.Media == SDPMediaTypesEnum.audio).MediaStreamStatus = localMediaStreamStatus;
        }


        public void SetRemoteAnswerSDP(SDP remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
        }

        public void SetRemoteOfferSDP(SDP remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
        }

        private void SetRemoteSDP(SDP remoteSDP)
        {
            session.SetRemoteSDP(remoteSDP);
            session.DestinationEndPoint = SDP.GetSDPRTPEndPoint(remoteSDP.ToString());
            logger.LogDebug($"Remote RTP socket {session.DestinationEndPoint}.");
        }

        /// <summary>
        /// Event handler for RTP events from the remote call party.
        /// </summary>
        /// <param name="rtpEvent">The received RTP event.</param>
        private void OnRemoteRtpEvent(RTPEvent rtpEvent)
        {
            if (rtpEvent.EndOfEvent == true)
            {
                remoteDtmfDuration = 0;
            }
            else if (remoteDtmfDuration == 0)
            {
                remoteDtmfDuration = rtpEvent.Duration;
                DtmfCompleted?.Invoke(rtpEvent.EventID);
            }
        }
    }
}
