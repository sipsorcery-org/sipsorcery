//-----------------------------------------------------------------------------
// Filename: SDP.cs
//
// Description: Session Description Protocol implementation as defined in RFC 2327.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Oct 2005	Aaron Clauson	Created.
// rj2: save raw string of SDP, in case there is something in it, that can't be parsed
//
// Notes:
//
// Relevant Bits from the RFC:
// "SDP is intended for describing mulitmedia sessions for the purposes of session
// announcement, session invitation, and other forms of multimedia session
// initiation." 
//
// SDP Includes:
// - Session name and Purpose,
// - Time(s) the session is active,
// - The media comprising the session,
// - Information to receive those media (addresses, ports, formats etc.)
// As resources to participate in the session may be limited, some additional information
// may also be desirable:
// - Information about the bandwidth to be used,
// - Contact information for the person responsible for the conference.
//
// Media Information, SDP Includes:
// - The type of media (video, audio, etc),
// - The transport protocol (RTP/UDP/IP, H.320, ext),
// - The format of the media (H.261 video, MPEG video, etc).
//
// An SDP session description consists of a number of lines of text of the form
// <type>=<value> where <type> is always exactly one character and is case-significant.
// <value> is a structured test string whose format depends on <type> and is also
// case-significant unless the <type> permits otherwise. Whitespace is not permitted
// either side of the = sign.
//
// An announcement consists of a session-level section followed by zero
// or more media-level sections.  The session-level part starts with a
// 'v=' line and continues to the first media-level section.  The media
// description starts with an `m=' line and continues to the next media
// description or end of the whole session description.
//
// The sequence CRLF (0x0d0a) is used to end a record, although parsers should be
// tolerant and also accept records terminated with a single newline character. 
//
// Session description
// v=  (protocol version)
// o=  (owner/creator and session identifier).
//     <username> <session id> <version> <network type> <address type> <address>
// s=  (session name)
// i=* (session information)
//
// u=* (URI of description)
// e=* (email address)
// p=* (phone number)
// c=* (connection information - not required if included in all media)
// b=* (bandwidth information)
// One or more time descriptions (see below)
// z=* (time zone adjustments)
// k=* (encryption key)
// a=* (zero or more session attribute lines)
// Zero or more media descriptions (see below)
//
// Time description
// t=  (time the session is active)
// r=* (zero or more repeat times)
//
// Media description
// m=  (media name and transport address)
//     <media> <port> <transport> [<fmt list>]
// i=* (media title)
// c=* (connection information - optional if included at session-level)
// b=* (bandwidth information)
// k=* (encryption key)
// a=* (zero or more media attribute lines)
//
// Example SDP Description:
// 
// v=0
// o=mhandley 2890844526 2890842807 IN IP4 126.16.64.4
// s=SDP Seminar
// i=A Seminar on the session description protocol
// u=http://www.cs.ucl.ac.uk/staff/M.Handley/sdp.03.ps
// e=mjh@isi.edu (Mark Handley)
// c=IN IP4 224.2.17.12/127
// t=2873397496 2873404696
// a=recvonly
// m=audio 49170 RTP/AVP 0
// m=video 51372 RTP/AVP 31
// m=application 32416 udp wb
// a=orient:portrait
// 
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SDP
    {
        public const string CRLF = "\r\n";
        public const string SDP_MIME_CONTENTTYPE = "application/sdp";
        public const decimal SDP_PROTOCOL_VERSION = 0M;
        public const string GROUP_ATRIBUTE_PREFIX = "group";
        public const string DTLS_FINGERPRINT_ATTRIBUTE_PREFIX = "fingerprint";
        public const string ICE_CANDIDATE_ATTRIBUTE_PREFIX = "candidate";
        public const string ADDRESS_TYPE_IPV4 = "IP4";
        public const string ADDRESS_TYPE_IPV6 = "IP6";
        public const string DEFAULT_TIMING = "0 0";
        public const string MEDIA_ID_ATTRIBUTE_PREFIX = "mid";
        public const int IGNORE_RTP_PORT_NUMBER = 9;
        public const string TELEPHONE_EVENT_ATTRIBUTE = "telephone-event";
        public const int MEDIA_INDEX_NOT_PRESENT = -1;
        public const MediaStreamStatusEnum DEFAULT_STREAM_STATUS = MediaStreamStatusEnum.SendRecv;

        // ICE attributes.
        public const string ICE_UFRAG_ATTRIBUTE_PREFIX = "ice-ufrag";
        public const string ICE_PWD_ATTRIBUTE_PREFIX = "ice-pwd";
        public const string END_ICE_CANDIDATES_ATTRIBUTE = "end-of-candidates";
        public const string ICE_OPTIONS = "ice-options";

        private static ILogger logger = Log.Logger;

        public decimal Version = SDP_PROTOCOL_VERSION;

        private string m_rawSdp = null;

        // Owner fields.
        public string Username = "-";       // Username of the session originator.
        public string SessionId = "-";      // Unique Id for the session.
        public int AnnouncementVersion = 0; // Version number for each announcement, number must be increased for each subsequent SDP modification.
        public string NetworkType = "IN";   // Type of network, IN = Internet.
        public string AddressType = ADDRESS_TYPE_IPV4;  // Address type, typically IP4 or IP6.
        public string AddressOrHost;         // IP Address or Host of the machine that created the session, either FQDN or dotted quad or textual for IPv6.
        public string Owner
        {
            get { return Username + " " + SessionId + " " + AnnouncementVersion + " " + NetworkType + " " + AddressType + " " + AddressOrHost; }
        }

        public string SessionName = "sipsorcery";            // Common name of the session.
        public string Timing = DEFAULT_TIMING;
        public List<string> BandwidthAttributes = new List<string>();

        // Optional fields.
        public string SessionDescription;
        public string URI;                          // URI for additional information about the session.
        public string[] OriginatorEmailAddresses;   // Email addresses for the person responsible for the session.
        public string[] OriginatorPhoneNumbers;     // Phone numbers for the person responsible for the session.
        public string IceUfrag;                     // If ICE is being used the username for the STUN requests.
        public string IcePwd;                       // If ICE is being used the password for the STUN requests.
        public string DtlsFingerprint;              // If DTLS handshake is being used this is the fingerprint or our DTLS certificate.
        public List<string> IceCandidates;

        /// <summary>
        /// Indicates multiple media offers will be bundled on a single RTP connection.
        /// Example: a=group:BUNDLE audio video
        /// </summary>
        public string Group;

        public SDPConnectionInformation Connection;

        // Media.
        public List<SDPMediaAnnouncement> Media = new List<SDPMediaAnnouncement>();

        /// <summary>
        /// The stream status of this session. The default is sendrecv.
        /// If child media announcements have an explicit status set then 
        /// they take precedence.
        /// </summary>
        public MediaStreamStatusEnum? SessionMediaStreamStatus { get; set; } = null;

        public List<string> ExtraSessionAttributes = new List<string>();  // Attributes that were not recognised.

        public SDP()
        { }

        public SDP(IPAddress address)
        {
            AddressOrHost = address.ToString();
            AddressType = (address.AddressFamily == AddressFamily.InterNetworkV6) ? ADDRESS_TYPE_IPV6 : ADDRESS_TYPE_IPV4;
        }

        public static SDP ParseSDPDescription(string sdpDescription)
        {
            try
            {
                if (sdpDescription != null && sdpDescription.Trim().Length > 0)
                {
                    SDP sdp = new SDP();
                    sdp.m_rawSdp = sdpDescription;
                    int mLineIndex = 0;
                    SDPMediaAnnouncement activeAnnouncement = null;

                    // If a media announcement fmtp atribute is found before the rtpmap it will be stored
                    // in this dictionary. A dynamic media format type cannot be created without an rtpmap.
                    Dictionary<int, string> _pendingFmtp = new Dictionary<int, string>();

                    string[] sdpLines = Regex.Split(sdpDescription, CRLF);

                    foreach (string sdpLine in sdpLines)
                    {
                        string sdpLineTrimmed = sdpLine.Trim();

                        switch (sdpLineTrimmed)
                        {
                            case var l when l.StartsWith("v="):
                                if (!Decimal.TryParse(sdpLineTrimmed.Substring(2), out sdp.Version))
                                {
                                    logger.LogWarning("The Version value in an SDP description could not be parsed as a decimal: " + sdpLine + ".");
                                }
                                break;

                            case var l when l.StartsWith("o="):
                                string[] ownerFields = sdpLineTrimmed.Substring(2).Split(' ');
                                sdp.Username = ownerFields[0];
                                sdp.SessionId = ownerFields[1];
                                Int32.TryParse(ownerFields[2], out sdp.AnnouncementVersion);
                                sdp.NetworkType = ownerFields[3];
                                sdp.AddressType = ownerFields[4];
                                sdp.AddressOrHost = ownerFields[5];
                                break;

                            case var l when l.StartsWith("s="):
                                sdp.SessionName = sdpLineTrimmed.Substring(2);
                                break;

                            case var l when l.StartsWith("i="):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.MediaDescription = sdpLineTrimmed.Substring(2);
                                }
                                else
                                {
                                    sdp.SessionDescription = sdpLineTrimmed.Substring(2);
                                }

                                break;

                            case var l when l.StartsWith("c="):

                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.Connection = SDPConnectionInformation.ParseConnectionInformation(sdpLineTrimmed);
                                }
                                else if (sdp.Connection == null)
                                {
                                    sdp.Connection = SDPConnectionInformation.ParseConnectionInformation(sdpLineTrimmed);
                                }
                                else
                                {
                                    logger.LogWarning("The SDP message had a duplicate connection attribute which was ignored.");
                                }

                                break;

                            case var l when l.StartsWith("b="):
                                if (activeAnnouncement != null)
                                {
                                    if (l.StartsWith(SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_PREFIX))
                                    {
                                        if (uint.TryParse(l.Substring(l.IndexOf(':') + 1), out uint tias))
                                        {
                                            activeAnnouncement.TIASBandwidth = tias;
                                        }
                                    }
                                    else
                                    {
                                        activeAnnouncement.BandwidthAttributes.Add(sdpLineTrimmed.Substring(2));
                                    }
                                }
                                else
                                {
                                    sdp.BandwidthAttributes.Add(sdpLineTrimmed.Substring(2));
                                }
                                break;

                            case var l when l.StartsWith("t="):
                                sdp.Timing = sdpLineTrimmed.Substring(2);
                                break;

                            case var l when l.StartsWith("m="):
                                Match mediaMatch = Regex.Match(sdpLineTrimmed.Substring(2), @"(?<type>\w+)\s+(?<port>\d+)\s+(?<transport>\S+)(\s*)(?<formats>.*)$");
                                if (mediaMatch.Success)
                                {
                                    SDPMediaAnnouncement announcement = new SDPMediaAnnouncement();
                                    announcement.MLineIndex = mLineIndex;
                                    announcement.Media = SDPMediaTypes.GetSDPMediaType(mediaMatch.Result("${type}"));
                                    Int32.TryParse(mediaMatch.Result("${port}"), out announcement.Port);
                                    announcement.Transport = mediaMatch.Result("${transport}");
                                    announcement.ParseMediaFormats(mediaMatch.Result("${formats}"));
                                    if (announcement.Media == SDPMediaTypesEnum.audio || announcement.Media == SDPMediaTypesEnum.video)
                                    {
                                        announcement.MediaStreamStatus = sdp.SessionMediaStreamStatus != null ? sdp.SessionMediaStreamStatus.Value :
                                            MediaStreamStatusEnum.SendRecv;
                                    }
                                    sdp.Media.Add(announcement);

                                    activeAnnouncement = announcement;
                                }
                                else
                                {
                                    logger.LogWarning("A media line in SDP was invalid: " + sdpLineTrimmed.Substring(2) + ".");
                                }

                                mLineIndex++;
                                break;

                            case var x when x.StartsWith($"a={GROUP_ATRIBUTE_PREFIX}"):
                                sdp.Group = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                break;

                            case var x when x.StartsWith($"a={ICE_UFRAG_ATTRIBUTE_PREFIX}"):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.IceUfrag = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                else
                                {
                                    sdp.IceUfrag = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                break;

                            case var x when x.StartsWith($"a={ICE_PWD_ATTRIBUTE_PREFIX}"):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.IcePwd = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                else
                                {
                                    sdp.IcePwd = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                break;

                            case var x when x.StartsWith($"a={DTLS_FINGERPRINT_ATTRIBUTE_PREFIX}"):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.DtlsFingerprint = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                else
                                {
                                    sdp.DtlsFingerprint = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                break;

                            case var x when x.StartsWith($"a={ICE_CANDIDATE_ATTRIBUTE_PREFIX}"):
                                if (activeAnnouncement != null)
                                {
                                    if (activeAnnouncement.IceCandidates == null)
                                    {
                                        activeAnnouncement.IceCandidates = new List<string>();
                                    }
                                    activeAnnouncement.IceCandidates.Add(sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1));
                                }
                                else
                                {
                                    if (sdp.IceCandidates == null)
                                    {
                                        sdp.IceCandidates = new List<string>();
                                    }
                                    sdp.IceCandidates.Add(sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1));
                                }
                                break;

                            case var x when x == $"a={END_ICE_CANDIDATES_ATTRIBUTE}":
                                // TODO: Set a flag.
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    if (activeAnnouncement.Media == SDPMediaTypesEnum.audio || activeAnnouncement.Media == SDPMediaTypesEnum.video)
                                    {
                                        // Parse the rtpmap attribute for audio/video announcements.
                                        Match formatAttributeMatch = Regex.Match(sdpLineTrimmed, SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX + @"(?<id>\d+)\s+(?<attribute>.*)$");
                                        if (formatAttributeMatch.Success)
                                        {
                                            string formatID = formatAttributeMatch.Result("${id}");
                                            string rtpmap = formatAttributeMatch.Result("${attribute}");

                                            if (Int32.TryParse(formatID, out int id))
                                            {
                                                if (activeAnnouncement.MediaFormats.ContainsKey(id))
                                                {
                                                    activeAnnouncement.MediaFormats[id] = activeAnnouncement.MediaFormats[id].WithUpdatedRtpmap(rtpmap, activeAnnouncement.MediaFormats[id]);
                                                }
                                                else
                                                {
                                                    string fmtp = _pendingFmtp.ContainsKey(id) ? _pendingFmtp[id] : null;
                                                    activeAnnouncement.MediaFormats.Add(id, new SDPAudioVideoMediaFormat(activeAnnouncement.Media, id, rtpmap, fmtp));
                                                }
                                            }
                                            else
                                            {
                                                logger.LogWarning("Non-numeric audio/video media format attribute in SDP: " + sdpLine);
                                            }
                                        }
                                        else
                                        {
                                            activeAnnouncement.AddExtra(sdpLineTrimmed);
                                        }
                                    }
                                    else
                                    {
                                        // Parse the rtpmap attribute for NON audio/video announcements.
                                        Match formatAttributeMatch = Regex.Match(sdpLineTrimmed, SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX + @"(?<id>\S+)\s+(?<attribute>.*)$");
                                        if (formatAttributeMatch.Success)
                                        {
                                            string formatID = formatAttributeMatch.Result("${id}");
                                            string rtpmap = formatAttributeMatch.Result("${attribute}");

                                            if (activeAnnouncement.ApplicationMediaFormats.ContainsKey(formatID))
                                            {
                                                activeAnnouncement.ApplicationMediaFormats[formatID] = activeAnnouncement.ApplicationMediaFormats[formatID].WithUpdatedRtpmap(rtpmap);
                                            }
                                            else
                                            {
                                                activeAnnouncement.ApplicationMediaFormats.Add(formatID, new SDPApplicationMediaFormat(formatID, rtpmap, null));
                                            }
                                        }
                                        else
                                        {
                                            activeAnnouncement.AddExtra(sdpLineTrimmed);
                                        }
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("There was no active media announcement for a media format attribute, ignoring.");
                                }
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    if (activeAnnouncement.Media == SDPMediaTypesEnum.audio || activeAnnouncement.Media == SDPMediaTypesEnum.video)
                                    {
                                        // Parse the fmtp attribute for audio/video announcements.
                                        Match formatAttributeMatch = Regex.Match(sdpLineTrimmed, SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX + @"(?<id>\d+)\s+(?<attribute>.*)$");
                                        if (formatAttributeMatch.Success)
                                        {
                                            string avFormatID = formatAttributeMatch.Result("${id}");
                                            string fmtp = formatAttributeMatch.Result("${attribute}");

                                            if (Int32.TryParse(avFormatID, out int id))
                                            {
                                                if (activeAnnouncement.MediaFormats.ContainsKey(id))
                                                {
                                                    activeAnnouncement.MediaFormats[id] = activeAnnouncement.MediaFormats[id].WithUpdatedFmtp(fmtp, activeAnnouncement.MediaFormats[id]);
                                                }
                                                else
                                                {
                                                    // Store the fmtp attribute for use when the rtpmap attribute turns up.
                                                    if (_pendingFmtp.ContainsKey(id))
                                                    {
                                                        _pendingFmtp.Remove(id);
                                                    }
                                                    _pendingFmtp.Add(id, fmtp);
                                                }
                                            }
                                            else
                                            {
                                                logger.LogWarning("Invalid media format parameter attribute in SDP: " + sdpLine);
                                            }
                                        }
                                        else
                                        {
                                            activeAnnouncement.AddExtra(sdpLineTrimmed);
                                        }
                                    }
                                    else
                                    {
                                        // Parse the fmtp attribute for NON audio/video announcements.
                                        Match formatAttributeMatch = Regex.Match(sdpLineTrimmed, SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX + @"(?<id>\S+)\s+(?<attribute>.*)$");
                                        if (formatAttributeMatch.Success)
                                        {
                                            string formatID = formatAttributeMatch.Result("${id}");
                                            string fmtp = formatAttributeMatch.Result("${attribute}");

                                            if (activeAnnouncement.ApplicationMediaFormats.ContainsKey(formatID))
                                            {
                                                activeAnnouncement.ApplicationMediaFormats[formatID] = activeAnnouncement.ApplicationMediaFormats[formatID].WithUpdatedFmtp(fmtp);
                                            }
                                            else
                                            {
                                                activeAnnouncement.ApplicationMediaFormats.Add(formatID, new SDPApplicationMediaFormat(formatID, null, fmtp));
                                            }
                                        }
                                        else
                                        {
                                            activeAnnouncement.AddExtra(sdpLineTrimmed);
                                        }
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("There was no active media announcement for a media format parameter attribute, ignoring.");
                                }
                                break;

                            case var l when l.StartsWith(SDPSecurityDescription.CRYPTO_ATTRIBUE_PREFIX):
                                //2018-12-21 rj2: add a=crypto
                                if (activeAnnouncement != null)
                                {
                                    try
                                    {
                                        activeAnnouncement.AddCryptoLine(sdpLineTrimmed);
                                    }
                                    catch (FormatException fex)
                                    {
                                        logger.LogWarning("Error Parsing SDP-Line(a=crypto) " + fex);
                                    }
                                }
                                break;

                            case var x when x.StartsWith($"a={MEDIA_ID_ATTRIBUTE_PREFIX}"):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.MediaID = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                }
                                else
                                {
                                    logger.LogWarning("A media ID can only be set on a media announcement.");
                                }
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    string[] fields = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1).Split(' ');

                                    // Set the ID.
                                    if (fields.Length > 0)
                                    {
                                        activeAnnouncement.SsrcGroupID = fields[0];
                                    }

                                    // Add attributes for each of the SSRC values.
                                    for (int i = 1; i < fields.Length; i++)
                                    {
                                        if (uint.TryParse(fields[i], out var ssrc))
                                        {
                                            activeAnnouncement.SsrcAttributes.Add(new SDPSsrcAttribute(ssrc, null, activeAnnouncement.SsrcGroupID));
                                        }
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("A ssrc-group ID can only be set on a media announcement.");
                                }
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    string[] ssrcFields = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1).Split(' ');

                                    if (ssrcFields.Length > 0 && uint.TryParse(ssrcFields[0], out var ssrc))
                                    {
                                        var ssrcAttribute = activeAnnouncement.SsrcAttributes.FirstOrDefault(x => x.SSRC == ssrc);
                                        if (ssrcAttribute == null)
                                        {
                                            ssrcAttribute = new SDPSsrcAttribute(ssrc, null, null);
                                            activeAnnouncement.SsrcAttributes.Add(ssrcAttribute);
                                        }

                                        if (ssrcFields.Length > 1)
                                        {
                                            if (ssrcFields[1].StartsWith(SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX))
                                            {
                                                ssrcAttribute.Cname = ssrcFields[1].Substring(ssrcFields[1].IndexOf(':') + 1);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("An ssrc attribute can only be set on a media announcement.");
                                }
                                break;

                            case var x when MediaStreamStatusType.IsMediaStreamStatusAttribute(x, out var mediaStreamStatus):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.MediaStreamStatus = mediaStreamStatus;
                                }
                                else
                                {
                                    sdp.SessionMediaStreamStatus = mediaStreamStatus;
                                }
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.SctpMap = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);

                                    (var sctpPortStr, _, var maxMessageSizeStr) = activeAnnouncement.SctpMap.Split(' ');

                                    if (ushort.TryParse(sctpPortStr, out var sctpPort))
                                    {
                                        activeAnnouncement.SctpPort = sctpPort;
                                    }
                                    else
                                    {
                                        logger.LogWarning($"An sctp-port value of {sctpPortStr} was not recognised as a valid port.");
                                    }

                                    if (!long.TryParse(maxMessageSizeStr, out activeAnnouncement.MaxMessageSize))
                                    {
                                        logger.LogWarning($"A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("An sctpmap attribute can only be set on a media announcement.");
                                }
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    string sctpPortStr = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);

                                    if (ushort.TryParse(sctpPortStr, out var sctpPort))
                                    {
                                        activeAnnouncement.SctpPort = sctpPort;
                                    }
                                    else
                                    {
                                        logger.LogWarning($"An sctp-port value of {sctpPortStr} was not recognised as a valid port.");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("An sctp-port attribute can only be set on a media announcement.");
                                }
                                break;

                            case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX):
                                if (activeAnnouncement != null)
                                {
                                    string maxMessageSizeStr = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                                    if (!long.TryParse(maxMessageSizeStr, out activeAnnouncement.MaxMessageSize))
                                    {
                                        logger.LogWarning($"A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.");
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("A max-message-size attribute can only be set on a media announcement.");
                                }
                                break;

                            default:
                                if (activeAnnouncement != null)
                                {
                                    activeAnnouncement.AddExtra(sdpLineTrimmed);
                                }
                                else
                                {
                                    sdp.AddExtra(sdpLineTrimmed);
                                }
                                break;
                        }
                    }

                    return sdp;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception ParseSDPDescription. " + excp.Message);
                throw;
            }
        }

        public void AddExtra(string attribute)
        {
            if (!string.IsNullOrWhiteSpace(attribute))
            {
                ExtraSessionAttributes.Add(attribute);
            }
        }

        public string RawString()
        {
            if (string.IsNullOrWhiteSpace(this.m_rawSdp))
            {
                return this.ToString();
            }
            return this.m_rawSdp;
        }

        public override string ToString()
        {
            string sdp =
                "v=" + SDP_PROTOCOL_VERSION + CRLF +
                "o=" + Owner + CRLF +
                "s=" + SessionName + CRLF +
                ((Connection != null) ? Connection.ToString() : null);
            foreach (string bandwidth in BandwidthAttributes)
            {
                sdp += "b=" + bandwidth + CRLF;
            }

            sdp += "t=" + Timing + CRLF;

            sdp += !string.IsNullOrWhiteSpace(IceUfrag) ? "a=" + ICE_UFRAG_ATTRIBUTE_PREFIX + ":" + IceUfrag + CRLF : null;
            sdp += !string.IsNullOrWhiteSpace(IcePwd) ? "a=" + ICE_PWD_ATTRIBUTE_PREFIX + ":" + IcePwd + CRLF : null;
            sdp += !string.IsNullOrWhiteSpace(DtlsFingerprint) ? "a=" + DTLS_FINGERPRINT_ATTRIBUTE_PREFIX + ":" + DtlsFingerprint + CRLF : null;
            if (IceCandidates?.Count > 0)
            {
                foreach (var candidate in IceCandidates)
                {
                    sdp += $"a={SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX}:{candidate}{CRLF}";
                }
            }
            sdp += string.IsNullOrWhiteSpace(SessionDescription) ? null : "i=" + SessionDescription + CRLF;
            sdp += string.IsNullOrWhiteSpace(URI) ? null : "u=" + URI + CRLF;

            if (OriginatorEmailAddresses != null && OriginatorEmailAddresses.Length > 0)
            {
                foreach (string originatorAddress in OriginatorEmailAddresses)
                {
                    sdp += string.IsNullOrWhiteSpace(originatorAddress) ? null : "e=" + originatorAddress + CRLF;
                }
            }

            if (OriginatorPhoneNumbers != null && OriginatorPhoneNumbers.Length > 0)
            {
                foreach (string originatorNumber in OriginatorPhoneNumbers)
                {
                    sdp += string.IsNullOrWhiteSpace(originatorNumber) ? null : "p=" + originatorNumber + CRLF;
                }
            }

            sdp += (Group == null) ? null : $"a={GROUP_ATRIBUTE_PREFIX}:{Group}" + CRLF;

            foreach (string extra in ExtraSessionAttributes)
            {
                sdp += string.IsNullOrWhiteSpace(extra) ? null : extra + CRLF;
            }

            if (SessionMediaStreamStatus != null)
            {
                sdp += MediaStreamStatusType.GetAttributeForMediaStreamStatus(SessionMediaStreamStatus.Value) + CRLF;
            }

            foreach (SDPMediaAnnouncement media in Media.OrderBy(x => x.MLineIndex).ThenBy(x => x.MediaID))
            {
                sdp += (media == null) ? null : media.ToString();
            }

            return sdp;
        }

        /// <summary>
        /// A convenience method to get the RTP end point for single audio offer SDP payloads.
        /// </summary>
        /// <returns>The RTP end point for the first media end point.</returns>
        public IPEndPoint GetSDPRTPEndPoint()
        {
            // Find first media offer.
            var sessionConnection = Connection;
            var firstMediaOffer = Media.FirstOrDefault();

            if (sessionConnection != null && firstMediaOffer != null)
            {
                return new IPEndPoint(IPAddress.Parse(sessionConnection.ConnectionAddress), firstMediaOffer.Port);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// A convenience method to get the RTP end point for single audio offer SDP payloads.
        /// </summary>
        /// <param name="sdpMessage">A string representing the SDP payload.</param>
        /// <returns>The RTP end point for the first media end point.</returns>
        public static IPEndPoint GetSDPRTPEndPoint(string sdpMessage)
        {
            return ParseSDPDescription(sdpMessage)
                .GetSDPRTPEndPoint();
        }

        /// <summary>
        /// Gets the media stream status for the specified media announcement.
        /// </summary>
        /// <param name="mediaType">The type of media (audio, video etc) to get the status for.</param>
        /// <param name="announcementIndex">THe index of the announcement to get the status for.</param>
        /// <returns>The media stream status set on the announcement or if there is none the session. If
        /// there is also no status set on the session then the default value of sendrecv is returned.</returns>
        public MediaStreamStatusEnum GetMediaStreamStatus(SDPMediaTypesEnum mediaType, int announcementIndex)
        {
            var announcements = Media.Where(x => x.Media == mediaType).ToList();

            if (announcements == null || announcements.Count() < announcementIndex + 1)
            {
                return DEFAULT_STREAM_STATUS;
            }
            else
            {
                var announcement = announcements[announcementIndex];
                return announcement.MediaStreamStatus.HasValue ? announcement.MediaStreamStatus.Value : DEFAULT_STREAM_STATUS;
            }
        }

        /// <summary>
        /// Media announcements can be placed in SDP in any order BUT the orders must match
        /// up in offer/answer pairs. This method can be used to get the index for a specific
        /// media type. It is useful for obtaining the index of a particular media type when
        /// constructing an SDP answer.
        /// </summary>
        /// <param name="mediaType">The media type to get the index for.</param>
        /// <returns></returns>
        public int GetIndexForMediaType(SDPMediaTypesEnum mediaType)
        {
            int index = 0;
            foreach (var ann in Media)
            {
                if (ann.Media == mediaType)
                {
                    return index;
                }
                else
                {
                    index++;
                }
            }

            return MEDIA_INDEX_NOT_PRESENT;
        }
    }
}
