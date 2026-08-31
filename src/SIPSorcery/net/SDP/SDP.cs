//-----------------------------------------------------------------------------
// Filename: SDP.cs
//
// Description: Session Description Protocol implementation as defined in RFC 2327.
//
// Author(s):
// Aaron Clauson
// Jacek Dzija
// Mateusz Greczek
//
// History:
// 20 Oct 2005	Aaron Clauson	Created.
// rj2: save raw string of SDP, in case there is something in it, that can't be parsed
// 30 Mar 2021 Jacek Dzija,Mateusz Greczek Added MSRP
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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

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
        public const string ICE_SETUP_ATTRIBUTE_PREFIX = "setup";
        public const string ADDRESS_TYPE_IPV4 = "IP4";
        public const string ADDRESS_TYPE_IPV6 = "IP6";
        public const string DEFAULT_TIMING = "0 0";
        public const string MEDIA_ID_ATTRIBUTE_PREFIX = "mid";
        public const int IGNORE_RTP_PORT_NUMBER = 9;
        public const string TELEPHONE_EVENT_ATTRIBUTE = "telephone-event";
        public const int MEDIA_INDEX_NOT_PRESENT = -1;
        public const string MEDIA_INDEX_TAG_NOT_PRESENT = "";
        public const MediaStreamStatusEnum DEFAULT_STREAM_STATUS = MediaStreamStatusEnum.SendRecv;

        // ICE attributes.
        public const string ICE_LITE_IMPLEMENTATION_ATTRIBUTE_PREFIX = "ice-lite";
        public const string ICE_UFRAG_ATTRIBUTE_PREFIX = "ice-ufrag";
        public const string ICE_PWD_ATTRIBUTE_PREFIX = "ice-pwd";
        public const string END_ICE_CANDIDATES_ATTRIBUTE = "end-of-candidates";
        public const string ICE_OPTIONS = "ice-options";

        private static readonly ILogger logger = LogFactory.CreateLogger<SDP>();

        public decimal Version = SDP_PROTOCOL_VERSION;

        private string m_rawSdp = null;

        // Owner fields.
        public string Username = "-";       // Username of the session originator.
        public string SessionId = "-";      // Unique Id for the session.
        public ulong AnnouncementVersion = 0; // Version number for each announcement, number must be increased for each subsequent SDP modification.
        public string NetworkType = "IN";   // Type of network, IN = Internet.
        public string AddressType = ADDRESS_TYPE_IPV4;  // Address type, typically IP4 or IP6.
        public string AddressOrHost;         // IP Address or Host of the machine that created the session, either FQDN or dotted quad or textual for IPv6.
        public string Owner
        {
            get { return $"{Username} {SessionId} {AnnouncementVersion} {NetworkType} {AddressType} {AddressOrHost}"; }
        }

        public string SessionName = "sipsorcery";            // Common name of the session.
        public string Timing = DEFAULT_TIMING;
        public List<string> BandwidthAttributes = new List<string>();

        // Optional fields.
        public string SessionDescription;
        public string URI;                          // URI for additional information about the session.
        public string[] OriginatorEmailAddresses;   // Email addresses for the person responsible for the session.
        public string[] OriginatorPhoneNumbers;     // Phone numbers for the person responsible for the session.
        public IceImplementationEnum IceImplementation = IceImplementationEnum.full;
        public string IceUfrag;                     // If ICE is being used the username for the STUN requests.
        public string IcePwd;                       // If ICE is being used the password for the STUN requests.
        public IceRolesEnum? IceRole = null;
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
            => ParseSDPDescription(sdpDescription.AsSpan());

