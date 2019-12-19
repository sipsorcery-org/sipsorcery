using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App.Media
{
    public class RTPMediaSession : IMediaSession
    {
        private static readonly ILogger logger = Log.Logger;
        private readonly RTPSession session;

        public RTPMediaSession(RTPSession rtpSession)
        {
            session = rtpSession;
            session.OnRtpEvent += OnRemoteRtpEvent;
        }

        public Task SendDtmf(byte key, CancellationToken cancellationToken = default)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, 1200, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            return session.SendDtmfEvent(dtmfEvent, cancellationToken);
        }

        public event Action<byte> DtmfCompleted;

        public void Close()
        {
            session.Close();
            session.OnRtpEvent -= OnRemoteRtpEvent;
        }

        public SDP CreateOffer(IPAddress destinationAddress)
        {
            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(destinationAddress);
            return session.GetSDP(localIPAddress);
        }

        public void OfferAnswered(SDP remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
        }

        public SDP AnswerOffer(SDP remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
            return CreateOffer(remoteSDP.GetSDPRTPEndPoint().Address);
        }

        public SDP ReInvite(SDP remoteSDP)
        {
            IPEndPoint dstRtpEndPoint = remoteSDP.GetSDPRTPEndPoint();

            bool newEndpoint = session.DestinationEndPoint != dstRtpEndPoint;

            var localSDP = CreateOffer(dstRtpEndPoint.Address);

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
            var mediaAnnouncement = localSDP.Media
                .FirstOrDefault(x => x.Media == SDPMediaTypesEnum.audio);

            if (mediaAnnouncement != null)
            {
                mediaAnnouncement.MediaStreamStatus = localMediaStreamStatus;
            }
        }

        private void SetRemoteSDP(SDP remoteSDP)
        {
            session.SetRemoteSDP(remoteSDP);
            session.DestinationEndPoint = SDP.GetSDPRTPEndPoint(remoteSDP.ToString());
            logger.LogDebug($"Remote RTP socket {session.DestinationEndPoint}.");
        }


        private ushort remoteDtmfDuration;

        /// <summary>
        /// Event handler for RTP events from the remote call party.
        /// </summary>
        /// <param name="rtpEvent">The received RTP event.</param>
        private void OnRemoteRtpEvent(RTPEvent rtpEvent)
        {
            if (rtpEvent.EndOfEvent)
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
