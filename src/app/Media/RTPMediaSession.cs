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
        public RTPSession Session { get; }

        public MediaState MediaState { get; set; }

        public RTPMediaSession(RTPSession rtpSession)
        {
            Session = rtpSession;
            Session.OnRtpEvent += OnRemoteRtpEvent;
            MediaState = new MediaState();
        }

        public Task SendDtmf(byte key, CancellationToken cancellationToken = default)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, 1200, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            return Session.SendDtmfEvent(dtmfEvent, cancellationToken);
        }

        public event Action<byte> DtmfCompleted;
        
        public event Action Closed;

        public virtual void Close()
        {
            Session.OnRtpEvent -= OnRemoteRtpEvent;
            Session.Close();
            Closed?.Invoke();
        }

        public SDP CreateOffer(IPAddress destinationAddress = null)
        {
            var destinationAddressToUse = FindDestinationAddressToUse(destinationAddress);

            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(destinationAddressToUse);

            var localSDP = Session.GetSDP(localIPAddress);

            AdjustSdpForMediaState(localSDP);

            return localSDP;
        }

        private IPAddress FindDestinationAddressToUse(IPAddress destinationAddress)
        {
            IPAddress destinationAddressToUse = destinationAddress;

            if (destinationAddressToUse == null)
            {
                if (Session.RemoteSDP != null)
                {
                    //Check for endpoint from the SDP
                    IPEndPoint dstRtpEndPoint = Session.RemoteSDP.GetSDPRTPEndPoint();
                    destinationAddressToUse = dstRtpEndPoint.Address;

                    bool newEndpoint = Session.DestinationEndPoint != dstRtpEndPoint;

                    if (newEndpoint)
                    {
                        logger.LogDebug(
                            $"Remote call party RTP end point changed from {Session.DestinationEndPoint} to {dstRtpEndPoint}.");
                    }
                }
                else
                {
                    destinationAddressToUse = IPAddress.Any;
                }
            }

            return destinationAddressToUse;
        }

        private void AdjustSdpForMediaState(SDP localSDP)
        {
            var mediaAnnouncement = localSDP.Media.FirstOrDefault(x => x.Media == SDPMediaTypesEnum.audio);

            if (mediaAnnouncement == null)
            {
                return;
            }

            if (MediaState.LocalOnHold && MediaState.RemoteOnHold)
            {
                mediaAnnouncement.MediaStreamStatus = MediaStreamStatusEnum.None;
            }
            else if (!MediaState.LocalOnHold && !MediaState.RemoteOnHold)
            {
                mediaAnnouncement.MediaStreamStatus = MediaStreamStatusEnum.SendRecv;
            }
            else
            {
                mediaAnnouncement.MediaStreamStatus =
                    MediaState.LocalOnHold
                        ? MediaStreamStatusEnum.SendOnly
                        : MediaStreamStatusEnum.RecvOnly;
            }
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

        public SDP RemoteReInvite(SDP remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
            return CreateOffer();
        }

        private void SetRemoteSDP(SDP remoteSDP)
        {
            Session.SetRemoteSDP(remoteSDP);
            Session.DestinationEndPoint = SDP.GetSDPRTPEndPoint(remoteSDP.ToString());

            CheckRemotePartyHoldCondition(remoteSDP);

            logger.LogDebug($"Remote RTP socket {Session.DestinationEndPoint}.");
        }

        private void CheckRemotePartyHoldCondition(SDP remoteSDP)
        {
            var mediaStreamStatus = remoteSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);

            if (mediaStreamStatus == MediaStreamStatusEnum.SendOnly)
            {
                MediaState.RemoteOnHold = true;
            }
            else if (mediaStreamStatus == MediaStreamStatusEnum.SendRecv && MediaState.RemoteOnHold)
            {
                MediaState.RemoteOnHold = false;
            }
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
