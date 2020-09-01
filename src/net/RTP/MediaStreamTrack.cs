//-----------------------------------------------------------------------------
// Filename: MediaStreamTrack.cs
//
// Description: Represents a one-way audio or video stream. In a typical call
// a media session could have 4 tracks, local and remote audio and video.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Aug 2020	Aaron Clauson	Refactored from RTPSession.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Net
{
    public class MediaStreamTrack
    {
        /// <summary>
        /// The type of media stream represented by this track. Must be audio or video.
        /// </summary>
        public SDPMediaTypesEnum Kind { get; private set; }

        /// <summary>
        /// The value used in the RTP Synchronisation Source header field for media packets
        /// sent using this media stream.
        /// </summary>
        public uint Ssrc { get; internal set; }

        /// <summary>
        /// The value used in the RTP Sequence Number header field for media packets
        /// sent using this media stream.
        /// </summary>
        public ushort SeqNum { get; internal set; }

        /// <summary>
        /// The value used in the RTP Timestamp header field for media packets
        /// sent using this media stream.
        /// </summary>
        public uint Timestamp { get; internal set; }

        /// <summary>
        /// Indicates whether this track was sourced by a remote connection.
        /// </summary>
        public bool IsRemote { get; set; }

        /// <summary>
        /// By default audio channels will support DTMF via telephone events. To opt
        /// out of DTMF support set this property to true.
        /// </summary>
        public bool NoDtmfSupport { get; set; }

        /// <summary>
        /// The media capabilities supported by this track.
        /// </summary>
        public List<SDPMediaFormat> Capabilities { get; internal set; }

        /// <summary>
        /// Represents the original and default stream status for the track. This is set
        /// when the track is created and does not change. It allows tracks to be set back to
        /// their original state after being put on hold etc. For example if a track is
        /// added as receive only video source then when after on and off hold it needs to
        /// be known that the track reverts receive only rather than sendrecv.
        /// </summary>
        public MediaStreamStatusEnum DefaultStreamStatus { get; private set; }

        /// <summary>
        /// Holds the stream state of the track.
        /// </summary>
        public MediaStreamStatusEnum StreamStatus { get; internal set; }

        /// <summary>
        /// If the SDP remote the remote party provides "a=ssrc" attributes, as specified
        /// in RFC5576, this property will hold the values. The list can be used when
        /// an RTP/RTCP packet is received and needs to be matched against a media type or 
        /// RTCP report.
        /// </summary>
        public Dictionary<uint, SDPSsrcAttribute> SdpSsrc { get; set; } = new Dictionary<uint, SDPSsrcAttribute>();

        /// <summary>
        /// Creates a lightweight class to track a media stream track within an RTP session 
        /// When supporting RFC3550 (the standard RTP specification) the relationship between
        /// an RTP stream and session is 1:1. For WebRTC and RFC8101 there can be multiple
        /// streams per session.
        /// </summary>
        /// <param name="kind">The type of media for this stream. There can only be one
        /// stream per media type.</param>
        /// <param name="isRemote">True if this track corresponds to a media announcement from the 
        /// remote party.</param>
        /// <param name="Capabilities">The capabilities for the track being added. Where the same media
        /// type is supported locally and remotely only the mutual capabilities can be used. This will
        /// occur if we receive an SDP offer (add track initiated by the remote party) and we need
        /// to remove capabilities we don't support.</param>
        /// <param name="streamStatus">The initial stream status for the media track. Defaults to
        /// send receive.</param>
        public MediaStreamTrack(
            SDPMediaTypesEnum kind,
            bool isRemote,
            List<SDPMediaFormat> capabilities,
            MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv)
        {
            Kind = kind;
            IsRemote = isRemote;
            Capabilities = capabilities;
            StreamStatus = streamStatus;
            DefaultStreamStatus = streamStatus;

            if (!isRemote)
            {
                Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
                SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
            }
        }

        /// <summary>
        /// Add a local audio track.
        /// </summary>
        /// <param name="audioFormats">The audio formats that the local application supports.</param>
        /// <param name="streamStatus">Optional. The stream status for the audio track, e.g. whether
        /// send and receive or only one of.</param>
        public MediaStreamTrack(
            List<AudioCodecsEnum> audioFormats,
            MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv)
        {
            Kind = SDPMediaTypesEnum.audio;
            IsRemote = false;
            StreamStatus = streamStatus;
            DefaultStreamStatus = streamStatus;
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));

            if (audioFormats != null && audioFormats.Count > 0)
            {
                Capabilities = new List<SDPMediaFormat>();

                foreach (var format in audioFormats)
                {
                    switch (format)
                    {
                        case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.PCMU:
                            Capabilities.Add(new SDPMediaFormat(SDPMediaFormatsEnum.PCMU));
                            break;
                        case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.PCMA:
                            Capabilities.Add(new SDPMediaFormat(SDPMediaFormatsEnum.PCMA));
                            break;
                        case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.G722:
                            Capabilities.Add(new SDPMediaFormat(SDPMediaFormatsEnum.G722));
                            break;
                        default:
                            // Audio codec without encoder support. It will be up to the application
                            // to package appropriately and send via SendRawRtp calls.
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Add a local video track.
        /// </summary>
        /// <param name="videoFormats">The video formats that the local application supports.</param>
        /// <param name="streamStatus">Optional. The stream status for the video track, e.g. whether
        /// send and receive or only one of.</param>
        public MediaStreamTrack(
            List<VideoCodecsEnum> videoFormats,
            MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv)
        {
            Kind = SDPMediaTypesEnum.video;
            IsRemote = false;
            StreamStatus = streamStatus;
            DefaultStreamStatus = streamStatus;
            Ssrc = Convert.ToUInt32(Crypto.GetRandomInt(0, Int32.MaxValue));
            SeqNum = Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));

            if (videoFormats != null && videoFormats.Count > 0)
            {
                Capabilities = new List<SDPMediaFormat>();

                foreach (var format in videoFormats)
                {
                    switch (format)
                    {
                        case SIPSorceryMedia.Abstractions.V1.VideoCodecsEnum.VP8:
                            Capabilities.Add(new SDPMediaFormat(SDPMediaFormatsEnum.VP8));
                            break;
                        case SIPSorceryMedia.Abstractions.V1.VideoCodecsEnum.H264:
                            Capabilities.Add(
                                new SDPMediaFormat(SDPMediaFormatsEnum.H264)
                                {
                                    FormatParameterAttribute = "packetization-mode=1"
                                });
                            break;
                        default:
                            // Video codec without inbuilt packetisation support. It will be up to the application
                            // to package appropriately and send via SendRawRtp calls.
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether the payload ID in an RTP packet received from the remote call party
        /// is in this track's list.
        /// </summary>
        /// <param name="payloadID">The payload ID to check against.</param>
        /// <returns>True if the payload ID matches one of the codecs for this stream. False if not.</returns>
        public bool IsPayloadIDMatch(int payloadID)
        {
            return Capabilities.Any(x => x.FormatID == payloadID.ToString());
        }

        /// <summary>
        /// Creates and returns a copy of the media stream track.
        /// </summary>
        //public MediaStreamTrack CopyOf()
        //{
        //    List<SDPMediaFormat> capabilitiesCopy = new List<SDPMediaFormat>(Capabilities);
        //    var copy = new MediaStreamTrack(Kind, IsRemote, capabilitiesCopy, StreamStatus);
        //    copy.Ssrc = Ssrc;
        //    copy.SeqNum = SeqNum;
        //    copy.Timestamp = Timestamp;
        //    return copy;
        //}

        /// <summary>
        /// Checks whether a SSRC value from an RTP header or RTCP report matches
        /// a value expected for this track.
        /// </summary>
        /// <param name="ssrc">The SSRC value to check.</param>
        /// <returns>True if the SSRC value is expected for this track. False if not.</returns>
        public bool IsSsrcMatch(uint ssrc)
        {
            return ssrc == Ssrc || SdpSsrc.ContainsKey(ssrc);
        }
    }
}
