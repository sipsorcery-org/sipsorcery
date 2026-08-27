//-----------------------------------------------------------------------------
// Filename: SDPMediaAnnouncement.cs
//
// Description: 
//
// Remarks:
// 
// An example of an "application" type media writer use is negotiating
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
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
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
        public const string MEDIA_FORMAT_PATH_MSRP_PREFIX = "a=" + MEDIA_FORMAT_PATH_MSRP_NAME + ":" + MEDIA_FORMAT_PATH_MSRP_SCHEME + ":";
        public const string MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME = "accept-types";
        public const string MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX = "a=" + MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME + ":";
        public const string TIAS_BANDWIDTH_ATTRIBUE_NAME = "TIAS";
        public const string TIAS_BANDWIDTH_ATTRIBUE_PREFIX = "b=" + TIAS_BANDWIDTH_ATTRIBUE_NAME + ":";

        public const MediaStreamStatusEnum DEFAULT_STREAM_STATUS = MediaStreamStatusEnum.SendRecv;

        public const string m_CRLF = "\r\n";

        private static readonly ILogger logger = LogFactory.CreateLogger<SDPMediaAnnouncement>();

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

        public void ParseMediaFormats(string formatList) => ParseMediaFormats(formatList.AsSpan());

        public void ParseMediaFormats(ReadOnlySpan<char> formatList)
        {
            formatList = formatList.Trim();
            if (formatList.IsEmpty)
            {
                return;
            }

            foreach (var formatIDRange in formatList.SplitAny())
            {
                var formatIDSpan = formatList[formatIDRange];

                if (Media == SDPMediaTypesEnum.application)
                {
                    var formatID = formatIDSpan.ToString();
                    ApplicationMediaFormats.Add(formatID, new SDPApplicationMediaFormat(formatID));
                }
                else if (Media == SDPMediaTypesEnum.message)
                {
                    //TODO
                }
                else
                {
                    if (int.TryParse(formatIDSpan, out var id)
                        && !MediaFormats.ContainsKey(id)
                        && id < SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN)
                    {
                        if (Enum.IsDefined(typeof(SDPWellKnownMediaFormatsEnum), id) &&
                            Enum.TryParse<SDPWellKnownMediaFormatsEnum>(formatIDSpan, out var wellKnown))
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

        public override string ToString()
        {
            var builder = new StringBuilder();
            WriteString(builder);
            return builder.ToString();
        }

        public void WriteString(StringBuilder builder)
        {
            builder
                .Append("m=")
                .Append(GetMediaTypeName(Media))
                .Append(' ')
                .Append(Port)
                .Append(' ')
                .Append(Transport)
                .Append(' ');
            WriteFormatListString(builder);
            builder.Append(m_CRLF);

            if (!string.IsNullOrWhiteSpace(MediaDescription))
            {
                builder.Append("i=").Append(MediaDescription).Append(m_CRLF);
            }

            if (Connection != null)
            {
                Connection.WriteString(builder);
            }

            if (TIASBandwidth > 0)
            {
                builder.Append(TIAS_BANDWIDTH_ATTRIBUE_PREFIX).Append(TIASBandwidth).Append(m_CRLF);
            }

            foreach (var bandwidthAttribute in BandwidthAttributes)
            {
                builder.Append("b=").Append(bandwidthAttribute).Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IceUfrag))
            {
                builder.Append("a=" + SDP.ICE_UFRAG_ATTRIBUTE_PREFIX + ":").Append(IceUfrag).Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IcePwd))
            {
                builder.Append("a=" + SDP.ICE_PWD_ATTRIBUTE_PREFIX + ":").Append(IcePwd).Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(DtlsFingerprint))
            {
                builder.Append("a=" + SDP.DTLS_FINGERPRINT_ATTRIBUTE_PREFIX + ":").Append(DtlsFingerprint).Append(m_CRLF);
            }

            if (IceRole is { } iceRole)
            {
                builder.Append("a=" + SDP.ICE_SETUP_ATTRIBUTE_PREFIX + ":").Append(GetIceRoleName(iceRole)).Append(m_CRLF);
            }

            if (IceCandidates?.Count > 0)
            {
                foreach (var candidate in IceCandidates)
                {
                    builder.Append("a=" + SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX + ":").Append(candidate).Append(m_CRLF);
                }
            }

            if (IceOptions != null)
            {
                builder.Append("a=" + SDP.ICE_OPTIONS + ":").Append(IceOptions).Append(m_CRLF);
            }

            if (IceEndOfCandidates)
            {
                builder.Append("a=" + SDP.END_ICE_CANDIDATES_ATTRIBUTE).Append(m_CRLF);
            }

            if (!string.IsNullOrWhiteSpace(MediaID))
            {
                builder.Append("a=" + SDP.MEDIA_ID_ATTRIBUTE_PREFIX + ":").Append(MediaID).Append(m_CRLF);
            }

            WriteFormatListAttributesString(builder);

            foreach (var headerExtension in HeaderExtensions)
            {
                builder.Append(MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX).Append(headerExtension.Value.Id).Append(' ')
                    .Append(headerExtension.Value.Uri).Append(m_CRLF);
            }

            foreach (var extra in ExtraMediaAttributes)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    builder.Append(extra).Append(m_CRLF);
                }
            }

            foreach (var desc in this.SecurityDescriptions)
            {
                if (desc.WriteString(builder))
                {
                    builder.Append(m_CRLF);
                }
            }

            if (MediaStreamStatus != null)
            {
                builder.Append(MediaStreamStatusType.GetAttributeForMediaStreamStatus(MediaStreamStatus.Value)).Append(m_CRLF);
            }

            if (SsrcGroupID != null && SsrcAttributes.Count > 0)
            {
                builder.Append(MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX).Append(SsrcGroupID);
                foreach (var ssrcAttr in SsrcAttributes)
                {
                    builder.Append(' ').Append(ssrcAttr.SSRC);
                }
                builder.Append(m_CRLF);
            }

            if (SsrcAttributes.Count > 0)
            {
                foreach (var ssrcAttr in SsrcAttributes)
                {
                    if (!string.IsNullOrWhiteSpace(ssrcAttr.Cname))
                    {
                        builder.Append(MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX).Append(ssrcAttr.SSRC).Append(' ')
                            .Append(SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX).Append(':').Append(ssrcAttr.Cname).Append(m_CRLF);
                    }
                    else
                    {
                        builder.Append(MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX).Append(ssrcAttr.SSRC).Append(m_CRLF);
                    }
                }
            }

            // If the "sctpmap" attribute is set, use it instead of the separate "sctpport" and "max-message-size"
            // attributes. They both contain the same information. The "sctpmap" is the legacy attribute and if
            // an application sets it then it's likely to be for a specific reason.
            if (SctpMap != null)
            {
                builder.Append(MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX).Append(SctpMap).Append(m_CRLF);
            }
            else
            {
                if (SctpPort != null)
                {
                    builder.Append(MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX).Append(SctpPort).Append(m_CRLF);
                }

                if (MaxMessageSize != 0)
                {
                    builder.Append(MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX).Append(MaxMessageSize).Append(m_CRLF);
                }
            }

            // TODO: use https://www.nuget.org/packages/NetEscapades.EnumGenerators
            static string GetMediaTypeName(SDPMediaTypesEnum media) => media switch
            {
                SDPMediaTypesEnum.invalid => nameof(SDPMediaTypesEnum.invalid),
                SDPMediaTypesEnum.audio => nameof(SDPMediaTypesEnum.audio),
                SDPMediaTypesEnum.video => nameof(SDPMediaTypesEnum.video),
                SDPMediaTypesEnum.application => nameof(SDPMediaTypesEnum.application),
                SDPMediaTypesEnum.data => nameof(SDPMediaTypesEnum.data),
                SDPMediaTypesEnum.control => nameof(SDPMediaTypesEnum.control),
                SDPMediaTypesEnum.image => nameof(SDPMediaTypesEnum.image),
                SDPMediaTypesEnum.message => nameof(SDPMediaTypesEnum.message),
                SDPMediaTypesEnum.text => nameof(SDPMediaTypesEnum.text),
                _ => media.ToString()
            };

            // TODO: use https://www.nuget.org/packages/NetEscapades.EnumGenerators
            static string GetIceRoleName(IceRolesEnum iceRole) => iceRole switch
            {
                IceRolesEnum.actpass => nameof(IceRolesEnum.actpass),
                IceRolesEnum.passive => nameof(IceRolesEnum.passive),
                IceRolesEnum.active => nameof(IceRolesEnum.active),
                _ => iceRole.ToString()
            };
        }

        public string GetFormatListToString()
        {
            if (Media != SDPMediaTypesEnum.application &&
                Media != SDPMediaTypesEnum.message &&
                MediaFormats.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            WriteFormatListString(builder);
            return builder.ToString();
        }

        public void WriteFormatListString(StringBuilder builder)
        {
            if (Media == SDPMediaTypesEnum.application)
            {
                var writeSparator = false;
                foreach (var appFormat in ApplicationMediaFormats)
                {
                    if (writeSparator)
                    {
                        builder.Append(' ');
                    }
                    writeSparator = true;

                    builder.Append(appFormat.Key);
                }
            }
            else if (Media == SDPMediaTypesEnum.message)
            {
                builder.Append('*');
            }
            else
            {
                var writeSparator = false;
                foreach (var mediaFormat in MediaFormats)
                {
                    if (writeSparator)
                    {
                        builder.Append(' ');
                    }
                    writeSparator = true;

                    builder.Append(mediaFormat.Key);
                }
            }
        }

        public string GetFormatListAttributesToString()
        {
            if ((Media == SDPMediaTypesEnum.application && ApplicationMediaFormats.Count == 0) ||
                (Media != SDPMediaTypesEnum.application && Media != SDPMediaTypesEnum.message &&
                    (MediaFormats == null || MediaFormats.Count == 0)))
            {
                return null;
            }

            var builder = new StringBuilder();
            WriteFormatListAttributesString(builder);
            return builder.ToString();
        }

        public void WriteFormatListAttributesString(StringBuilder builder)
        {
            if (Media == SDPMediaTypesEnum.application)
            {
                foreach (var appFormat in ApplicationMediaFormats)
                {
                    if (appFormat.Value.Rtpmap != null)
                    {
                        builder.Append(MEDIA_FORMAT_ATTRIBUTE_PREFIX).Append(appFormat.Key).Append(' ')
                            .Append(appFormat.Value.Rtpmap).Append(m_CRLF);
                    }

                    if (appFormat.Value.Fmtp != null)
                    {
                        builder.Append(MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX).Append(appFormat.Key).Append(' ')
                            .Append(appFormat.Value.Fmtp).Append(m_CRLF);
                    }
                }
            }
            else if (Media == SDPMediaTypesEnum.message)
            {
                var mediaFormat = MessageMediaFormat;
                var acceptTypes = mediaFormat.AcceptTypes;
                if (acceptTypes != null && acceptTypes.Count > 0)
                {
                    builder.Append(MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX);
                    foreach (var type in acceptTypes)
                    {
                        builder.Append(type).Append(' ');
                    }

                    builder.Append(m_CRLF);
                }

                if (mediaFormat.Endpoint != null)
                {
                    builder.Append(MEDIA_FORMAT_PATH_MSRP_PREFIX).Append("//").Append(Connection.ConnectionAddress).Append(':')
                        .Append(Port).Append('/').Append(mediaFormat.Endpoint).Append(m_CRLF);
                }
            }
            else
            {
                if (MediaFormats != null)
                {
                    foreach (var entry in MediaFormats)
                    {
                        var mediaFormat = entry.Value;
                        if (mediaFormat.Rtpmap == null)
                        {
                            // Well known media formats are not required to add an rtpmap but we do so any way as some SIP
                            // stacks don't work without it.
                            builder.Append(MEDIA_FORMAT_ATTRIBUTE_PREFIX).Append(mediaFormat.ID).Append(' ')
                                .Append(mediaFormat.Name()).Append('/').Append(mediaFormat.ClockRate()).Append(m_CRLF);
                        }
                        else
                        {
                            builder.Append(MEDIA_FORMAT_ATTRIBUTE_PREFIX).Append(mediaFormat.ID).Append(' ')
                                .Append(mediaFormat.Rtpmap).Append(m_CRLF);
                        }

                        // Leaving out the feedback attribute for now. It should only be added where it's present in a parsed SDP packet or
                        // is opted in in a produced SDP packet. AC 7 Nov 2024, see also https://github.com/sipsorcery-org/sipsorcery/issues/1130.

                        if (mediaFormat.Kind == SDPMediaTypesEnum.video)
                        {
                            foreach (var rtcpFeedbackMessage in mediaFormat.SupportedRtcpFeedbackMessages)
                            {
                                builder.Append(MEDIA_FORMAT_FEEDBACK_PREFIX).Append(mediaFormat.ID).Append(' ')
                                    .Append(rtcpFeedbackMessage).Append(m_CRLF);
                            }
                        }

                        if (mediaFormat.Fmtp != null)
                        {
                            builder.Append(MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX).Append(mediaFormat.ID).Append(' ')
                                .Append(mediaFormat.Fmtp).Append(m_CRLF);
                        }
                    }
                }
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
