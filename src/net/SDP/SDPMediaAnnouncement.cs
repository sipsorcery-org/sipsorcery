//-----------------------------------------------------------------------------
// Filename: SDPMediaAnnouncement.cs
//
// Description: 
//
// Remarks:
// 
// An example of an "application" type media announcement use is negotiating
// SCTP-over-DTLS which acts as the transport for WebRTC data channels.
// https://tools.ietf.org/html/rfc8841
// "Session Description Protocol (SDP) Offer/Answer Procedures for Stream
// Control Transmission Protocol (SCTP) over Datagram Transport Layer
// Security (DTLS) Transport"
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// Jacek Dzija
// Mateusz Greczek
//
// History:
// ??	Aaron Clauson	Created, Hobart, Australia.
// rj2: add SDPSecurityDescription parser
// 30 Mar 2021 Jacek Dzija,Mateusz Greczek Added MSRP
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An attribute used to defined additional properties about
    /// a media source and the relationship between them.
    /// As specified in RFC5576, https://tools.ietf.org/html/rfc5576.
    /// </summary>
    public class SDPSsrcAttribute
    {
        public const string MEDIA_CNAME_ATTRIBUE_PREFIX = "cname";

        public uint SSRC { get; set; }

        public string Cname { get; set; }

        public string GroupID { get; set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="ssrc">The SSRC that should match an RTP stream.</param>
        /// <param name="cname">Optional. The CNAME value to use in RTCP SDES sections.</param>
        /// <param name="groupID">Optional. If this "ssrc" attribute is part of a 
        /// group this is the group ID.</param>
        public SDPSsrcAttribute(uint ssrc, string cname, string groupID)
        {
            SSRC = ssrc;
            Cname = cname;
            GroupID = groupID;
        }
    }

    public class SDPMediaAnnouncement
    {
        public const string MEDIA_EXTENSION_MAP_ATTRIBUE_NAME = "extmap";
        public const string MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX = "a=" + MEDIA_EXTENSION_MAP_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_ATTRIBUTE_NAME = "rtpmap";
        public const string MEDIA_FORMAT_ATTRIBUTE_PREFIX = "a=" + MEDIA_FORMAT_ATTRIBUTE_NAME + ":";
        public const string MEDIA_FORMAT_FEEDBACK_PREFIX = "a=rtcp-fb:";
        public const string MEDIA_FORMAT_PARAMETERS_ATTRIBUE_NAME = "fmtp";
        public const string MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX = "a=" + MEDIA_FORMAT_PARAMETERS_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_SSRC_ATTRIBUE_NAME = "ssrc";
        public const string MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX = "a=" + MEDIA_FORMAT_SSRC_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_NAME = "ssrc-group";
        public const string MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX = "a=" + MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_NAME = "sctpmap";
        public const string MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX = "a=" + MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_NAME = "sctp-port";
        public const string MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX = "a=" + MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_NAME = "max-message-size";
        public const string MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX = "a=" + MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_NAME + ":";
        public const string MEDIA_FORMAT_PATH_MSRP_NAME = "path";
        public const string MEDIA_FORMAT_PATH_MSRP_SCHEME = "msrp";
        public const string MEDIA_FORMAT_PATH_MSRP_PREFIX = "a="+ MEDIA_FORMAT_PATH_MSRP_NAME + ":"+"msrp:"+":";
        public const string MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME = "accept-types";
        public const string MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX = "a=" + MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME + ":";
        public const string TIAS_BANDWIDTH_ATTRIBUE_NAME = "TIAS";
        public const string TIAS_BANDWIDTH_ATTRIBUE_PREFIX = "b=" + TIAS_BANDWIDTH_ATTRIBUE_NAME + ":";

        public const MediaStreamStatusEnum DEFAULT_STREAM_STATUS = MediaStreamStatusEnum.SendRecv;

        public const string m_CRLF = "\r\n";

        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        public SDPConnectionInformation Connection;

        // Media Announcement fields.
        public SDPMediaTypesEnum Media = SDPMediaTypesEnum.audio;   // Media type for the stream.
        public int Port;                        // For UDP transports should be in the range 1024 to 65535 and for RTP compliance should be even (only even ports used for data).
        /// <summary>
        /// Gets or sets the number of consecutive ports specified for the media stream in the SDP.
        /// When the SDP media line includes a port range (e.g., "30000/2"), this property holds the count of ports.
        /// Typically, a value of 2 indicates that two ports are allocated: one for RTP and the following port for RTCP.
        /// </summary>
        public int PortCount { get; set; }
        public string Transport = "RTP/AVP";    // Defined types RTP/AVP (RTP Audio Visual Profile) and udp.
        public string IceUfrag;                 // If ICE is being used the username for the STUN requests.
        public string IcePwd;                   // If ICE is being used the password for the STUN requests.
        public string IceOptions;               // Optional attribute to specify support ICE options, e.g. "trickle".
        public bool IceEndOfCandidates;         // If ICE candidate trickling is being used this needs to be set if all candidates have been gathered.
        public IceRolesEnum? IceRole = null;
        public string DtlsFingerprint;          // If DTLS handshake is being used this is the fingerprint or our DTLS certificate.
        public int MLineIndex = 0;

        /// <summary>
        /// If being used in a bundle this the ID for the announcement.
        /// Example: a=mid:audio or a=mid:video.
        /// </summary>
        public string MediaID;

        /// <summary>
        /// The "ssrc" attributes group ID as specified in RFC5576.
        /// </summary>
        public string SsrcGroupID;

        /// <summary>
        /// The "sctpmap" attribute defined in https://tools.ietf.org/html/draft-ietf-mmusic-sctp-sdp-26 for
        /// use in WebRTC data channels.
        /// </summary>
        public string SctpMap;

        /// <summary>
        /// The "sctp-port" attribute defined in https://tools.ietf.org/html/draft-ietf-mmusic-sctp-sdp-26 for
        /// use in WebRTC data channels.
        /// </summary>
        public ushort? SctpPort = null;

        /// <summary>
        /// The "max-message-size" attribute defined in https://tools.ietf.org/html/draft-ietf-mmusic-sctp-sdp-26 for
        /// use in WebRTC data channels.
        /// </summary>
        public long MaxMessageSize = 0;

        /// <summary>
        /// If the RFC5576 is being used this is the list of "ssrc" attributes
        /// supplied.
        /// </summary>
        public List<SDPSsrcAttribute> SsrcAttributes = new List<SDPSsrcAttribute>();

        /// <summary>
        /// Optional Transport Independent Application Specific Maximum (TIAS) bandwidth.
        /// </summary>
        public uint TIASBandwidth = 0;

        public List<string> BandwidthAttributes = new List<string>();

        /// <summary>
        /// In media definitions, "i=" fields are primarily intended for labelling media streams https://tools.ietf.org/html/rfc4566#page-12
        /// </summary>
        public string MediaDescription;

        /// <summary>
        ///  For AVP these will normally be a media payload type as defined in the RTP Audio/Video Profile.
        /// </summary>
        public Dictionary<int, SDPAudioVideoMediaFormat> MediaFormats = new Dictionary<int, SDPAudioVideoMediaFormat>();

        /// <summary>
        ///  a=extmap - Mapping for RTP header extensions
        /// </summary>
        public Dictionary<int, RTPHeaderExtension> HeaderExtensions = new Dictionary<int, RTPHeaderExtension>();

        /// <summary>
        ///  For AVP these will normally be a media payload type as defined in the RTP Audio/Video Profile.
        /// </summary>
        public SDPMessageMediaFormat MessageMediaFormat = new SDPMessageMediaFormat();

        /// <summary>
        /// List of media formats for "application media announcements. Application media announcements have different
        /// semantics to audio/video announcements. They can also use aribtrary strings as the format ID.
        /// </summary>
        public Dictionary<string, SDPApplicationMediaFormat> ApplicationMediaFormats = new Dictionary<string, SDPApplicationMediaFormat>();

        public List<string> ExtraMediaAttributes = new List<string>();          // Attributes that were not recognised.
        public List<SDPSecurityDescription> SecurityDescriptions = new List<SDPSecurityDescription>(); //2018-12-21 rj2: add a=crypto parsing etc.
        public List<string> IceCandidates;

        /// <summary>
        /// The stream status of this media announcement.
        /// </summary>
        public MediaStreamStatusEnum? MediaStreamStatus { get; set; }

        public SDPMediaAnnouncement()
        { }

        public SDPMediaAnnouncement(int port)
        {
            Port = port;
        }

        public SDPMediaAnnouncement(SDPConnectionInformation connection)
        {
            Connection = connection;
        }

        public SDPMediaAnnouncement(SDPMediaTypesEnum mediaType, int port, List<SDPAudioVideoMediaFormat> mediaFormats)
        {
            Media = mediaType;
            Port = port;
            MediaStreamStatus = DEFAULT_STREAM_STATUS;

            if (mediaFormats != null)
            {
                foreach (var fmt in mediaFormats)
                {
                    if (!MediaFormats.ContainsKey(fmt.ID))
                    {
                        MediaFormats.Add(fmt.ID, fmt);
                    }
                }
            }
        }

        public SDPMediaAnnouncement(SDPMediaTypesEnum mediaType, int port, List<SDPApplicationMediaFormat> appMediaFormats)
        {
            Media = mediaType;
            Port = port;

            if (appMediaFormats != null)
            {
                foreach (var fmt in appMediaFormats)
                {
                    if (!ApplicationMediaFormats.ContainsKey(fmt.ID))
                    {
                        ApplicationMediaFormats.Add(fmt.ID, fmt);
                    }
                }
            }
        }

        public SDPMediaAnnouncement(SDPMediaTypesEnum mediaType, SDPConnectionInformation connection, int port, SDPMessageMediaFormat messageMediaFormat)
        {
            Media = mediaType;
            Port = port;
            Connection = connection;

            MessageMediaFormat = messageMediaFormat;
        }

        public void ParseMediaFormats(string formatList)
        {
            if (!String.IsNullOrWhiteSpace(formatList))
            {
                string[] formatIDs = Regex.Split(formatList, @"\s");
                if (formatIDs != null)
                {
                    foreach (string formatID in formatIDs)
                    {
                        if (Media == SDPMediaTypesEnum.application)
                        {
                            ApplicationMediaFormats.Add(formatID, new SDPApplicationMediaFormat(formatID));
                        }
                        else if (Media == SDPMediaTypesEnum.message)
                        {
                            //TODO
                        }
                        else
                        {
                            if (Int32.TryParse(formatID, out int id)
                                && !MediaFormats.ContainsKey(id)
                                && id < SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN)
                            {
                                if (Enum.IsDefined(typeof(SDPWellKnownMediaFormatsEnum), id) &&
                                    Enum.TryParse<SDPWellKnownMediaFormatsEnum>(formatID, out var wellKnown))
                                {
                                    MediaFormats.Add(id, new SDPAudioVideoMediaFormat(wellKnown));
                                }
                                else
                                {
                                    logger.LogWarning("Excluding unrecognised well known media format ID {FormatID}.", id);
                                }
                            }
                        }
                    }
                }
            }
        }

        public override string ToString()
        {
            var builder = new ValueStringBuilder();

            try
            {
                ToString(ref builder);

                return builder.ToString();
            }
            finally
            {
                builder.Dispose();
            }
        }

        internal void ToString(ref ValueStringBuilder builder)
        {
            builder.Append("m=");
            builder.Append(Media.ToString());
            builder.Append(' ');
            builder.Append(Port);
            builder.Append(' ');
            builder.Append(Transport);
            builder.Append(' ');
            WriteFormatListString(ref builder);
            builder.Append(m_CRLF);

            if (!string.IsNullOrWhiteSpace(MediaDescription))
            {
                builder.Append("i=");
                builder.Append(MediaDescription);
                builder.Append(m_CRLF);
            }

            if (Connection != null)
            {
                builder.Append(Connection.ToString());
            }

            if (TIASBandwidth > 0)
            {
                builder.Append(TIAS_BANDWIDTH_ATTRIBUE_PREFIX);
                builder.Append(TIASBandwidth);
                builder.Append(m_CRLF);
            }

            foreach (string bandwidthAttribute in BandwidthAttributes)
            {
                builder.Append("b=");
                builder.Append(bandwidthAttribute);
                builder.Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IceUfrag))
            {
                builder.Append("a=");
                builder.Append(SDP.ICE_UFRAG_ATTRIBUTE_PREFIX);
                builder.Append(':');
                builder.Append(IceUfrag);
                builder.Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IcePwd))
            {
                builder.Append("a=");
                builder.Append(SDP.ICE_PWD_ATTRIBUTE_PREFIX);
                builder.Append(':');
                builder.Append(IcePwd);
                builder.Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(DtlsFingerprint))
            {
                builder.Append("a=");
                builder.Append(SDP.DTLS_FINGERPRINT_ATTRIBUTE_PREFIX);
                builder.Append(':');
                builder.Append(DtlsFingerprint);
                builder.Append(m_CRLF);
            }

            if (IceRole != null)
            {
                builder.Append("a=");
                builder.Append(SDP.ICE_SETUP_ATTRIBUTE_PREFIX);
                builder.Append(':');
                builder.Append(IceRole.ToString());
                builder.Append(m_CRLF);
            }

            if (IceCandidates?.Any() == true)
            {
                foreach (var candidate in IceCandidates)
                {
                    builder.Append("a=");
                    builder.Append(SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX);
                    builder.Append(':');
                    builder.Append(candidate);
                    builder.Append(m_CRLF);
                }
            }

            if (IceOptions != null)
            {
                builder.Append("a=");
                builder.Append(SDP.ICE_OPTIONS);
                builder.Append(':');
                builder.Append(IceOptions);
                builder.Append(m_CRLF);
            }

            if (IceEndOfCandidates)
            {
                builder.Append("a=");
                builder.Append(SDP.END_ICE_CANDIDATES_ATTRIBUTE);
                builder.Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(MediaID))
            {
                builder.Append("a=");
                builder.Append(SDP.MEDIA_ID_ATTRIBUTE_PREFIX);
                builder.Append(':');
                builder.Append(MediaID);
                builder.Append(m_CRLF);
            }

            builder.Append(GetFormatListAttributesToString());

            foreach (var ext in HeaderExtensions)
            {
                builder.Append(MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX);
                builder.Append(ext.Value.Id);
                builder.Append(' ');
                builder.Append(ext.Value.Uri);
                builder.Append(m_CRLF);
            }

            foreach (string extra in ExtraMediaAttributes)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    builder.Append(extra);
                    builder.Append(m_CRLF);
                }
            }

            foreach (SDPSecurityDescription desc in this.SecurityDescriptions)
            {
                builder.Append(desc.ToString());
                builder.Append(m_CRLF);
            }

            if (MediaStreamStatus != null)
            {
                builder.Append(MediaStreamStatusType.GetAttributeForMediaStreamStatus(MediaStreamStatus.Value));
                builder.Append(m_CRLF);
            }

            if (SsrcGroupID != null && SsrcAttributes.Count > 0)
            {
                builder.Append(MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX);
                builder.Append(SsrcGroupID);
                foreach (var ssrcAttr in SsrcAttributes)
                {
                    builder.Append(' ');
                    builder.Append(ssrcAttr.SSRC);
                }
                builder.Append(m_CRLF);
            }

            if (SsrcAttributes.Count > 0)
            {
                foreach (var ssrcAttr in SsrcAttributes)
                {
                    builder.Append(MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX);
                    builder.Append(ssrcAttr.SSRC);
                    if (!string.IsNullOrWhiteSpace(ssrcAttr.Cname))
                    {
                        builder.Append(' ');
                        builder.Append(SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX);
                        builder.Append(':');
                        builder.Append(ssrcAttr.Cname);
                    }
                    builder.Append(m_CRLF);
                }
            }

            // If the "sctpmap" attribute is set, use it instead of the separate "sctpport" and "max-message-size"
            // attributes. They both contain the same information. The "sctpmap" is the legacy attribute and if
            // an application sets it then it's likely to be for a specific reason.
            if (SctpMap != null)
            {
                builder.Append(MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX);
                builder.Append(SctpMap);
                builder.Append(m_CRLF);
            }
            else
            {
                if (SctpPort != null)
                {
                    builder.Append(MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX);
                    builder.Append(SctpPort);
                    builder.Append(m_CRLF);
                }

                if (MaxMessageSize != 0)
                {
                    builder.Append(MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX);
                    builder.Append(MaxMessageSize);
                    builder.Append(m_CRLF);
                }
            }
        }

        public string GetFormatListToString()
        {
            if (Media == SDPMediaTypesEnum.message)
            {
                return "*";
            }

            var builder = new ValueStringBuilder();

            try
            {
                WriteFormatListString(ref builder);

                if (Media == SDPMediaTypesEnum.application)
                {
                    return builder.ToString();
                }
                else
                {
                    return builder.Length > 0 ? builder.ToString() : null;
                }
            }
            finally
            {
                builder.Dispose();
            }
        }

        internal void WriteFormatListString(ref ValueStringBuilder builder)
        {
            if (Media == SDPMediaTypesEnum.message)
            {
                builder.Append('*');
            }
            else if (Media == SDPMediaTypesEnum.application)
            {
                var first = true;
                foreach (var appFormat in ApplicationMediaFormats)
                {
                    if (!first)
                    {
                        builder.Append(' ');
                    }
                    builder.Append(appFormat.Key);
                    first = false;
                }
            }
            else
            {
                var first = true;
                foreach (var mediaFormat in MediaFormats)
                {
                    if (!first)
                    {
                        builder.Append(' ');
                    }
                    builder.Append(mediaFormat.Key);
                    first = false;
                }
            }
        }

        public string GetFormatListAttributesToString()
        {
            if (Media == SDPMediaTypesEnum.application)
            {
                if (ApplicationMediaFormats.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var appFormat in ApplicationMediaFormats)
                    {
                        if (appFormat.Value.Rtpmap != null)
                        {
                            sb.Append($"{MEDIA_FORMAT_ATTRIBUTE_PREFIX}{appFormat.Key} {appFormat.Value.Rtpmap}{m_CRLF}");
                        }

                        if (appFormat.Value.Fmtp != null)
                        {
                            sb.Append($"{MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX}{appFormat.Key} {appFormat.Value.Fmtp}{m_CRLF}");
                        }
                    }

                    return sb.ToString();
                }
                else
                {
                    return null;
                }
            }
            else if (Media == SDPMediaTypesEnum.message)
            {
                StringBuilder sb = new StringBuilder();

                var mediaFormat = MessageMediaFormat;
                var acceptTypes = mediaFormat.AcceptTypes;
                if (acceptTypes != null && acceptTypes.Count > 0)
                {
                    sb.Append(MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX);
                    foreach (var type in acceptTypes)
                    {
                        sb.Append($"{type} ");
                    }

                    sb.Append($"{m_CRLF}");
                }

                if (mediaFormat.Endpoint != null)
                {
                    sb.Append($"{MEDIA_FORMAT_PATH_MSRP_PREFIX}//{Connection.ConnectionAddress}:{Port}/{mediaFormat.Endpoint}{m_CRLF}");
                }

                return sb.ToString();
            }
            else
            {
                string formatAttributes = null;

                if (MediaFormats != null)
                {
                    foreach (var mediaFormat in MediaFormats.Select(y => y.Value))
                    {
                        if (mediaFormat.Rtpmap == null)
                        {
                            // Well known media formats are not required to add an rtpmap but we do so any way as some SIP
                            // stacks don't work without it.
                            formatAttributes += MEDIA_FORMAT_ATTRIBUTE_PREFIX + mediaFormat.ID + " " + mediaFormat.Name() + "/" + mediaFormat.ClockRate() + m_CRLF;
                        }
                        else
                        {
                            formatAttributes += MEDIA_FORMAT_ATTRIBUTE_PREFIX + mediaFormat.ID + " " + mediaFormat.Rtpmap + m_CRLF;
                        }

                        // Leaving out the feedback attribute for now. It should only be added where it's present in a parsed SDP packet or
                        // is opted in in a produced SDP packet. AC 7 Nov 2024, see also https://github.com/sipsorcery-org/sipsorcery/issues/1130.

                        if (mediaFormat.Kind == SDPMediaTypesEnum.video)
                        {
                            foreach (var rtcpFeedbackMessage in mediaFormat.SupportedRtcpFeedbackMessages)
                            {
                                formatAttributes += MEDIA_FORMAT_FEEDBACK_PREFIX + mediaFormat.ID + " " + rtcpFeedbackMessage + m_CRLF;
                            }
                        }

                        if (mediaFormat.Fmtp != null)
                        {
                            formatAttributes += MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX + mediaFormat.ID + " " + mediaFormat.Fmtp + m_CRLF;
                        }
                    }
                }

                return formatAttributes;
            }
        }

        public void AddExtra(string attribute)
        {
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                ExtraMediaAttributes.Add(attribute);
            }
        }

        public bool HasCryptoLine(SDPSecurityDescription.CryptoSuites cryptoSuite)
        {
            if (this.SecurityDescriptions == null)
            {
                return false;
            }
            foreach (SDPSecurityDescription secdesc in this.SecurityDescriptions)
            {
                if (secdesc.CryptoSuite == cryptoSuite)
                {
                    return true;
                }
            }

            return false;
        }

        public SDPSecurityDescription GetCryptoLine(SDPSecurityDescription.CryptoSuites cryptoSuite)
        {
            if (this.SecurityDescriptions == null)
            {
                return null;
            }
            foreach (SDPSecurityDescription secdesc in this.SecurityDescriptions)
            {
                if (secdesc.CryptoSuite == cryptoSuite)
                {
                    return secdesc;
                }
            }

            return null;
        }

        public void AddCryptoLine(string crypto)
        {
            this.SecurityDescriptions.Add(SDPSecurityDescription.Parse(crypto));
        }

        /// <summary>
        /// Attempts to locate a media format corresponding to telephone events. If available its 
        /// format ID is returned.
        /// </summary>
        /// <returns>If found the format ID for telephone events or -1 if not.</returns>
        public int GetTelephoneEventFormatID()
        {
            foreach (var mediaFormat in MediaFormats.Values)
            {
                if (mediaFormat.Name()?.StartsWith(SDP.TELEPHONE_EVENT_ATTRIBUTE) == true)
                {
                    return mediaFormat.ID;
                }
            }

            return -1;
        }
    }
}
