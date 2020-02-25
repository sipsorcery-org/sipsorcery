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

        private static readonly ILogger logger = Log.Logger;

        public uint AudioTimestamp { get; private set; }
        public uint VideoTimestamp { get; private set; }

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

        /// <summary>
        /// Send a media sample to the remote party.
        /// </summary>
        /// <param name="mediaType">Whether the sample is audio or video.</param>
        /// <param name="samplePeriod">The period for the sample in the sampling units
        /// (e.g. 8KHz for PCMU, 90KHz for VP8).</param>
        /// <param name="sample">The sample payload.</param>
        public void SendMedia(SDPMediaTypesEnum mediaType, uint samplePeriod, byte[] sample)
        {
            if (mediaType == SDPMediaTypesEnum.video)
            {
                int vp8PayloadID = Convert.ToInt32(VideoLocalTrack.Capabilties.Single(x => x.FormatCodec == SDPMediaFormatsEnum.VP8).FormatID);
                SendVp8Frame(VideoTimestamp, vp8PayloadID, sample);
                VideoTimestamp += samplePeriod;
            }
            else if (mediaType == SDPMediaTypesEnum.audio)
            {
                int pcmuPayloadID = Convert.ToInt32(AudioLocalTrack.Capabilties.Single(x => x.FormatCodec == SDPMediaFormatsEnum.PCMU).FormatID);
                SendAudioFrame(AudioTimestamp, pcmuPayloadID, sample);
                AudioTimestamp += samplePeriod;
            }
        }

        public Task SendDtmf(byte key, CancellationToken cancellationToken = default)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, DTMF_EVENT_DURATION, DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, cancellationToken);
        }

        public virtual void Close()
        {
            CloseSession(null);
        }

        /// <summary>
        /// Sets relevant properties for this session based on the SDP from the remote party.
        /// </summary>
        /// <param name="sessionDescription">The session description from the remote call party.</param>
        public override void setRemoteDescription(RTCSessionDescription sessionDescription)
        {
            base.setRemoteDescription(sessionDescription);

            var connAddr = IPAddress.Parse(sessionDescription.sdp.Connection.ConnectionAddress);

            foreach (var announcement in sessionDescription.sdp.Media)
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
                else if (announcement.Media == SDPMediaTypesEnum.video)
                {
                    var connRtpEndPoint = new IPEndPoint(annAddr, announcement.Port);
                    var connRtcpEndPoint = new IPEndPoint(annAddr, announcement.Port + 1);

                    SetDestination(SDPMediaTypesEnum.video, connRtpEndPoint, connRtcpEndPoint);
                }
            }
        }
    }
}
