//-----------------------------------------------------------------------------
// Filename: RTPMediaSession.cs
//
// Description: An implementation of IMediaSession based on RTPSession
//
// Author(s):
// Yizchok G.
//
// History:
// 12/23/2019	Yitzchok	    Created.
// 26 Dec 2019  Aaron Clauson   Modified to inherit from RTPSession instead of
//                              using an instance and wrapping same methods.
// 20 Jan 2020  Aaron Clauson   Extracted SDP functions from RTPSession and placed
//                              into this class.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// A concrete implementation of IMediaSession.
    /// Relies on RTPSession and RTPChannel for RTP and network functions.
    ///
    /// This implementation sets up the RTP stream but doesn't process the actual audio or video
    /// and will need another component to do the processing of the media and send/receive it to/from the RTP session.
    /// </summary>
    public class RTPMediaSession : RTPSession, IMediaSession
    {
        public const string TELEPHONE_EVENT_ATTRIBUTE = "telephone-event";
        public const int DTMF_EVENT_DURATION = 1200;        // Default duration for a DTMF event.
        public const int DTMF_EVENT_PAYLOAD_ID = 101;
        public const string SDP_SESSION_NAME = "sipsorcery";

        private static readonly ILogger logger = Log.Logger;

        public bool LocalOnHold { get; set; }
        public bool RemoteOnHold { get; set; }

        /// <summary>
        /// The media announcements from each of the streams multiplexed in this RTP session.
        /// <code>
        /// // Example:
        /// m=audio 10000 RTP/AVP 0
        /// a=rtpmap:0 PCMU/8000
        /// a=rtpmap:101 telephone-event/8000
        /// a=fmtp:101 0-15
        /// a=sendrecv
        /// </code>
        /// </summary>
        public List<SDPMediaAnnouncement> MediaAnnouncements { get; private set; } = new List<SDPMediaAnnouncement>();

        /// <summary>
        /// The SDP offered by the remote call party for this session.
        /// </summary>
        public SDP RemoteSDP { get; private set; }

        public RTPMediaSession(SDPMediaTypesEnum mediaType, int formatTypeID, AddressFamily addrFamily)
             : base(mediaType, formatTypeID, addrFamily, false, false)
        {
            // Construct the local SDP. There are a number of assumptions being made here:
            // PCMU audio, RTP event support etc.
            var mediaFormat = new SDPMediaFormat(formatTypeID);
            var mediaAnnouncement = new SDPMediaAnnouncement
            {
                Media = mediaType,
                MediaFormats = new List<SDPMediaFormat> { mediaFormat },
                MediaStreamStatus = MediaStreamStatusEnum.SendRecv,
                Port = base.RtpChannel.RTPPort
            };

            if (mediaType == SDPMediaTypesEnum.audio)
            {
                // RTP event support.
                int clockRate = mediaFormat.GetClockRate();
                SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
                rtpEventFormat.SetFormatAttribute($"{TELEPHONE_EVENT_ATTRIBUTE}/{clockRate}");
                rtpEventFormat.SetFormatParameterAttribute("0-16");
                mediaAnnouncement.MediaFormats.Add(rtpEventFormat);
            }

            MediaAnnouncements.Add(mediaAnnouncement);
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
        /// Gets fired when an RTP DTMF event is completed on the remote call party's RTP stream.
        /// </summary>
        public event Action<byte> DtmfCompleted;

        public Task SendDtmf(byte key, CancellationToken cancellationToken = default)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, DTMF_EVENT_DURATION, DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, cancellationToken);
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
            CloseSession(null);
        }

        /// <summary>
        /// Gets the a basic Session Description Protocol object that describes this RTP session.
        /// </summary>
        /// <param name="localAddress">The RTP socket we will be sending from. Note this can't be IPAddress.Any as
        /// it's getting sent to the callee. An IP address of 0.0.0.0 or [::0] will typically be interpreted as
        /// "don't send me any RTP".</param>
        /// <returns>An Session Description Protocol object that can be sent to a remote callee.</returns>
        public SDP GetSDP(IPAddress localAddress)
        {
            var sdp = new SDP(localAddress)
            {
                SessionId = Crypto.GetRandomInt(5).ToString(),
                SessionName = SDP_SESSION_NAME,
                Timing = "0 0",
                Connection = new SDPConnectionInformation(localAddress),
            };

            sdp.Media = MediaAnnouncements;

            return sdp;
        }

        /// <summary>
        /// Sets relevant properties for this session based on the SDP from the remote party.
        /// </summary>
        /// <param name="sdp">The SDP from the remote call party.</param>
        public void SetRemoteSDP(SDP sdp)
        {
            RemoteSDP = sdp;

            foreach (var announcement in sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio))
            {
                foreach (var mediaFormat in announcement.MediaFormats)
                {
                    if (mediaFormat.FormatAttribute?.StartsWith(TELEPHONE_EVENT_ATTRIBUTE) == true)
                    {
                        if (!int.TryParse(mediaFormat.FormatID, out var remoteRtpEventPayloadID))
                        {
                            logger.LogWarning("The media format on the telephone event attribute was not a valid integer.");
                        }
                        else
                        {
                            base.RemoteRtpEventPayloadID = remoteRtpEventPayloadID;
                        }
                        break;
                    }
                }
            }
            var dstEndPoint = sdp.GetSDPRTPEndPoint();
            SetDestination(dstEndPoint, new IPEndPoint(dstEndPoint.Address, dstEndPoint.Port + 1));
        }

        public Task<string> CreateOffer(IPAddress destinationAddress = null) =>
            Task.FromResult(CreateOfferInternal(destinationAddress));

        private string CreateOfferInternal(IPAddress destinationAddress = null)
        {
            var destinationAddressToUse = FindDestinationAddressToUse(destinationAddress);

            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(destinationAddressToUse);

            var localSDP = GetSDP(localIPAddress);

            AdjustSdpForMediaState(localSDP);

            return localSDP.ToString();
        }

        private IPAddress FindDestinationAddressToUse(IPAddress destinationAddress)
        {
            IPAddress destinationAddressToUse = destinationAddress;

            if (destinationAddressToUse == null)
            {
                if (RemoteSDP != null)
                {
                    //Check for endpoint from the SDP
                    IPEndPoint dstRtpEndPoint = RemoteSDP.GetSDPRTPEndPoint();
                    destinationAddressToUse = dstRtpEndPoint.Address;

                    bool newEndpoint = DestinationEndPoint != dstRtpEndPoint;

                    if (newEndpoint)
                    {
                        logger.LogDebug(
                            $"Remote call party RTP end point changed from {DestinationEndPoint} to {dstRtpEndPoint}.");
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
            SetRemoteSDP(sdp);

            CheckRemotePartyHoldCondition(sdp);

            logger.LogDebug($"Remote RTP socket {DestinationEndPoint}.");
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
