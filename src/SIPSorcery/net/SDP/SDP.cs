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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        /// <summary>
        /// Parses an SDP description, preserving the original text for <see cref="RawString"/>.
        /// </summary>
        public static SDP ParseSDPDescription(string sdpDescription)
            => ParseSDPDescription(sdpDescription.AsSpan(), sdpDescription);

#nullable enable
        /// <summary>
        /// Parses an SDP description, preserving a copy of the input for <see cref="RawString"/>.
        /// </summary>
        public static SDP? ParseSDPDescription(ReadOnlySpan<char> sdpDescription)
            => ParseSDPDescription(sdpDescription, null);

        private static SDP? ParseSDPDescription(ReadOnlySpan<char> sdpDescription, string? rawSdp)
        {
            if (sdpDescription.IsWhiteSpace())
            {
                return null;
            }

            try
            {
                var sdp = new SDP
                {
                    m_rawSdp = rawSdp ?? sdpDescription.ToString()
                };

                var mLineIndex = 0;
                SDPMediaAnnouncement? activeAnnouncement = null;

                // If a media announcement fmtp atribute is found before the rtpmap it will be stored
                // in this dictionary. A dynamic media format type cannot be created without an rtpmap.
                Dictionary<int, string>? pendingFmtp = null;

                // The SSRCs of an endpoint normally share one CNAME (RFC 7022), so the last CNAME parsed is kept to
                // reuse its string instead of allocating an equal one for every SSRC.
                string? lastCname = null;

                var remaining = sdpDescription;
                while (TryReadLine(ref remaining, out var rawLine))
                {
                    var line = rawLine.Trim();
                    if (line.IsEmpty)
                    {
                        continue;
                    }

                    // Only "<type>=<value>" lines have a type. Any other line is kept as an extra attribute.
                    var hasType = line.Length >= 2 && line[1] == '=';
                    var value = hasType ? line.Slice(2) : ReadOnlySpan<char>.Empty;

                    switch (hasType ? line[0] : '\0')
                    {
                        case 'v':
                            if (!decimal.TryParse(value, out sdp.Version))
                            {
                                logger.LogSdpInvalidVersion(line);
                            }
                            break;

                        case 'o':
                            ParseOrigin(line, sdp);
                            break;

                        case 's':
                            // "-" is the name RFC 8866 recommends when there is none, and "sipsorcery" is this library's
                            // default, so those reuse the literal instead of allocating a string.
                            sdp.SessionName = value switch
                            {
                                "-" => "-",
                                "sipsorcery" => "sipsorcery",
                                _ => value.ToString(),
                            };
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
                            ParseBandwidth(line, sdp, activeAnnouncement);
                            break;

                        case 't':
                            // Nearly every session is unbounded ("0 0"), which reuses the default instead of allocating.
                            sdp.Timing = value is DEFAULT_TIMING ? DEFAULT_TIMING : value.ToString();
                            break;

                        case 'm':
                            ParseMedia(line, sdp, ref activeAnnouncement, ref mLineIndex);
                            break;

                        case 'a':
                            ParseAttribute(line, sdp, activeAnnouncement, ref pendingFmtp, ref lastCname);
                            break;

                        default:
                            // Media stream status attributes match the whole line case-insensitively (e.g. "A=SENDRECV").
                            if (MediaStreamStatusType.IsMediaStreamStatusAttribute(line, out var mediaStreamStatus))
                            {
                                SetMediaStreamStatus(mediaStreamStatus, sdp, activeAnnouncement);
                            }
                            else
                            {
                                AddExtraAttribute(line, sdp, activeAnnouncement);
                            }
                            break;
                    }

                    static void ParseOrigin(ReadOnlySpan<char> line, SDP sdp)
                    {
                        var value = line.Slice(2);
                        Span<Range> fields = stackalloc Range[6];
                        var count = value.Split(fields, ' ', StringSplitOptions.RemoveEmptyEntries);

                        if (count >= 5)
                        {
                            sdp.Username = value[fields[0]].ToString();
                            sdp.SessionId = value[fields[1]].ToString();
                            sdp.AnnouncementVersion = ulong.TryParse(value[fields[2]], out var version) ? version : 0;
                            sdp.NetworkType = SDPConnectionInformation.GetNetworkType(value[fields[3]]);
                            sdp.AddressType = SDPConnectionInformation.GetAddressType(value[fields[4]]);
                            sdp.AddressOrHost = count > 5 ? value[fields[5]].ToString() : null;
                        }
                        else
                        {
                            logger.LogSdpInvalidSdpLineFormat(line);
                        }
                    }

                    static void ParseBandwidth(ReadOnlySpan<char> line, SDP sdp, SDPMediaAnnouncement? activeAnnouncement)
                    {
                        if (activeAnnouncement is { })
                        {
                            if (line.StartsWith(SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_PREFIX.AsSpan(), StringComparison.Ordinal))
                            {
                                if (uint.TryParse(line.Slice(SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_PREFIX.Length), out var tias))
                                {
                                    activeAnnouncement.TIASBandwidth = tias;
                                }
                            }
                            else
                            {
                                activeAnnouncement.BandwidthAttributes.Add(line.Slice(2).ToString());
                            }
                        }
                        else
                        {
                            sdp.BandwidthAttributes.Add(line.Slice(2).ToString());
                        }
                    }

                    static void ParseMedia(ReadOnlySpan<char> line, SDP sdp, ref SDPMediaAnnouncement? activeAnnouncement, ref int mLineIndex)
                    {
                        var mediaLine = line.Slice(2);
                        if (TryParseMediaDescription(
                            mediaLine,
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
                            logger.LogSdpInvalidMediaLine(mediaLine);
                        }

                        mLineIndex++;

                        /// <summary>
                        /// Reads the "&lt;media&gt; &lt;port&gt;[/&lt;number of ports&gt;] &lt;proto&gt; &lt;fmt&gt; ..." fields of a media line.
                        /// The first three fields are whitespace-delimited tokens and the formats are the rest of the line.
                        /// </summary>
                        static bool TryParseMediaDescription(
                            ReadOnlySpan<char> input,
                            out ReadOnlySpan<char> type,
                            out int port,
                            out int? portCount,
                            [NotNullWhen(true)] out string? transport,
                            out ReadOnlySpan<char> formats)
                        {
                            port = default;
                            portCount = default;
                            transport = default;
                            formats = default;

                            if (!TryReadToken(ref input, out type) ||
                                !TryReadToken(ref input, out var portToken) ||
                                !TryReadToken(ref input, out var transportToken))
                            {
                                return false;
                            }

                            // The port token is "<port>" or "<port>/<number of ports>".
                            var slashIndex = portToken.IndexOf('/');
                            var portSpan = slashIndex == -1 ? portToken : portToken.Slice(0, slashIndex);
                            if (!int.TryParse(portSpan, out port))
                            {
                                return false;
                            }

                            if (slashIndex != -1)
                            {
                                var portCountSpan = portToken.Slice(slashIndex + 1);
                                if (portCountSpan.IsEmpty || !int.TryParse(portCountSpan, out var parsedPortCount))
                                {
                                    return false;
                                }

                                portCount = parsedPortCount;
                            }

                            transport = transportToken switch
                            {
                                RTPSession.RTP_MEDIA_PROFILE => RTPSession.RTP_MEDIA_PROFILE,
                                RTPSession.RTP_SECUREMEDIA_PROFILE => RTPSession.RTP_SECUREMEDIA_PROFILE,
                                "RTP/AVPF" => "RTP/AVPF",
                                "RTP/SAVPF" => "RTP/SAVPF",
                                "UDP/TLS/RTP/SAVP" => "UDP/TLS/RTP/SAVP",
                                "UDP/TLS/RTP/SAVPF" => "UDP/TLS/RTP/SAVPF",
                                "DTLS/SCTP" => "DTLS/SCTP",
                                "UDP/DTLS/SCTP" => "UDP/DTLS/SCTP",
                                _ => transportToken.ToString()
                            };

                            formats = input.TrimStart();
                            return true;

                            /// <summary>Reads the next whitespace-delimited token and advances the input past it.</summary>
                            static bool TryReadToken(scoped ref ReadOnlySpan<char> input, out ReadOnlySpan<char> token)
                            {
                                input = input.TrimStart();
                                if (input.IsEmpty)
                                {
                                    token = default;
                                    return false;
                                }

                                var tokenEnd = input.IndexOfAny(SearchValueHelpers.WhiteSpaceChars);
                                if (tokenEnd == -1)
                                {
                                    tokenEnd = input.Length;
                                }

                                token = input.Slice(0, tokenEnd);
                                input = input.Slice(tokenEnd);
                                return true;
                            }
                        }
                    }

                    static void ParseAttribute(
                        ReadOnlySpan<char> line,
                        SDP sdp,
                        SDPMediaAnnouncement? activeAnnouncement,
                        ref Dictionary<int, string>? pendingFmtp,
                        ref string? lastCname)
                    {
                        var value = line.Slice(2);
                        var colonIndex = value.IndexOf(':');
                        var hasColon = colonIndex != -1;
                        var name = hasColon ? value.Slice(0, colonIndex) : value;

                        // Without a colon, the whole line is used as the attribute value.
                        var attrValue = hasColon ? value.Slice(colonIndex + 1) : line;

                        if (TryParseNamedAttribute(name, line, attrValue, hasColon, sdp, activeAnnouncement, ref pendingFmtp, ref lastCname))
                        {
                            return;
                        }

                        // Some attributes are also recognised when the name only starts with theirs (e.g. "a=candidates:"
                        // is parsed as a candidate), so an unrecognised name is retried with the name it starts with.
                        if (GetPrefixedAttributeName(name) is { } prefixedName &&
                            TryParseNamedAttribute(prefixedName.AsSpan(), line, attrValue, hasColon, sdp, activeAnnouncement, ref pendingFmtp, ref lastCname))
                        {
                            return;
                        }

                        AddExtraAttribute(line, sdp, activeAnnouncement);
                    }

                    /// <summary>
                    /// Gets the name of the prefix-matched attribute that an attribute name starts with, if any.
                    /// </summary>
                    /// <remarks>
                    /// Every unrecognised attribute (e.g. "rtcp-fb") gets here, so the first character selects the
                    /// prefixes worth comparing. A name can only start with a prefix that has the same first character,
                    /// and the arms keep their original order, so the result is the same as comparing each in turn.
                    /// </remarks>
                    static string? GetPrefixedAttributeName(ReadOnlySpan<char> name)
                    {
                        if (name.IsEmpty)
                        {
                            return null;
                        }

                        return name[0] switch
                        {
                            'g' when name.StartsWith(GROUP_ATRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => GROUP_ATRIBUTE_PREFIX,
                            'i' when name.StartsWith(ICE_LITE_IMPLEMENTATION_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => ICE_LITE_IMPLEMENTATION_ATTRIBUTE_PREFIX,
                            'i' when name.StartsWith(ICE_UFRAG_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => ICE_UFRAG_ATTRIBUTE_PREFIX,
                            'i' when name.StartsWith(ICE_PWD_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => ICE_PWD_ATTRIBUTE_PREFIX,
                            's' when name.StartsWith(ICE_SETUP_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => ICE_SETUP_ATTRIBUTE_PREFIX,
                            'f' when name.StartsWith(DTLS_FINGERPRINT_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => DTLS_FINGERPRINT_ATTRIBUTE_PREFIX,
                            'c' when name.StartsWith(ICE_CANDIDATE_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => ICE_CANDIDATE_ATTRIBUTE_PREFIX,
                            'm' when name.StartsWith(MEDIA_ID_ATTRIBUTE_PREFIX.AsSpan(), StringComparison.Ordinal) => MEDIA_ID_ATTRIBUTE_PREFIX,
                            _ => null,
                        };
                    }

                    /// <summary>
                    /// Parses an attribute with a recognised name. Returns false if the attribute is not recognised.
                    /// </summary>
                    static bool TryParseNamedAttribute(
                        ReadOnlySpan<char> name,
                        ReadOnlySpan<char> line,
                        ReadOnlySpan<char> attrValue,
                        bool hasColon,
                        SDP sdp,
                        SDPMediaAnnouncement? activeAnnouncement,
                        ref Dictionary<int, string>? pendingFmtp,
                        ref string? lastCname)
                    {
                        switch (name)
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
                                    if (hasColon)
                                    {
                                        if (Enum.TryParse<IceRolesEnum>(attrValue.Trim(), true, out var iceRole))
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
                            case END_ICE_CANDIDATES_ATTRIBUTE when !hasColon:
                                {
                                    // TODO: Set a flag.
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_NAME when hasColon:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video)
                                        {
                                            if (TryParseNumericIdAndUrl(attrValue, out var extensionId, out var uri))
                                            {
                                                var rtpExtension = RTPHeaderExtension.GetRTPHeaderExtensionCore(extensionId, uri, activeAnnouncement.Media);
                                                if (rtpExtension is { })
                                                {
                                                    activeAnnouncement.HeaderExtensions.TryAdd(extensionId, rtpExtension);
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUTE_NAME when hasColon:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                                        {
                                            // Parse the rtpmap attribute for audio/video announcements.
                                            if (TrySplitIdAndStringAttribute(attrValue, out var formatIdRange, out var rtpmap))
                                            {
                                                if (!int.TryParse(attrValue[formatIdRange], out var formatId))
                                                {
                                                    logger.LogSdpNonNumericMediaFormatAttribute(line);
                                                    break;
                                                }

                                                if (activeAnnouncement.MediaFormats.TryGetValue(formatId, out var mediaFormat))
                                                {
                                                    activeAnnouncement.MediaFormats[formatId] = mediaFormat.WithUpdatedRtpmap(GetRtpmapString(attrValue[rtpmap]));
                                                }
                                                else
                                                {
                                                    // A pending fmtp is not removed when used, so it also applies to later
                                                    // media announcements that define the same format ID.
                                                    var fmtp = pendingFmtp is not null && pendingFmtp.TryGetValue(formatId, out var pending) ? pending : null;
                                                    activeAnnouncement.MediaFormats.Add(
                                                        formatId,
                                                        new SDPAudioVideoMediaFormat(
                                                            activeAnnouncement.Media,
                                                            formatId,
                                                            GetRtpmapString(attrValue[rtpmap]),
                                                            fmtp));
                                                }
                                            }
                                            else
                                            {
                                                activeAnnouncement.AddExtra(line.ToString());
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
                            case SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_NAME when hasColon:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                                        {
                                            // Parse the fmtp attribute for audio/video announcements.
                                            if (TrySplitIdAndStringAttribute(attrValue, out var avFormatIdRange, out var fmtp))
                                            {
                                                if (!int.TryParse(attrValue[avFormatIdRange], out var avFormatID))
                                                {
                                                    logger.LogSdpInvalidMediaFormatParameterAttribute(line);
                                                    break;
                                                }

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
                            case SDPSecurityDescription.CRYPTO_ATTRIBUTE_NAME when hasColon:
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
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_NAME when hasColon:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        var fieldIndex = 0;
                                        foreach (var fieldRange in attrValue.Split(' '))
                                        {
                                            var field = attrValue[fieldRange];
                                            if (fieldIndex == 0)
                                            {
                                                // Set the ID.
                                                activeAnnouncement.SsrcGroupID = field.ToString();
                                            }
                                            else if (uint.TryParse(field, out var ssrc))
                                            {
                                                // Add attributes for each of the SSRC values.
                                                activeAnnouncement.SsrcAttributes.Add(new SDPSsrcAttribute(ssrc, null, activeAnnouncement.SsrcGroupID));
                                            }

                                            fieldIndex++;
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSsrcGroupIdOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_NAME when hasColon:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        // The source attribute is optional: "a=ssrc:<id>" on its own still registers the SSRC.
                                        var ssrcField = ReadOnlySpan<char>.Empty;
                                        var cnameField = ReadOnlySpan<char>.Empty;
                                        var fieldIndex = 0;
                                        foreach (var fieldRange in attrValue.Split(' '))
                                        {
                                            if (fieldIndex == 0)
                                            {
                                                ssrcField = attrValue[fieldRange];
                                            }
                                            else
                                            {
                                                cnameField = attrValue[fieldRange];
                                                break;
                                            }

                                            fieldIndex++;
                                        }

                                        if (uint.TryParse(ssrcField, out var ssrc))
                                        {
                                            if (GetFirstMatchingSsrcAttribute(activeAnnouncement, ssrc) is not { } ssrcAttribute)
                                            {
                                                ssrcAttribute = new SDPSsrcAttribute(ssrc, null, null);
                                                activeAnnouncement.SsrcAttributes.Add(ssrcAttribute);
                                            }

                                            if (cnameField.StartsWith(SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX.AsSpan(), StringComparison.Ordinal))
                                            {
                                                var cname = cnameField.Slice(cnameField.IndexOf(':') + 1);
                                                if (lastCname is null || !cname.SequenceEqual(lastCname.AsSpan()))
                                                {
                                                    lastCname = cname.ToString();
                                                }

                                                ssrcAttribute.Cname = lastCname;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSsrcAttributeOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_NAME when hasColon:
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        activeAnnouncement.SctpMap = attrValue.ToString();

                                        // Parse sctp-port and max-message-size from space-separated values
                                        // Format: "sctpPort protocol maxMessageSize [additional-params...]"
                                        var sctpPortField = ReadOnlySpan<char>.Empty;
                                        var maxMessageSizeField = ReadOnlySpan<char>.Empty;
                                        var fieldIndex = 0;
                                        foreach (var fieldRange in attrValue.Split(' '))
                                        {
                                            if (fieldIndex == 0)
                                            {
                                                sctpPortField = attrValue[fieldRange];
                                            }
                                            else if (fieldIndex == 2)
                                            {
                                                maxMessageSizeField = attrValue[fieldRange];
                                                break;
                                            }

                                            fieldIndex++;
                                        }

                                        if (ushort.TryParse(sctpPortField, out var sctpPort))
                                        {
                                            activeAnnouncement.SctpPort = sctpPort;
                                        }
                                        else
                                        {
                                            logger.LogSdpInvalidSctpPort(sctpPortField);
                                        }

                                        // A missing max-message-size field also resets the value to 0 and is logged.
                                        if (!long.TryParse(maxMessageSizeField, out activeAnnouncement.MaxMessageSize))
                                        {
                                            logger.LogSdpInvalidMaxMessageSize(maxMessageSizeField);
                                        }
                                    }
                                    else
                                    {
                                        logger.LogSdpSctpMapOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            case SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_NAME when hasColon:
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
                            case SDPMediaAnnouncement.MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_NAME when hasColon:
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
                            case SDPMediaAnnouncement.MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME when hasColon:
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
                            // Only msrp paths are parsed. Other schemes (e.g. msrps for MSRP over TLS) are kept as extra attributes.
                            case SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_NAME when line.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_PREFIX.AsSpan(), StringComparison.Ordinal):
                                {
                                    if (activeAnnouncement is { })
                                    {
                                        // Skip the "msrp:" scheme and the two characters after it ("//"), then split "<ip>:<port>/<endpoint>".
                                        var path = attrValue.Slice(SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_SCHEME.Length + 3);
                                        activeAnnouncement.MessageMediaFormat.IP = path.Slice(0, path.IndexOf(':')).ToString();

                                        path = path.Slice(path.IndexOf(':') + 1);
                                        activeAnnouncement.MessageMediaFormat.Port = path.Slice(0, path.IndexOf('/')).ToString();

                                        path = path.Slice(path.IndexOf('/') + 1);
                                        activeAnnouncement.MessageMediaFormat.Endpoint = path.ToString();
                                    }
                                    else
                                    {
                                        logger.LogSdpPathOnlyOnAnnouncement();
                                    }
                                    break;
                                }
                            // a=rtcp-fb:<pt|*> <type> [<subtype>] (RFC 4585 section 4.2). Only video formats write feedback
                            // attributes, so for other media, and at session level, the lines are kept as extra attributes.
                            case SDPMediaAnnouncement.MEDIA_FORMAT_FEEDBACK_NAME when hasColon && activeAnnouncement is { Media: SDPMediaTypesEnum.video }:
                                {
                                    if (TrySplitIdAndStringAttribute(attrValue, out var formatIDRange, out var feedbackRange))
                                    {
                                        activeAnnouncement.AddParsedRtcpFeedback(attrValue[formatIDRange], attrValue[feedbackRange], line);
                                    }
                                    else
                                    {
                                        activeAnnouncement.AddExtra(line.ToString());
                                    }
                                    break;
                                }
                            case var _ when MediaStreamStatusType.IsMediaStreamStatusAttribute(line, out var mediaStreamStatus):
                                {
                                    SetMediaStreamStatus(mediaStreamStatus, sdp, activeAnnouncement);
                                    break;
                                }
                            default:
                                {
                                    return false;
                                }
                        }

                        return true;

                        static SDPSsrcAttribute? GetFirstMatchingSsrcAttribute(SDPMediaAnnouncement activeAnnouncement, uint ssrc)
                        {
                            foreach (var ssrcAttribute in activeAnnouncement.SsrcAttributes)
                            {
                                if (ssrcAttribute.SSRC == ssrc)
                                {
                                    return ssrcAttribute;
                                }
                            }

                            return null;
                        }

                        // Browsers and phones offer the same few encodings, often several times (e.g. one rtx entry
                        // per video codec), so those reuse a literal instead of allocating a string each time.
                        // Anything else, including the same encodings in a different case, is copied as before.
                        static string GetRtpmapString(ReadOnlySpan<char> rtpmap)
                            => rtpmap switch
                            {
                                "opus/48000/2" => "opus/48000/2",
                                "red/48000/2" => "red/48000/2",
                                "G722/8000" => "G722/8000",
                                "G722/8000/1" => "G722/8000/1",
                                "PCMU/8000" => "PCMU/8000",
                                "PCMA/8000" => "PCMA/8000",
                                "G729/8000" => "G729/8000",
                                "ILBC/8000" => "ILBC/8000",
                                "ISAC/16000" => "ISAC/16000",
                                "ISAC/32000" => "ISAC/32000",
                                "CN/8000" => "CN/8000",
                                "CN/16000" => "CN/16000",
                                "CN/32000" => "CN/32000",
                                "telephone-event/8000" => "telephone-event/8000",
                                "telephone-event/8000/1" => "telephone-event/8000/1",
                                "telephone-event/16000" => "telephone-event/16000",
                                "telephone-event/32000" => "telephone-event/32000",
                                "telephone-event/48000" => "telephone-event/48000",
                                "VP8/90000" => "VP8/90000",
                                "VP9/90000" => "VP9/90000",
                                "H264/90000" => "H264/90000",
                                "H265/90000" => "H265/90000",
                                "AV1/90000" => "AV1/90000",
                                "rtx/90000" => "rtx/90000",
                                "red/90000" => "red/90000",
                                "ulpfec/90000" => "ulpfec/90000",
                                "flexfec-03/90000" => "flexfec-03/90000",
                                _ => rtpmap.ToString(),
                            };
                    }

                    static void AddExtraAttribute(ReadOnlySpan<char> line, SDP sdp, SDPMediaAnnouncement? activeAnnouncement)
                    {
                        if (activeAnnouncement is { })
                        {
                            activeAnnouncement.AddExtra(line.ToString());
                        }
                        else
                        {
                            sdp.AddExtra(line.ToString());
                        }
                    }

                    static void SetMediaStreamStatus(MediaStreamStatusEnum mediaStreamStatus, SDP sdp, SDPMediaAnnouncement? activeAnnouncement)
                    {
                        if (activeAnnouncement is { })
                        {
                            activeAnnouncement.MediaStreamStatus = mediaStreamStatus;
                        }
                        else
                        {
                            sdp.SessionMediaStreamStatus = mediaStreamStatus;
                        }
                    }

                    /// <summary>Splits a whitespace-delimited ID and attribute without allocating strings.</summary>
                    static bool TrySplitIdAndStringAttribute(ReadOnlySpan<char> input, out Range id, out Range attribute)
                    {
                        id = default;
                        attribute = default;

                        var trimmedInput = input.TrimStart();
                        var idEnd = trimmedInput.IndexOfAny(SearchValueHelpers.WhiteSpaceChars);
                        if (idEnd <= 0)
                        {
                            return false;
                        }

                        var attributeSpan = trimmedInput[idEnd..].TrimStart();
                        if (attributeSpan.IsEmpty)
                        {
                            return false;
                        }

                        var idStart = input.Length - trimmedInput.Length;
                        id = idStart..(idStart + idEnd);
                        attribute = (input.Length - attributeSpan.Length)..;

                        return true;
                    }

                    /// <summary>Parses a whitespace-delimited string ID and its attribute.</summary>
                    static bool TryParseStringIdAndStringAttribute(
                        ReadOnlySpan<char> input,
                        [NotNullWhen(true)] out string? id,
                        [NotNullWhen(true)] out string? attribute)
                    {
                        id = default;
                        attribute = default;

                        if (!TrySplitIdAndStringAttribute(input, out var idRange, out var attributeRange))
                        {
                            return false;
                        }

                        id = input[idRange].ToString();
                        attribute = input[attributeRange].ToString();

                        return true;
                    }

                    /// <summary>Parses a numeric extension ID followed by a single URL token without allocating the URL.</summary>
                    static bool TryParseNumericIdAndUrl(
                        ReadOnlySpan<char> input,
                        out int id,
                        out ReadOnlySpan<char> url)
                    {
                        id = default;
                        url = default;

                        if (!TrySplitIdAndStringAttribute(input, out var idRange, out var urlRange) ||
                            !int.TryParse(input[idRange], out id))
                        {
                            return false;
                        }

                        var urlSpan = input[urlRange];
                        if (urlSpan.IndexOfAny(SearchValueHelpers.WhiteSpaceChars) != -1)
                        {
                            return false;
                        }

                        url = urlSpan;
                        return true;
                    }

                }

                foreach (var announcement in sdp.Media)
                {
                    announcement.ApplyParsedRtcpFeedback();
                }

                return sdp;

                /// <summary>Reads the next line, which ends at a CR, an LF or a CR LF pair, and advances the input past the line ending.</summary>
                static bool TryReadLine(scoped ref ReadOnlySpan<char> input, out ReadOnlySpan<char> line)
                {
                    // Only CR and LF end an SDP line, so EnumerateLines is not used: it also ends lines at FF, NEL, LS and PS.
                    // A CR LF pair is consumed as a single ending, so it does not produce an empty line to be skipped.
                    if (input.IsEmpty)
                    {
                        line = default;
                        return false;
                    }

                    var lineEnd = input.IndexOfAny('\r', '\n');
                    if (lineEnd == -1)
                    {
                        line = input;
                        input = default;
                        return true;
                    }

                    line = input.Slice(0, lineEnd);

                    var next = lineEnd + 1;
                    if (input[lineEnd] == '\r' && next < input.Length && input[next] == '\n')
                    {
                        next++;
                    }

                    input = input.Slice(next);
                    return true;
                }
            }
            catch (Exception excp)
            {
                logger.LogSdpParseException(excp);
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

        /// <summary>
        /// Returns the original parsed text, or a generated description for a new instance.
        /// </summary>
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
