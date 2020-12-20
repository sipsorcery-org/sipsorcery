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
// 15 Oct 2020  Aaron Clauson   Added media format map lookup class.
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
        /// The last seqnum received from the remote peer for this stream.
        /// </summary>
        public ushort LastRemoteSeqNum { get; internal set; }

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
        public List<SDPAudioVideoMediaFormat> Capabilities { get; internal set; }

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
        /// <param name="capabilities">The capabilities for the track being added. Where the same media
        /// type is supported locally and remotely only the mutual capabilities can be used. This will
        /// occur if we receive an SDP offer (add track initiated by the remote party) and we need
        /// to remove capabilities we don't support.</param>
        /// <param name="streamStatus">The initial stream status for the media track. Defaults to
        /// send receive.</param>
        /// <param name="ssrcAttributes">If th track is being created from an SDP announcement this
        /// parameter contains a list of </param>
        public MediaStreamTrack(
            SDPMediaTypesEnum kind,
            bool isRemote,
            List<SDPAudioVideoMediaFormat> capabilities,
            MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv,
            List<SDPSsrcAttribute> ssrcAttributes = null)
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

            // Add the source attributes from the remote SDP to help match RTP SSRC and RTCP CNAME values against
            // RTP and RTCP packets received from the remote party.
            if (ssrcAttributes?.Count > 0)
            {
                foreach (var ssrcAttr in ssrcAttributes)
                {
                    SdpSsrc.Add(ssrcAttr.SSRC, ssrcAttr);
                }
            }
        }

        /// <summary>
        /// Add a local audio track.
        /// </summary>
        /// <param name="format">The audio format that the local application supports.</param>
        /// <param name="streamStatus">Optional. The stream status for the audio track, e.g. whether
        /// send and receive or only one of.</param>
        public MediaStreamTrack(
            AudioFormat format,
            MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) :
            this(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(format) }, streamStatus)
        { }

        /// <summary>
        /// Add a local audio track.
        /// </summary>
        /// <param name="audioFormats">The audio formats that the local application supports.</param>
        /// <param name="streamStatus">Optional. The stream status for the audio track, e.g. whether
        /// send and receive or only one of.</param>
        public MediaStreamTrack(
        List<AudioFormat> formats,
        MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) :
             this(SDPMediaTypesEnum.audio, false, formats.Select(x => new SDPAudioVideoMediaFormat(x)).ToList(), streamStatus)
        { }

        /// <summary>
        /// Add a local video track.
        /// </summary>
        /// <param name="format">The video format that the local application supports.</param>
        /// <param name="streamStatus">Optional. The stream status for the video track, e.g. whether
        /// send and receive or only one of.</param>
        public MediaStreamTrack(
           VideoFormat format,
           MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) :
            this(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(format) }, streamStatus)
        { }

        /// <summary>
        /// Add a local video track.
        /// </summary>
        /// <param name="videoFormats">The video formats that the local application supports.</param>
        /// <param name="streamStatus">Optional. The stream status for the video track, e.g. whether
        /// send and receive or only one of.</param>
        public MediaStreamTrack(
        List<VideoFormat> formats,
        MediaStreamStatusEnum streamStatus = MediaStreamStatusEnum.SendRecv) :
             this(SDPMediaTypesEnum.video, false, formats.Select(x => new SDPAudioVideoMediaFormat(x)).ToList(), streamStatus)
        { }

        /// <summary>
        /// Adds a local audio track based on one or more well known audio formats.
        /// There is no equivalent for a local video track as there is no support in this library for any of
        /// the well known video formats.
        /// </summary>
        /// <param name="wellKnownAudioFormats">One or more well known audio formats.</param>
        public MediaStreamTrack(params SDPWellKnownMediaFormatsEnum[] wellKnownAudioFormats)
            : this(wellKnownAudioFormats.Select(x => new AudioFormat(x)).ToList())
        { }

        /// <summary>
        /// Checks whether the payload ID in an RTP packet received from the remote call party
        /// is in this track's list.
        /// </summary>
        /// <param name="payloadID">The payload ID to check against.</param>
        /// <returns>True if the payload ID matches one of the codecs for this stream. False if not.</returns>
        public bool IsPayloadIDMatch(int payloadID)
        {
            return Capabilities?.Any(x => x.ID == payloadID) == true;
        }

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
