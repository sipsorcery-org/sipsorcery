//-----------------------------------------------------------------------------
// Filename: SDPMediaAnnouncement.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// ??	Aaron Clauson	Created, Hobart, Australia.
// rj2: add SDPSecurityDescription parser
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
        public const string MEDIA_FORMAT_ATTRIBUE_PREFIX = "a=rtpmap:";
        public const string MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX = "a=fmtp:";
        public const string MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX = "a=ssrc:";
        public const string MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX = "a=ssrc-group:";
        public const string MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX = "a=sctpmap:";
        public const string MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX = "a=sctp-port:";
        public const string MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX = "a=max-message-size:";
        public const string TIAS_BANDWIDTH_ATTRIBUE_PREFIX = "b=TIAS:";
        public const MediaStreamStatusEnum DEFAULT_STREAM_STATUS = MediaStreamStatusEnum.SendRecv;

        public const string m_CRLF = "\r\n";

        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        public SDPConnectionInformation Connection;

        // Media Announcement fields.
        public SDPMediaTypesEnum Media = SDPMediaTypesEnum.audio;   // Media type for the stream.
        public int Port;                        // For UDP transports should be in the range 1024 to 65535 and for RTP compliance should be even (only even ports used for data).
        public string Transport = "RTP/AVP";    // Defined types RTP/AVP (RTP Audio Visual Profile) and udp.
        public string IceUfrag;                 // If ICE is being used the username for the STUN requests.
        public string IcePwd;                   // If ICE is being used the password for the STUN requests.
        public string IceOptions;               // Optional attribute to specify support ICE options, e.g. "trickle".
        public bool IceEndOfCandidates;         // If ICE candidate trickling is being used this needs to be set if all candidates have been gathered.
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

            foreach (var fmt in mediaFormats)
            {
                if (!MediaFormats.ContainsKey(fmt.ID))
                {
                    MediaFormats.Add(fmt.ID, fmt);
                }
            }
        }

        public SDPMediaAnnouncement(SDPMediaTypesEnum mediaType, int port, List<SDPApplicationMediaFormat> appMediaFormats)
        {
            Media = mediaType;
            Port = port;

            foreach (var fmt in appMediaFormats)
            {
                if (!ApplicationMediaFormats.ContainsKey(fmt.ID))
                {
                    ApplicationMediaFormats.Add(fmt.ID, fmt);
                }
            }
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
                                    logger.LogWarning($"Excluding unrecognised well known media format ID {id}.");
                                }
                            }
                        }
                    }
                }
            }
        }

        public override string ToString()
        {
            string announcement = "m=" + Media + " " + Port + " " + Transport + " " + GetFormatListToString() + m_CRLF;

            announcement += !string.IsNullOrWhiteSpace(MediaDescription) ? "i=" + MediaDescription + m_CRLF : null;

            announcement += (Connection == null) ? null : Connection.ToString();

            if(TIASBandwidth > 0)
            {
                announcement += TIAS_BANDWIDTH_ATTRIBUE_PREFIX + TIASBandwidth + m_CRLF;
            }

            foreach (string bandwidthAttribute in BandwidthAttributes)
            {
                announcement += "b=" + bandwidthAttribute + m_CRLF;
            }

            announcement += !string.IsNullOrWhiteSpace(IceUfrag) ? "a=" + SDP.ICE_UFRAG_ATTRIBUTE_PREFIX + ":" + IceUfrag + m_CRLF : null;
            announcement += !string.IsNullOrWhiteSpace(IcePwd) ? "a=" + SDP.ICE_PWD_ATTRIBUTE_PREFIX + ":" + IcePwd + m_CRLF : null;
            announcement += !string.IsNullOrWhiteSpace(DtlsFingerprint) ? "a=" + SDP.DTLS_FINGERPRINT_ATTRIBUTE_PREFIX + ":" + DtlsFingerprint + m_CRLF : null;

            if (IceCandidates?.Count() > 0)
            {
                foreach (var candidate in IceCandidates)
                {
                    announcement += $"a={SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX}:{candidate}{m_CRLF}";
                }
            }

            if (IceOptions != null)
            {
                announcement += $"a={SDP.ICE_OPTIONS}:" + IceOptions + m_CRLF;
            }

            if (IceEndOfCandidates)
            {
                announcement += $"a={SDP.END_ICE_CANDIDATES_ATTRIBUTE}" + m_CRLF;
            }

            announcement += !string.IsNullOrWhiteSpace(MediaID) ? "a=" + SDP.MEDIA_ID_ATTRIBUTE_PREFIX + ":" + MediaID + m_CRLF : null;

            announcement += GetFormatListAttributesToString();

            foreach (string extra in ExtraMediaAttributes)
            {
                announcement += string.IsNullOrWhiteSpace(extra) ? null : extra + m_CRLF;
            }

            foreach (SDPSecurityDescription desc in this.SecurityDescriptions)
            {
                announcement += desc.ToString() + m_CRLF;
            }

            if (MediaStreamStatus != null)
            {
                announcement += MediaStreamStatusType.GetAttributeForMediaStreamStatus(MediaStreamStatus.Value) + m_CRLF;
            }

            if (SsrcGroupID != null && SsrcAttributes.Count > 0)
            {
                announcement += MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX + SsrcGroupID;
                foreach (var ssrcAttr in SsrcAttributes)
                {
                    announcement += $" {ssrcAttr.SSRC}";
                }
                announcement += m_CRLF;
            }

            if (SsrcAttributes.Count > 0)
            {
                foreach (var ssrcAttr in SsrcAttributes)
                {
                    if (!string.IsNullOrWhiteSpace(ssrcAttr.Cname))
                    {
                        announcement += $"{MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX}{ssrcAttr.SSRC} {SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX}:{ssrcAttr.Cname}" + m_CRLF;
                    }
                    else
                    {
                        announcement += $"{MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX}{ssrcAttr.SSRC}" + m_CRLF;
                    }
                }
            }

            // If the "sctpmap" attribute is set, use it instead of the separate "sctpport" and "max-message-size"
            // attributes. They both contain the same information. The "sctpmap" is the legacy attribute and if
            // an application sets it then it's likely to be for a specific reason.
            if (SctpMap != null)
            {
                announcement += $"{MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX}{SctpMap}" + m_CRLF;
            }
            else
            {
                if (SctpPort != null)
                {
                    announcement += $"{MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX}{SctpPort}" + m_CRLF;
                }

                if (MaxMessageSize != 0)
                {
                    announcement += $"{MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX}{MaxMessageSize}" + m_CRLF;
                }
            }

            return announcement;
        }

        public string GetFormatListToString()
        {
            if (Media == SDPMediaTypesEnum.application)
            {
                StringBuilder sb = new StringBuilder();
                foreach (var appFormat in ApplicationMediaFormats)
                {
                    sb.Append(appFormat.Key);
                    sb.Append(" ");
                }

                return sb.ToString().Trim();
            }
            else
            {
                string mediaFormatList = null;
                foreach (var mediaFormat in MediaFormats)
                {
                    mediaFormatList += mediaFormat.Key + " ";
                }

                return (mediaFormatList != null) ? mediaFormatList.Trim() : null;
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
                            sb.Append($"{MEDIA_FORMAT_ATTRIBUE_PREFIX}{appFormat.Key} {appFormat.Value.Rtpmap}{m_CRLF}");
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
                            formatAttributes += MEDIA_FORMAT_ATTRIBUE_PREFIX + mediaFormat.ID + " " + mediaFormat.Name() + "/" + mediaFormat.ClockRate() + m_CRLF;
                        }
                        else
                        {
                            formatAttributes += MEDIA_FORMAT_ATTRIBUE_PREFIX + mediaFormat.ID + " " + mediaFormat.Rtpmap + m_CRLF;
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