#nullable enable
        public static SDP? ParseSDPDescription(ReadOnlySpan<char> sdpDescription)
        {
            if (sdpDescription.IsEmptyOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var sdp = new SDP();

                var mLineIndex = 0;
                SDPMediaAnnouncement? activeAnnouncement = null;

                // If a media announcement fmtp atribute is found before the rtpmap it will be stored
                // in this dictionary. A dynamic media format type cannot be created without an rtpmap.
                Dictionary<int, string>? pendingFmtp = null;

                var sdpDescriptionSpan = sdpDescription;
                foreach (var lineRange in sdpDescriptionSpan.SplitAny(SearchValueHelpers.NewLineChars))
                {
                    var line = sdpDescriptionSpan[lineRange].Trim();

                    if (line.Length < 2 || line[1] != '=')
                    {
                        continue;
                    }

                    var type = line[0];
                    var value = line.Slice(2);

                    switch (type)
                    {
                        case 'v':
                            if (!decimal.TryParse(value, out sdp.Version))
                            {
                                logger.LogSdpInvalidVersion(value);
                            }
                            break;

                        case 'o':
                            ParseOrigin(value, sdp);
                            break;

                        case 's':
                            sdp.SessionName = value.ToString();
                            break;

                        case 'i':
                            if (activeAnnouncement is { })
                            {
                                activeAnnouncement.MediaDescription = value.ToString();
                            }
                            else
                            {
                                sdp.SessionDescription = value.ToString();
                            }

                            break;

                        case 'c':
                            if (activeAnnouncement is { })
                            {
                                activeAnnouncement.Connection = SDPConnectionInformation.ParseConnectionInformation(line);
                            }
                            else if (sdp.Connection is null)
                            {
                                sdp.Connection = SDPConnectionInformation.ParseConnectionInformation(line);
                            }
                            else
                            {
                                logger.LogSdpDuplicateConnectionAttribute();
                            }

                            break;

                        case 'b':
                            ParseBandwidth(value, sdp, activeAnnouncement);
                            break;

                        case 't':
                            sdp.Timing = value.ToString();
                            break;

                        case 'm':
                            pendingFmtp?.Clear();
                            ParseMedia(line, sdp, ref activeAnnouncement, ref mLineIndex);
                            break;

                        case 'a':
                            ParseAttribute(line, sdp, activeAnnouncement, ref pendingFmtp);
                            break;

                        default:
                            if (activeAnnouncement is { })
                            {
                                activeAnnouncement.AddExtra(line.ToString());
                            }
                            else
                            {
                                sdp.AddExtra(line.ToString());
                            }
                            break;
                    }

                    static void ParseOrigin(ReadOnlySpan<char> value, SDP sdp)
                    {
                        Span<Range> fields = stackalloc Range[6];
                        var count = value.Split(fields, ' ', StringSplitOptions.RemoveEmptyEntries);

                        if (count >= 5)
                        {
                            sdp.Username = value[fields[0]].ToString();
                            sdp.SessionId = value[fields[1]].ToString();
                            sdp.AnnouncementVersion = ulong.TryParse(value[fields[2]], out var version) ? version : 0;
                            sdp.NetworkType = value[fields[3]].ToString();
                            sdp.AddressType = value[fields[4]].ToString();
                            sdp.AddressOrHost = count > 5 ? value[fields[5]].ToString() : null;
                        }
                        else
                        {
                            logger.LogSdpInvalidSdpLineFormat(value);
                        }
                    }

                    static void ParseBandwidth(ReadOnlySpan<char> value, SDP sdp, SDPMediaAnnouncement? activeAnnouncement)
                    {
                        if (activeAnnouncement is { })
                        {
                            var colonIndex = value.IndexOf(':');
                            var key = colonIndex != -1 ? value.Slice(0, colonIndex) : value;
                            var attrValue = colonIndex != -1 && colonIndex + 1 < value.Length
                                ? value.Slice(colonIndex + 1)
                                : ReadOnlySpan<char>.Empty;
                            if (key.SequenceEqual(SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_NAME.AsSpan()))
                            {
                                if (uint.TryParse(attrValue, out var tias))
                                {
                                    activeAnnouncement.TIASBandwidth = tias;
                                }
                            }
                            else
                            {
                                activeAnnouncement.BandwidthAttributes.Add(value.ToString());
                            }
                        }
                        else
                        {
                            sdp.BandwidthAttributes.Add(value.ToString());
                        }
                    }

                    static void ParseMedia(ReadOnlySpan<char> line, SDP sdp, ref SDPMediaAnnouncement? activeAnnouncement, ref int mLineIndex)
                    {
                        if (TryParseMediaDescription(
                            line.Slice(2),
                            out var type,
                            out var port,
                            out var portCount,
                            out var transport,
                            out var formats))
                        {
                            var announcement = new SDPMediaAnnouncement();
                            announcement.MLineIndex = mLineIndex;
                            announcement.Media = SDPMediaTypes.GetSDPMediaType(type);
                            announcement.Port = port;

                            if (portCount is { } portCountValue)
                            {
                                announcement.PortCount = portCountValue;
                            }

                            announcement.Transport = transport;
                            announcement.ParseMediaFormats(formats);
                            if (announcement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                            {
                                announcement.MediaStreamStatus = sdp.SessionMediaStreamStatus is { } ? sdp.SessionMediaStreamStatus.Value :
                                    MediaStreamStatusEnum.SendRecv;
                            }
                            sdp.Media.Add(announcement);

                            activeAnnouncement = announcement;
                        }
                        else
                        {
                            logger.LogSdpInvalidMediaLine(line);
                        }

                        mLineIndex++;

                        /// <summary>
                        /// (?&lt;type&gt;\w+)\s+(?&lt;port&gt;\d+)(?:\/(?&lt;portCount&gt;\d+))?\s+(?&lt;transport&gt;\S+)\s*(?&lt;formats&gt;.*)
                        /// </summary>
                        static bool TryParseMediaDescription(
                            ReadOnlySpan<char> input,
                            out ReadOnlySpan<char> type,
                            out int port,
                            out int? portCount,
                            [NotNullWhen(true)] out string? transport,
                            out ReadOnlySpan<char> formats)
                        {
                            type = default;
                            port = default;
                            portCount = default;
                            transport = default;
                            formats = default;

                            // Parse type
                            var typeEnd = input.IndexOfAny(SearchValueHelpers.WhiteSpaceChars);
                            if (typeEnd <= 0)
                            {
                                return false;
                            }

                            type = input[..typeEnd];

                            // Skip whitespace after type
                            var i = typeEnd + input[typeEnd..].IndexOfAnyExcept(SearchValueHelpers.WhiteSpaceChars);
                            if (i >= input.Length)
                            {
                                return false;
                            }

                            // Parse port
                            var portStart = i;
                            var portEnd = input[portStart..].IndexOfAnyExcept(SearchValueHelpers.DigitChars);
                            if (portEnd <= 0)
                            {
                                return false;
                            }

                            portEnd += portStart;
                            if (!int.TryParse(input[portStart..portEnd], out port))
                            {
                                return false;
                            }

                            i = portEnd;

                            // Optional: /<portCount>
                            if (i < input.Length && input[i] == '/')
                            {
                                i++;
                                var portCountStart = i;
                                var portCountEnd = input[portCountStart..].IndexOfAnyExcept(SearchValueHelpers.DigitChars);
                                if (portCountEnd <= 0)
                                {
                                    return false;
                                }

                                portCountEnd += portCountStart;
                                if (!int.TryParse(input[portCountStart..portCountEnd], out var parsedPortCount))
                                {
                                    return false;
                                }

                                portCount = parsedPortCount;
                                i = portCountEnd;
                            }

                            // Skip whitespace before transport
                            var transportStartOffset = input[i..].IndexOfAnyExcept(SearchValueHelpers.WhiteSpaceChars);
                            if (transportStartOffset == -1)
                            {
                                return false;
                            }

                            i += transportStartOffset;

                            // Parse transport
                            var transportEndOffset = input[i..].IndexOfAny(SearchValueHelpers.WhiteSpaceChars);
                            var transportEnd = transportEndOffset == -1 ? input.Length : i + transportEndOffset;
                            transport = input[i..transportEnd].ToString();

                            i = transportEnd;

                            // Skip whitespace before formats
                            var formatsStartOffset = input[i..].IndexOfAnyExcept(SearchValueHelpers.WhiteSpaceChars);
                            i = formatsStartOffset == -1 ? input.Length : i + formatsStartOffset;

                            formats = input[i..];
                            return true;
                        }
                    }

                    static void ParseAttribute(
                        ReadOnlySpan<char> line,
                        SDP sdp,
                        SDPMediaAnnouncement? activeAnnouncement,
                        ref Dictionary<int, string>? pendingFmtp)
                    {
                        var value = line.Slice(2);
                        var colonIndex = value.IndexOf(':');
                        var key = colonIndex != -1 ? value.Slice(0, colonIndex) : value;
                        var attrValue = colonIndex != -1 && colonIndex + 1 < value.Length
                            ? value.Slice(colonIndex + 1)
                            : ReadOnlySpan<char>.Empty;

                        switch (key)
                        {
                            case GROUP_ATRIBUTE_PREFIX:
                                {
                                    sdp.Group = attrValue.ToString();
                                    break;
                                }
                            case ICE_LITE_IMPLEMENTATION_ATTRIBUTE_PREFIX:
                                {
                                    sdp.IceImplementation = IceImplementationEnum.lite;
                                    break;
                                }
                            case ICE_UFRAG_ATTRIBUTE_PREFIX:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.IceUfrag = attrValue.ToString();
                                    }
                                    else
                                    {
                                        sdp.IceUfrag = attrValue.ToString();
                                    }
                                    break;
                                }
                            case ICE_PWD_ATTRIBUTE_PREFIX:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.IcePwd = attrValue.ToString();
                                    }
                                    else
                                    {
                                        sdp.IcePwd = attrValue.ToString();
                                    }
                                    break;
                                }
                            case ICE_SETUP_ATTRIBUTE_PREFIX:
                                {
                                    if (!attrValue.IsEmpty)
                                    {
                                        if (Enum.TryParse<IceRolesEnum>(attrValue, true, out var iceRole))
                                        {
                                            if (activeAnnouncement is { })
                                            {
                                                activeAnnouncement.IceRole = iceRole;
                                            }
                                            else
                                            {
                                                sdp.IceRole = iceRole;
                                            }
                                        }
                                        else
                                        {
                                            logger.LogSdpInvalidIceRole(line);
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpMissingColon(line);
                                    }
                                    break;
                                }
                            case DTLS_FINGERPRINT_ATTRIBUTE_PREFIX:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.DtlsFingerprint = attrValue.ToString();
                                    }
                                    else
                                    {
                                        sdp.DtlsFingerprint = attrValue.ToString();
                                    }
                                    break;
                                }
                            case ICE_CANDIDATE_ATTRIBUTE_PREFIX:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.IceCandidates ??= new();
                                        activeAnnouncement.IceCandidates.Add(attrValue.ToString());
                                    }
                                    else
                                    {
                                        sdp.IceCandidates ??= new();
                                        sdp.IceCandidates.Add(attrValue.ToString());
                                    }
                                    break;
                                }
                            case END_ICE_CANDIDATES_ATTRIBUTE:
                                {
                                    // TODO: Set a flag.
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video)
                                        {
                                            if (TryParseNumericIdAndUrl(attrValue, out var extensionId, out var uri))
                                            {
                                                var rtpExtension = RTPHeaderExtension.GetRTPHeaderExtension(extensionId, uri, activeAnnouncement.Media);
                                                if (rtpExtension is { })
                                                {
                                                    activeAnnouncement.HeaderExtensions.TryAdd(extensionId, rtpExtension);
                                                }
                                            }
                                            else
                                            {
                                                logger.LogSdpInvalidHeaderExtension();
                                            }
                                        }
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUTE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                                        {
                                            // Parse the rtpmap attribute for audio/video announcements.
                                            if (TryParseNumericIdAndStringAttribute(attrValue, out var formatId, out var rtpmap))
                                            {
                                                if (activeAnnouncement.MediaFormats.TryGetValue(formatId, out var mediaFormat))
                                                {
                                                    activeAnnouncement.MediaFormats[formatId] = mediaFormat.WithUpdatedRtpmap(attrValue[rtpmap].ToString());
                                                }
                                                else
                                                {
                                                    string? fmtp = null;
                                                    if (pendingFmtp is not null && pendingFmtp.TryGetValue(formatId, out fmtp))
                                                    {
                                                        pendingFmtp.Remove(formatId);
                                                    }
                                                    activeAnnouncement.MediaFormats.Add(
                                                        formatId,
                                                        new SDPAudioVideoMediaFormat(
                                                            activeAnnouncement.Media,
                                                            formatId,
                                                            attrValue[rtpmap].ToString(),
                                                            fmtp));
                                                }
                                            }
                                            else
                                            {
                                                // This is a recognised rtpmap attribute with an invalid numeric payload ID.
                                                // Drop it instead of preserving it as an unknown extra attribute.
                                            }
                                        }
                                        else
                                        {
                                            // Parse the rtpmap attribute for NON audio/video announcements.
                                            if (TryParseStringIdAndStringAttribute(attrValue, out var formatID, out var rtpmap))
                                            {
                                                if (activeAnnouncement.ApplicationMediaFormats.TryGetValue(formatID, out var mediaFormat))
                                                {
                                                    activeAnnouncement.ApplicationMediaFormats[formatID] = mediaFormat.WithUpdatedRtpmap(rtpmap);
                                                }
                                                else
                                                {
                                                    activeAnnouncement.ApplicationMediaFormats.Add(formatID, new SDPApplicationMediaFormat(formatID, rtpmap, null));
                                                }
                                            }
                                            else
                                            {
                                                activeAnnouncement.AddExtra(line.ToString());
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpNoActiveMediaAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                                        {
                                            // Parse the fmtp attribute for audio/video announcements.
                                            if (TryParseNumericIdAndStringAttribute(attrValue, out var avFormatID, out var fmtp))
                                            {
                                                if (activeAnnouncement.MediaFormats.TryGetValue(avFormatID, out var mediaFormat))
                                                {
                                                    activeAnnouncement.MediaFormats[avFormatID] = mediaFormat.WithUpdatedFmtp(attrValue[fmtp].ToString());
                                                }
                                                else
                                                {
                                                    // Store the fmtp attribute for use when the rtpmap attribute turns up.
                                                    pendingFmtp ??= new Dictionary<int, string>();
                                                    pendingFmtp[avFormatID] = attrValue[fmtp].ToString();
                                                }
                                            }
                                            else
                                            {
                                                activeAnnouncement.AddExtra(line.ToString());
                                            }
                                        }
                                        else
                                        {
                                            // TODO: optimize this
                                            // Parse the fmtp attribute for NON audio/video announcements.
                                            if (TryParseStringIdAndStringAttribute(attrValue, out var formatID, out var fmtp))
                                            {
                                                if (activeAnnouncement.ApplicationMediaFormats.TryGetValue(formatID, out var mediaFormat))
                                                {
                                                    activeAnnouncement.ApplicationMediaFormats[formatID] = mediaFormat.WithUpdatedFmtp(fmtp);
                                                }
                                                else
                                                {
                                                    activeAnnouncement.ApplicationMediaFormats.Add(formatID, new SDPApplicationMediaFormat(formatID, null, fmtp));
                                                }
                                            }
                                            else
                                            {
                                                activeAnnouncement.AddExtra(line.ToString());
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpNoActiveMediaAnnouncementForParam();
                                    }
                                    break;
                                }
                            case SDPSecurityDescription.CRYPTO_ATTRIBUTE_NAME:
                                {
                                    //2018-12-21 rj2: add a=crypto
                                    if (activeAnnouncement is { })
                                    {
                                        try
                                        {
                                            activeAnnouncement.AddCryptoLine(line);
                                        }
                                        catch (FormatException fex)
                                        {
                                            logger.LogSdpCryptoParsingError(fex);
                                        }
                                    }
                                    break;
                                }
                            case MEDIA_ID_ATTRIBUTE_PREFIX:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.MediaID = attrValue.ToString();
                                    }
                                    else
                                    {
                                        logger.LogSdpMediaIdOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        var span = attrValue;
                                        var spaceIndex = span.IndexOf(' ');

                                        // Set the ID.
                                        if (spaceIndex != -1)
                                        {
                                            var idSpan = span.Slice(0, spaceIndex);
                                            activeAnnouncement.SsrcGroupID = idSpan.ToString();
                                            span = span.Slice(spaceIndex + 1);
                                        }
                                        else
                                        {
                                            activeAnnouncement.SsrcGroupID = attrValue.ToString();
                                            span = ReadOnlySpan<char>.Empty;
                                        }

                                        // Add attributes for each of the SSRC values.
                                        foreach (var token in span.Split(' '))
                                        {
                                            var ssrcSpan = span[token].Trim();
                                            if (uint.TryParse(ssrcSpan, out var ssrc))
                                            {
                                                activeAnnouncement.SsrcAttributes.Add(new SDPSsrcAttribute(ssrc, null, activeAnnouncement.SsrcGroupID));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSsrcGroupIdOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        var firstSpace = attrValue.IndexOf(' ');
                                        if (firstSpace == -1)
                                        {
                                            return;
                                        }

                                        var firstField = attrValue[..firstSpace];
                                        if (uint.TryParse(firstField, out var ssrc))
                                        {
                                            if (GetFirstMatchingAssrcAttribute(activeAnnouncement, ssrc) is not { } ssrcAttribute)
                                            {
                                                ssrcAttribute = new SDPSsrcAttribute(ssrc, null, null);
                                                activeAnnouncement.SsrcAttributes.Add(ssrcAttribute);
                                            }

                                            var remaining = attrValue[(firstSpace + 1)..];
                                            var secondSpace = remaining.IndexOf(' ');
                                            var secondField = secondSpace == -1
                                                ? remaining
                                                : remaining[..secondSpace];

                                            if (secondField.StartsWith("cname:".AsSpan()))
                                            {
                                                ssrcAttribute.Cname = secondField[6..].ToString();
                                            }

                                            static SDPSsrcAttribute? GetFirstMatchingAssrcAttribute(SDPMediaAnnouncement activeAnnouncement, uint ssrc)
                                            {
                                                SDPSsrcAttribute? ssrcAttribute = null;
                                                foreach (var attr in activeAnnouncement.SsrcAttributes)
                                                {
                                                    if (attr.SSRC == ssrc)
                                                    {
                                                        ssrcAttribute = attr;
                                                        break;
                                                    }
                                                }

                                                return ssrcAttribute;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSsrcAttributeOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.SctpMap = attrValue.ToString();

                                        // Parse sctp-port and max-message-size from space-separated values
                                        // Format: "sctpPort protocol maxMessageSize [additional-params...]"
                                        Span<Range> fields = stackalloc Range[4];
                                        var count = attrValue.Split(fields, ' ', StringSplitOptions.RemoveEmptyEntries);

                                        if (count >= 1)
                                        {
                                            var sctpPortSpan = attrValue[fields[0]];
                                            if (ushort.TryParse(sctpPortSpan, out var sctpPort))
                                            {
                                                activeAnnouncement.SctpPort = sctpPort;
                                            }
                                            else
                                            {
                                                logger.LogSdpInvalidSctpPort(sctpPortSpan);
                                            }
                                        }

                                        if (count >= 3)
                                        {
                                            var maxMessageSizeSpan = attrValue[fields[2]];
                                            if (!long.TryParse(maxMessageSizeSpan, out activeAnnouncement.MaxMessageSize))
                                            {
                                                logger.LogSdpInvalidMaxMessageSize(maxMessageSizeSpan);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSctpMapOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (ushort.TryParse(attrValue, out var sctpPort))
                                        {
                                            activeAnnouncement.SctpPort = sctpPort;
                                        }
                                        else
                                        {
                                            logger.LogSdpInvalidSctpPort(attrValue);
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSctpPortOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (!long.TryParse(attrValue, out activeAnnouncement.MaxMessageSize))
                                        {
                                            logger.LogSdpInvalidMaxMessageSize(attrValue);
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpMaxMessageSizeOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        var acceptTypesList = attrValue.Trim().SplitToList(' ');
                                        activeAnnouncement.MessageMediaFormat.AcceptTypes = acceptTypesList;
                                    }
                                    else
                                    {
                                        logger.LogSdpAcceptTypesOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_NAME:
                                {
                                    const string mediaFormatPathMsrpSchemeAndDelimiter = SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_SCHEME + "://";
                                    if (activeAnnouncement is { } && attrValue.StartsWith(mediaFormatPathMsrpSchemeAndDelimiter.AsSpan()))
                                    {
                                        const int mediaFormatPathMsrpSchemeAndDelimiterLength = 7;
                                        Debug.Assert(mediaFormatPathMsrpSchemeAndDelimiterLength == mediaFormatPathMsrpSchemeAndDelimiter.Length);

                                        attrValue = attrValue.Slice(mediaFormatPathMsrpSchemeAndDelimiterLength);
                                        var messageMediaFormatIP = attrValue.Slice(0, attrValue.IndexOf(':'));
                                        activeAnnouncement.MessageMediaFormat.IP = messageMediaFormatIP.ToString();

                                        attrValue = attrValue.Slice(messageMediaFormatIP.Length + 1);
                                        var messageMediaFormatPort = attrValue.Slice(0, attrValue.IndexOf('/'));
                                        activeAnnouncement.MessageMediaFormat.Port = messageMediaFormatPort.ToString();

                                        attrValue = attrValue.Slice(messageMediaFormatPort.Length + 1);
                                        var messageMediaFormatEndpoint = attrValue;
                                        activeAnnouncement.MessageMediaFormat.Endpoint = messageMediaFormatEndpoint.ToString();
                                    }
                                    else
                                    {
                                        logger.LogSdpPathOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case var _ when MediaStreamStatusType.IsMediaStreamStatusAttribute(line, out var mediaStreamStatus):
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.MediaStreamStatus = mediaStreamStatus;
                                    }
                                    else
                                    {
                                        sdp.SessionMediaStreamStatus = mediaStreamStatus;
                                    }
                                    break;
                                }
                        }
                    }

                    /// <summary>^(?&lt;id&gt;\d+)\s+(?&lt;attribute&gt;.*)$</summary>
                    static bool TryParseNumericIdAndStringAttribute(ReadOnlySpan<char> input, out int id, [NotNullWhen(true)] out Range attribute)
                    {
                        id = default;
                        attribute = default;

                        var digitEnd = input.IndexOfAnyExcept(SearchValueHelpers.DigitChars);

                        if (digitEnd <= 0)
                        {
                            // No digits at start or input is all digits (no attribute)
                            return false;
                        }

                        _ = int.TryParse(input[..digitEnd], out id); // not expected to fail

                        input = input[digitEnd..];
                        var nonWhitespaceIndex = input.IndexOfAnyExcept(SearchValueHelpers.WhiteSpaceChars);

                        if (nonWhitespaceIndex < 0)
                        {
                            // No non white spaces after id
                            return false;
                        }

                        attribute = (digitEnd + nonWhitespaceIndex)..;
                        return true;
                    }

                    /// <summary>^(?&lt;id&gt;\S+)\s+(?&lt;attribute&gt;.*)$</summary>
                    static bool TryParseStringIdAndStringAttribute(
                        ReadOnlySpan<char> input,
                        [NotNullWhen(true)] out string? id,
                        [NotNullWhen(true)] out string? attribute)
                    {
                        id = default;
                        attribute = default;

                        // Find the first whitespace (end of ID)
                        var idEnd = input.IndexOfAny(SearchValueHelpers.WhiteSpaceChars);
                        if (idEnd <= 0)
                        {
                            // Either starts with whitespace or no whitespace at all
                            return false;
                        }

                        id = input[..idEnd].ToString();

                        // Skip all whitespace after the ID
                        var attrStart = input[idEnd..].IndexOfAnyExcept(SearchValueHelpers.WhiteSpaceChars);
                        attribute = attrStart == -1
                            ? string.Empty
                            : input[(idEnd + attrStart)..].ToString();

                        return true;
                    }


                    /// <summary>^(?&lt;id&gt;\d+)\s+(?&lt;url&gt;.*)$</summary>
                    static bool TryParseNumericIdAndUrl(
                        ReadOnlySpan<char> input,
                        out int id,
                        [NotNullWhen(true)] out string? url)
                    {
                        id = default;
                        url = default;

                        // Find where the digits end
                        var digitEnd = input.IndexOfAnyExcept(SearchValueHelpers.DigitChars);
                        if (digitEnd <= 0 || digitEnd >= input.Length)
                        {
                            return false;
                        }

                        // Expect exactly one space after the digits
                        if (input[digitEnd] != ' ')
                        {
                            return false;
                        }

                        _ = int.TryParse(input[..digitEnd], out id); // not expected to fail

                        // The URL must be non-empty and contain no whitespace
                        var urlSpan = input[(digitEnd + 1)..];
                        if (urlSpan.IsEmpty || urlSpan.IndexOfAny(SearchValueHelpers.WhiteSpaceChars) != -1)
                        {
                            return false;
                        }

                        url = urlSpan.ToString();
                        return true;
                    }

                }

                return sdp;
            }
            catch (Exception excp)
            {
                logger.LogSdpParseException(excp.Message, excp);
                throw;
            }
        }
#nullable restore

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
            var builder = new StringBuilder();
            WriteString(builder);
            return builder.ToString();
        }

        public void WriteString(StringBuilder builder)
        {
            builder
                .Append("v=").Append(SDP_PROTOCOL_VERSION).Append(CRLF)
                .Append("o=")
                .Append(Username).Append(' ')
                .Append(SessionId).Append(' ')
                .Append(AnnouncementVersion).Append(' ')
                .Append(NetworkType).Append(' ')
                .Append(AddressType).Append(' ')
                .Append(AddressOrHost).Append(CRLF)
                .Append("s=").Append(SessionName).Append(CRLF);

            Connection?.WriteString(builder);

            foreach (var bandwidth in BandwidthAttributes)
            {
                builder.Append("b=").Append(bandwidth).Append(CRLF);
            }

            builder.Append("t=").Append(Timing).Append(CRLF);

            if (!string.IsNullOrWhiteSpace(IceUfrag))
            {
                builder.Append("a=" + ICE_UFRAG_ATTRIBUTE_PREFIX + ":").Append(IceUfrag).Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IcePwd))
            {
                builder.Append("a=" + ICE_PWD_ATTRIBUTE_PREFIX + ":").Append(IcePwd).Append(CRLF);
            }

            if (IceRole is { } iceRole)
            {
                builder.Append("a=" + ICE_SETUP_ATTRIBUTE_PREFIX + ":").Append(GetIceRoleName(iceRole)).Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(DtlsFingerprint))
            {
                builder.Append("a=" + DTLS_FINGERPRINT_ATTRIBUTE_PREFIX + ":").Append(DtlsFingerprint).Append(CRLF);
            }

            if (IceCandidates?.Count > 0)
            {
                foreach (var candidate in IceCandidates)
                {
                    builder.Append("a=" + ICE_CANDIDATE_ATTRIBUTE_PREFIX + ":").Append(candidate).Append(CRLF);
                }
            }

            if (!string.IsNullOrWhiteSpace(SessionDescription))
            {
                builder.Append("i=").Append(SessionDescription).Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(URI))
            {
                builder.Append("u=").Append(URI).Append(CRLF);
            }

            if (OriginatorEmailAddresses != null && OriginatorEmailAddresses.Length > 0)
            {
                foreach (var originatorAddress in OriginatorEmailAddresses)
                {
                    if (!string.IsNullOrWhiteSpace(originatorAddress))
                    {
                        builder.Append("e=").Append(originatorAddress).Append(CRLF);
                    }
                }
            }

            if (OriginatorPhoneNumbers != null && OriginatorPhoneNumbers.Length > 0)
            {
                foreach (var originatorNumber in OriginatorPhoneNumbers)
                {
                    if (!string.IsNullOrWhiteSpace(originatorNumber))
                    {
                        builder.Append("p=").Append(originatorNumber).Append(CRLF);
                    }
                }
            }

            if (Group != null)
            {
                builder.Append("a=" + GROUP_ATRIBUTE_PREFIX + ":").Append(Group).Append(CRLF);
            }

            foreach (var extra in ExtraSessionAttributes)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    builder.Append(extra).Append(CRLF);
                }
            }

            if (SessionMediaStreamStatus != null)
            {
                builder.Append(MediaStreamStatusType.GetAttributeForMediaStreamStatus(SessionMediaStreamStatus.Value)).Append(CRLF);
            }

            if (IsMediaSorted(Media))
            {
                foreach (var media in Media)
                {
                    media?.WriteString(builder);
                }
            }
            else
            {
                var medias = Media.ToArray();
                Array.Sort(medias, CompareMedia);

                foreach (var media in medias)
                {
                    media?.WriteString(builder);
                }
            }

            static bool IsMediaSorted(List<SDPMediaAnnouncement> media)
            {
                for (var i = 1; i < media.Count; i++)
                {
                    if (CompareMedia(media[i - 1], media[i]) > 0)
                    {
                        return false;
                    }
                }

                return true;
            }

            static int CompareMedia(SDPMediaAnnouncement x, SDPMediaAnnouncement y)
            {
                var comparison = x.MLineIndex.CompareTo(y.MLineIndex);
                return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(x.MediaID, y.MediaID);
            }

            // TODO: use https://www.nuget.org/packages/NetEscapades.EnumGenerators
            static string GetIceRoleName(IceRolesEnum iceRole) => iceRole switch
            {
                IceRolesEnum.actpass => nameof(IceRolesEnum.actpass),
                IceRolesEnum.passive => nameof(IceRolesEnum.passive),
                IceRolesEnum.active => nameof(IceRolesEnum.active),
                _ => iceRole.ToString()
            };
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
            else if (firstMediaOffer != null && firstMediaOffer.Connection != null)
            {
                return new IPEndPoint(IPAddress.Parse(firstMediaOffer.Connection.ConnectionAddress), firstMediaOffer.Port);
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
        /// <returns></returns>
        public (int, string) GetIndexForMediaType(SDPMediaTypesEnum mediaType, int mediaIndex)
        {
            int fullIndex = 0;
            int mIndex = 0;
            foreach (var ann in Media)
            {
                if (ann.Media == mediaType)
                {
                    if (mIndex == mediaIndex)
                    {
                        return (fullIndex, ann.MediaID);
                    }
                    mIndex++;
                }
                fullIndex++;
            }

            return (MEDIA_INDEX_NOT_PRESENT, MEDIA_INDEX_TAG_NOT_PRESENT);
        }
    }
}
