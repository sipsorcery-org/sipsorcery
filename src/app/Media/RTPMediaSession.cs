﻿//-----------------------------------------------------------------------------
// Filename: RTPMediaSession.cs
//
// Description: An implementation of IMediaSession based on RTPSession
//
// Author(s):
// Yizchok G.
//
// History:
// 12/23/2019	Yitzchok	  Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// The SipSorcery concrete implementation of IMediaSession.
    /// Relies on RTPSession and RTPChannel for RTP and network functions.
    ///
    /// This implementation sets up the RTP stream but doesn't process the actual audio or video
    /// and will need another component to do the processing of the media and send/receive it to/from the RTP session.
    /// </summary>
    public class RTPMediaSession : IMediaSession
    {
        private static readonly ILogger logger = Log.Logger;

        public RTPSession Session { get; }
        public bool LocalOnHold { get; set; }
        public bool RemoteOnHold { get; set; }

        public RTPMediaSession(RTPSession rtpSession)
        {
            Session = rtpSession;
            Session.OnRtpEvent += OnRemoteRtpEvent;
            Session.OnReceivedSampleReady += OnReceivedSampleReady;
        }

        /// <summary>
        /// This event is invoked when the session media has changed
        /// and a new SDP is available.
        /// </summary>
        public event Action<string> SessionMediaChanged;

        /// <summary>	
        /// The remote call party has put us on hold.	
        /// </summary>	
        public event Action RemotePutOnHold;

        /// <summary>	
        /// The remote call party has taken us off hold.	
        /// </summary>	
        public event Action RemoteTookOffHold;

        /// <summary>
        /// Media Session closed.
        /// </summary>
        public event Action Closed;

        /// <summary>
        /// Gets fired when an RTP DTMF event is completed on the remote call party's RTP stream.
        /// </summary>
        public event Action<byte> DtmfCompleted;

        /// <summary>
        /// Gets fired when an RTP packet is received, has been identified and is ready for processing.
        /// </summary>
        public event Action<byte[]> OnReceivedSampleReady;

        public Task SendDtmf(byte key, CancellationToken cancellationToken = default)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, 1200, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            return Session.SendDtmfEvent(dtmfEvent, cancellationToken);
        }

        /// <summary>
        /// Send a re-INVITE request to put the remote call party on hold.
        /// </summary>
        public void PutOnHold()
        {
            LocalOnHold = true;
            SessionMediaChanged?.Invoke(CreateOfferInternal());
        }

        /// <summary>
        /// Send a re-INVITE request to take the remote call party on hold.
        /// </summary>
        public void TakeOffHold()
        {
            LocalOnHold = false;
            SessionMediaChanged?.Invoke(CreateOfferInternal());
        }

        public virtual void Close()
        {
            Session.OnRtpEvent -= OnRemoteRtpEvent;
            Session.OnReceivedSampleReady -= OnReceivedSampleReady;
            Session.Close();
            Closed?.Invoke();
        }

        public Task<string> CreateOffer(IPAddress destinationAddress = null) =>
            Task.FromResult(CreateOfferInternal(destinationAddress));

        private string CreateOfferInternal(IPAddress destinationAddress = null)
        {
            var destinationAddressToUse = FindDestinationAddressToUse(destinationAddress);

            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(destinationAddressToUse);

            var localSDP = Session.GetSDP(localIPAddress);

            AdjustSdpForMediaState(localSDP);

            return localSDP.ToString();
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

            if (LocalOnHold && RemoteOnHold)
            {
                mediaAnnouncement.MediaStreamStatus = MediaStreamStatusEnum.None;
            }
            else if (!LocalOnHold && !RemoteOnHold)
            {
                mediaAnnouncement.MediaStreamStatus = MediaStreamStatusEnum.SendRecv;
            }
            else
            {
                mediaAnnouncement.MediaStreamStatus =
                    LocalOnHold
                        ? MediaStreamStatusEnum.SendOnly
                        : MediaStreamStatusEnum.RecvOnly;
            }
        }

        public Task OfferAnswered(string remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
            return Task.FromResult(true);
        }

        public Task<string> AnswerOffer(string remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
            return CreateOffer();
        }

        public Task<string> RemoteReInvite(string remoteSDP)
        {
            SetRemoteSDP(remoteSDP);
            return CreateOffer();
        }

        private void SetRemoteSDP(string remoteSDP)
        {
            var sdp = SDP.ParseSDPDescription(remoteSDP);
            Session.SetRemoteSDP(sdp);
            Session.DestinationEndPoint = sdp.GetSDPRTPEndPoint();

            CheckRemotePartyHoldCondition(sdp);

            logger.LogDebug($"Remote RTP socket {Session.DestinationEndPoint}.");
        }

        private void CheckRemotePartyHoldCondition(SDP remoteSDP)
        {
            var mediaStreamStatus = remoteSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);

            if (mediaStreamStatus == MediaStreamStatusEnum.SendOnly)
            {
                if (!RemoteOnHold)
                {
                    RemoteOnHold = true;
                    RemotePutOnHold?.Invoke();
                }
            }
            else if (mediaStreamStatus == MediaStreamStatusEnum.SendRecv && RemoteOnHold)
            {
                if (RemoteOnHold)
                {
                    RemoteOnHold = false;
                    RemoteTookOffHold?.Invoke();
                }
            }
        }

        public void SendAudioFrame(uint timestamp, byte[] buffer) =>
            Session.SendAudioFrame(timestamp, buffer);

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
