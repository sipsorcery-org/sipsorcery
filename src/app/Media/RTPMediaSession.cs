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

        private ushort m_remoteDtmfDuration;

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

        public RTPMediaSession(AddressFamily addrFamily)
           : base(addrFamily, false, false, false)
        { }

        public RTPMediaSession(SDPMediaTypesEnum mediaType, SDPMediaFormat codec, AddressFamily addrFamily)
             : base(addrFamily, false, false, false)
        {
            var capabilities = new List<SDPMediaFormat> { codec };

            if (mediaType == SDPMediaTypesEnum.audio)
            {
                // RTP event support.
                int clockRate = codec.GetClockRate();
                SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
                rtpEventFormat.SetFormatAttribute($"{TELEPHONE_EVENT_ATTRIBUTE}/{clockRate}");
                rtpEventFormat.SetFormatParameterAttribute("0-16");
                capabilities.Add(rtpEventFormat);
            }

            MediaStreamTrack track = new MediaStreamTrack(null, mediaType, false, capabilities);
            addTrack(track);
        }

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

            // The action we take to put a call on hold is to switch the media status
            // to sendonly and change the audio input from a capture device to on hold
            // music.
            AdjustSdpForMediaState(localDescription);

            SessionMediaChanged?.Invoke(localDescription.ToString());
        }

        /// <summary>
        /// Send a re-INVITE request to take the remote call party on hold.
        /// </summary>
        public void TakeOffHold()
        {
            LocalOnHold = false;
            AdjustSdpForMediaState(localDescription);
            SessionMediaChanged?.Invoke(localDescription.ToString());
        }

        public virtual void Close()
        {
            CloseSession(null);
        }

        /// <summary>
        /// Sets relevant properties for this session based on the SDP from the remote party.
        /// </summary>
        /// <param name="sdp">The SDP from the remote call party.</param>
        public override void setRemoteDescription(SDP sdp)
        {
            base.setRemoteDescription(sdp);

            var connAddr = IPAddress.Parse(sdp.Connection.ConnectionAddress);

            CheckRemotePartyHoldCondition(sdp);

            foreach (var announcement in sdp.Media)
            {
                var annAddr = connAddr;
                if (announcement.Connection != null)
                {
                    annAddr = IPAddress.Parse(announcement.Connection.ConnectionAddress);
                }

                if (announcement.Media == SDPMediaTypesEnum.audio)
                {
                    var connRtpEndPoint = new IPEndPoint(annAddr, announcement.Port);
                    var connRtcpEndPoint = new IPEndPoint(annAddr, announcement.Port + 1);

                    SetDestination(SDPMediaTypesEnum.audio, connRtpEndPoint, connRtcpEndPoint);

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
                else if(announcement.Media == SDPMediaTypesEnum.video)
                {
                    var connRtpEndPoint = new IPEndPoint(annAddr, announcement.Port);
                    var connRtcpEndPoint = new IPEndPoint(annAddr, announcement.Port + 1);

                    SetDestination(SDPMediaTypesEnum.video, connRtpEndPoint, connRtcpEndPoint);
                }
            }
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

        //public Task OfferAnswered(string remoteSDP)
        //{
        //    SetRemoteSDP(remoteSDP);
        //    return Task.FromResult(true);
        //}

        //public Task<SDP> createAnswer()
        //{
        //    //SetRemoteSDP(remoteSDP);
            
        //    // TODO: Need to generate an answer from the offer not generate a new offer.
            
        //    //IPAddress localAddress = null;
        //    //if(AudioDestinationEndPoint != null)
        //    //{
        //    //    localAddress = NetServices.GetLocalAddressForRemote(AudioDestinationEndPoint.Address);
        //    //}

        //    //return createOffer(localAddress);
        //}

        //public Task<SDP> RemoteReInvite(string remoteSDP)
        //{
        //    SetRemoteSDP(remoteSDP);
        //    // TODO: Need to generate an answer from the offer not generate a new offer.
        //    return CreateOffer();
        //}

        //public override void setRemoteDescription(SDP sessionDescription)
        //{
        //    //var sdp = SDP.ParseSDPDescription(remoteSDP);
        //    //SetRemoteSDP(sdp);
        //    base.setRemoteDescription(sessionDescription);

        //    CheckRemotePartyHoldCondition(sdp);

        //    logger.LogDebug($"Remote RTP Audio socket {AudioDestinationEndPoint}.");

        //    if(VideoDestinationEndPoint != null)
        //    {
        //        logger.LogDebug($"Remote RTP Audio socket {AudioDestinationEndPoint}.");
        //    }
        //}

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

        /// <summary>
        /// Event handler for RTP events from the remote call party.
        /// </summary>
        /// <param name="rtpEvent">The received RTP event.</param>
        private void OnRemoteRtpEvent(RTPEvent rtpEvent)
        {
            if (rtpEvent.EndOfEvent)
            {
                m_remoteDtmfDuration = 0;
            }
            else if (m_remoteDtmfDuration == 0)
            {
                m_remoteDtmfDuration = rtpEvent.Duration;
                DtmfCompleted?.Invoke(rtpEvent.EventID);
            }
        }
    }
}
