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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Polyfills;
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

        public static SDP ParseSDPDescription(string sdpDescription)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(sdpDescription))
                {
                    SDP sdp = new SDP();
                    sdp.m_rawSdp = sdpDescription;
                    int mLineIndex = 0;
                    SDPMediaAnnouncement activeAnnouncement = null;

                    // If a media announcement fmtp atribute is found before the rtpmap it will be stored
                    // in this dictionary. A dynamic media format type cannot be created without an rtpmap.
                    Dictionary<int, string> _pendingFmtp = new Dictionary<int, string>();

                    var sdpDescriptionSpan = sdpDescription.AsSpan();
                    Span<Range> ownerFieldRanges = stackalloc Range[6];
                    const StringSplitOptions TrimEntries = (StringSplitOptions)2;
                    const StringSplitOptions RemoveEmptyAndTrimSplitOptions = StringSplitOptions.RemoveEmptyEntries | TrimEntries;

                    static bool StartsWithAttribute(ReadOnlySpan<char> line, string attributePrefix) =>
                        line.StartsWith("a=", StringComparison.Ordinal) &&
                        line.Slice(2).StartsWith(attributePrefix, StringComparison.Ordinal);

                    static bool EqualsAttribute(ReadOnlySpan<char> line, string attribute) =>
                        line.Length == attribute.Length + 2 &&
                        line.StartsWith("a=", StringComparison.Ordinal) &&
                        line.Slice(2).Equals(attribute.AsSpan(), StringComparison.Ordinal);

                    static ReadOnlySpan<char> SliceAfterColon(ReadOnlySpan<char> line) =>
                        line.Slice(line.IndexOf(':') + 1);

                    static bool TryReadToken(ReadOnlySpan<char> value, ref int offset, out int tokenStart, out int tokenLength)
                    {
                        while (offset < value.Length && char.IsWhiteSpace(value[offset]))
                        {
                            offset++;
                        }

                        if (offset == value.Length)
                        {
                            tokenStart = 0;
                            tokenLength = 0;
                            return false;
                        }

                        tokenStart = offset;
                        var endIndex = offset;
                        while (endIndex < value.Length && !char.IsWhiteSpace(value[endIndex]))
                        {
                            endIndex++;
                        }

                        tokenLength = endIndex - tokenStart;
                        offset = endIndex;
                        return true;
                    }

                    static bool TrySplitAttributeValue(
                        ReadOnlySpan<char> line,
                        int prefixLength,
                        out int idStart,
                        out int idLength,
                        out int attributeStart)
                    {
                        var offset = prefixLength;
                        if (!TryReadToken(line, ref offset, out idStart, out idLength))
                        {
                            attributeStart = 0;
                            return false;
                        }

                        while (offset < line.Length && char.IsWhiteSpace(line[offset]))
                        {
                            offset++;
                        }

                        attributeStart = offset;
                        return attributeStart < line.Length;
                    }

                    static bool TryParseMediaLine(
                        ReadOnlySpan<char> mediaLine,
                        out int mediaTypeStart,
                        out int mediaTypeLength,
                        out int port,
                        out int? portCount,
                        out int transportStart,
                        out int transportLength,
                        out int formatsStart)
                    {
                        mediaTypeStart = 0;
                        mediaTypeLength = 0;
                        port = 0;
                        portCount = null;
                        transportStart = 0;
                        transportLength = 0;
                        formatsStart = 0;

                        var offset = 0;
                        if (!TryReadToken(mediaLine, ref offset, out mediaTypeStart, out mediaTypeLength) ||
                            !TryReadToken(mediaLine, ref offset, out var portStart, out var portLength) ||
                            !TryReadToken(mediaLine, ref offset, out transportStart, out transportLength))
                        {
                            return false;
                        }

                        var portToken = mediaLine.Slice(portStart, portLength);
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

                        while (offset < mediaLine.Length && char.IsWhiteSpace(mediaLine[offset]))
                        {
                            offset++;
                        }

                        formatsStart = offset;
                        return true;
                    }

                    static bool TryParseExtensionMap(ReadOnlySpan<char> line, out int id, out int uriStart, out int uriLength)
                    {
                        id = 0;
                        uriStart = 0;
                        uriLength = 0;
                        var offset = SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX.Length;

                        if (!TryReadToken(line, ref offset, out var idStart, out var idLength) ||
                            !int.TryParse(line.Slice(idStart, idLength), out id) ||
                            !TryReadToken(line, ref offset, out uriStart, out uriLength))
                        {
                            return false;
                        }

                        while (offset < line.Length && char.IsWhiteSpace(line[offset]))
                        {
                            offset++;
                        }

                        return offset == line.Length;
                    }

                    static bool TryParseMediaStreamStatus(ReadOnlySpan<char> attribute, out MediaStreamStatusEnum mediaStreamStatus)
                    {
                        mediaStreamStatus = MediaStreamStatusEnum.SendRecv;

                        if (attribute.Equals(MediaStreamStatusType.SEND_RECV_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            mediaStreamStatus = MediaStreamStatusEnum.SendRecv;
                            return true;
                        }

                        if (attribute.Equals(MediaStreamStatusType.SEND_ONLY_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            mediaStreamStatus = MediaStreamStatusEnum.SendOnly;
                            return true;
                        }

                        if (attribute.Equals(MediaStreamStatusType.RECV_ONLY_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            mediaStreamStatus = MediaStreamStatusEnum.RecvOnly;
                            return true;
                        }

                        if (attribute.Equals(MediaStreamStatusType.INACTIVE_ATTRIBUTE.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            mediaStreamStatus = MediaStreamStatusEnum.Inactive;
                            return true;
                        }

                        return false;
                    }

                    var sdpLineRangeBuffer = ArrayPool<Range>.Shared.Rent(sdpDescriptionSpan.Length + 1);
                    try
                    {
                        var sdpLineRanges = sdpLineRangeBuffer.AsSpan(0, sdpDescriptionSpan.Length + 1);
                        var sdpLineCount = sdpDescriptionSpan.SplitAny(
                            sdpLineRanges,
                            "\r\n".AsSpan(),
                            RemoveEmptyAndTrimSplitOptions);

                        for (var sdpLineIndex = 0; sdpLineIndex < sdpLineCount; sdpLineIndex++)
                        {
                            var sdpLineTrimmedSpan = sdpDescriptionSpan[sdpLineRanges[sdpLineIndex]];

                            switch (sdpLineTrimmedSpan)
                            {
                                case var _ when sdpLineTrimmedSpan.StartsWith("v=", StringComparison.Ordinal):
                                    if (!Decimal.TryParse(sdpLineTrimmedSpan.Slice(2), out sdp.Version))
                                    {
                                        logger.LogWarning("The Version value in an SDP description could not be parsed as a decimal: {sdpLine}.", sdpLineTrimmedSpan.ToString());
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith("o=", StringComparison.Ordinal):
                                    var ownerFieldsSpan = sdpLineTrimmedSpan.Slice(2);
                                    var ownerFieldCount = ownerFieldsSpan.Split(ownerFieldRanges, ' ', StringSplitOptions.RemoveEmptyEntries);

                                    if (ownerFieldCount >= 5)
                                    {
                                        sdp.Username = ownerFieldsSpan[ownerFieldRanges[0]].ToString();
                                        sdp.SessionId = ownerFieldsSpan[ownerFieldRanges[1]].ToString();
                                        sdp.AnnouncementVersion = UInt64.TryParse(ownerFieldsSpan[ownerFieldRanges[2]].ToString(), out var version) ? version : 0;
                                        sdp.NetworkType = ownerFieldsSpan[ownerFieldRanges[3]].ToString();
                                        sdp.AddressType = ownerFieldsSpan[ownerFieldRanges[4]].ToString();
                                        sdp.AddressOrHost = ownerFieldCount > 5 ? ownerFieldsSpan[ownerFieldRanges[5]].ToString() : null;
                                    }
                                    else
                                    {
                                        logger.LogWarning("The SDP message had an invalid SDP line format for 'o=': {sdpLineTrimmed}", sdpLineTrimmedSpan.ToString());
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith("s=", StringComparison.Ordinal):
                                    sdp.SessionName = sdpLineTrimmedSpan.Slice(2).ToString();
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith("i=", StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.MediaDescription = sdpLineTrimmedSpan.Slice(2).ToString();
                                    }
                                    else
                                    {
                                        sdp.SessionDescription = sdpLineTrimmedSpan.Slice(2).ToString();
                                    }

                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith("c=", StringComparison.Ordinal):

                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.Connection = SDPConnectionInformation.ParseConnectionInformation(sdpLineTrimmedSpan.ToString());
                                    }
                                    else if (sdp.Connection == null)
                                    {
                                        sdp.Connection = SDPConnectionInformation.ParseConnectionInformation(sdpLineTrimmedSpan.ToString());
                                    }
                                    else
                                    {
                                        logger.LogWarning("The SDP message had a duplicate connection attribute which was ignored.");
                                    }

                                    break;

                                case var l when l.StartsWith("b=", StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        if (l.StartsWith(SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_PREFIX, StringComparison.Ordinal))
                                        {
                                            if (uint.TryParse(SliceAfterColon(l), out var tias))
                                            {
                                                activeAnnouncement.TIASBandwidth = tias;
                                            }
                                        }
                                        else
                                        {
                                            activeAnnouncement.BandwidthAttributes.Add(sdpLineTrimmedSpan.Slice(2).ToString());
                                        }
                                    }
                                    else
                                    {
                                        sdp.BandwidthAttributes.Add(sdpLineTrimmedSpan.Slice(2).ToString());
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith("t=", StringComparison.Ordinal):
                                    sdp.Timing = sdpLineTrimmedSpan.Slice(2).ToString();
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith("m=", StringComparison.Ordinal):
                                    var mediaLine = sdpLineTrimmedSpan.Slice(2);
                                    if (TryParseMediaLine(
                                        mediaLine,
                                        out var mediaTypeStart,
                                        out var mediaTypeLength,
                                        out var port,
                                        out var portCount,
                                        out var transportStart,
                                        out var transportLength,
                                        out var formatsStart))
                                    {
                                        var announcement = new SDPMediaAnnouncement();
                                        announcement.MLineIndex = mLineIndex;
                                        announcement.Media = SDPMediaTypes.GetSDPMediaType(mediaLine.Slice(mediaTypeStart, mediaTypeLength).ToString());

                                        // Parse the primary port.
                                        announcement.Port = port;
                                        if (portCount.HasValue)
                                        {
                                            announcement.PortCount = portCount.Value;
                                        }

                                        announcement.Transport = mediaLine.Slice(transportStart, transportLength).ToString();
                                        announcement.ParseMediaFormats(mediaLine.Slice(formatsStart).ToString());
                                        if (announcement.Media == SDPMediaTypesEnum.audio || announcement.Media == SDPMediaTypesEnum.video || announcement.Media == SDPMediaTypesEnum.text)
                                        {
                                            announcement.MediaStreamStatus = sdp.SessionMediaStreamStatus != null ? sdp.SessionMediaStreamStatus.Value :
                                                MediaStreamStatusEnum.SendRecv;
                                        }
                                        sdp.Media.Add(announcement);

                                        activeAnnouncement = announcement;
                                    }
                                    else
                                    {
                                        logger.LogWarning("A media line in SDP was invalid: {sdpLine}.", sdpLineTrimmedSpan.Slice(2).ToString());
                                    }

                                    mLineIndex++;
                                    break;

                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, GROUP_ATRIBUTE_PREFIX):
                                    sdp.Group = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    break;
                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, ICE_LITE_IMPLEMENTATION_ATTRIBUTE_PREFIX):
                                    sdp.IceImplementation = IceImplementationEnum.lite;
                                    break;
                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, ICE_UFRAG_ATTRIBUTE_PREFIX):
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.IceUfrag = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    else
                                    {
                                        sdp.IceUfrag = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    break;

                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, ICE_PWD_ATTRIBUTE_PREFIX):
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.IcePwd = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    else
                                    {
                                        sdp.IcePwd = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    break;

                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, ICE_SETUP_ATTRIBUTE_PREFIX):
                                    var colonIndex = sdpLineTrimmedSpan.IndexOf(':');
                                    if (colonIndex != -1 && sdpLineTrimmedSpan.Length > colonIndex)
                                    {
                                        var iceRoleStr = sdpLineTrimmedSpan.Slice(colonIndex + 1).Trim().ToString();
                                        if (Enum.TryParse<IceRolesEnum>(iceRoleStr, true, out var iceRole))
                                        {
                                            if (activeAnnouncement != null)
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
                                            logger.LogWarning("ICE role was not recognised from SDP attribute: {sdpLineTrimmed}.", sdpLineTrimmedSpan.ToString());
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("ICE role SDP attribute was missing the mandatory colon: {sdpLineTrimmed}.", sdpLineTrimmedSpan.ToString());
                                    }
                                    break;

                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, DTLS_FINGERPRINT_ATTRIBUTE_PREFIX):
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.DtlsFingerprint = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    else
                                    {
                                        sdp.DtlsFingerprint = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    break;

                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, ICE_CANDIDATE_ATTRIBUTE_PREFIX):
                                    if (activeAnnouncement != null)
                                    {
                                        if (activeAnnouncement.IceCandidates == null)
                                        {
                                            activeAnnouncement.IceCandidates = new List<string>();
                                        }
                                        activeAnnouncement.IceCandidates.Add(SliceAfterColon(sdpLineTrimmedSpan).ToString());
                                    }
                                    else
                                    {
                                        if (sdp.IceCandidates == null)
                                        {
                                            sdp.IceCandidates = new List<string>();
                                        }
                                        sdp.IceCandidates.Add(SliceAfterColon(sdpLineTrimmedSpan).ToString());
                                    }
                                    break;

                                case var _ when EqualsAttribute(sdpLineTrimmedSpan, END_ICE_CANDIDATES_ATTRIBUTE):
                                    // TODO: Set a flag.
                                    break;
                                case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null &&
                                        (activeAnnouncement.Media == SDPMediaTypesEnum.audio || activeAnnouncement.Media == SDPMediaTypesEnum.video) &&
                                        TryParseExtensionMap(l, out var extensionId, out var uriStart, out var uriLength))
                                    {
                                        var rtpExtension = RTPHeaderExtension.GetRTPHeaderExtension(extensionId, l.Slice(uriStart, uriLength).ToString(), activeAnnouncement.Media);
                                        if ((rtpExtension != null) && !activeAnnouncement.HeaderExtensions.ContainsKey(extensionId))
                                        {
                                            activeAnnouncement.HeaderExtensions.Add(extensionId, rtpExtension);
                                        }
                                    }

                                    break;
                                case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUTE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        if (activeAnnouncement.Media == SDPMediaTypesEnum.audio || activeAnnouncement.Media == SDPMediaTypesEnum.video || activeAnnouncement.Media == SDPMediaTypesEnum.text)
                                        {
                                            // Parse the rtpmap attribute for audio/video announcements.
                                            if (TrySplitAttributeValue(
                                               l,
                                               SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUTE_PREFIX.Length,
                                               out var formatIDStart,
                                               out var formatIDLength,
                                               out var rtpmapStart))
                                            {
                                                var formatID = l.Slice(formatIDStart, formatIDLength);
                                                if (int.TryParse(formatID, out var mediaFormatId))
                                                {
                                                    var rtpmap = l.Slice(rtpmapStart).ToString();
                                                    if (activeAnnouncement.MediaFormats.ContainsKey(mediaFormatId))
                                                    {
                                                        activeAnnouncement.MediaFormats[mediaFormatId] = activeAnnouncement.MediaFormats[mediaFormatId].WithUpdatedRtpmap(rtpmap);
                                                    }
                                                    else
                                                    {
                                                        var fmtp = _pendingFmtp.ContainsKey(mediaFormatId) ? _pendingFmtp[mediaFormatId] : null;
                                                        activeAnnouncement.MediaFormats.Add(mediaFormatId, new SDPAudioVideoMediaFormat(activeAnnouncement.Media, mediaFormatId, rtpmap, fmtp));
                                                    }
                                                }
                                                else
                                                {
                                                    logger.LogWarning("Non-numeric audio/video media format attribute in SDP: {sdpLine}", sdpLineTrimmedSpan.ToString());
                                                }
                                            }
                                            else
                                            {
                                                activeAnnouncement.AddExtra(sdpLineTrimmedSpan.ToString());
                                            }
                                        }
                                        else
                                        {
                                            // Parse the rtpmap attribute for NON audio/video announcements.
                                            if (TrySplitAttributeValue(
                                                l,
                                                SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUTE_PREFIX.Length,
                                                out var formatIDStart,
                                                out var formatIDLength,
                                                out var rtpmapStart))
                                            {
                                                var formatID = l.Slice(formatIDStart, formatIDLength).ToString();
                                                var rtpmap = l.Slice(rtpmapStart).ToString();

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
                                                activeAnnouncement.AddExtra(sdpLineTrimmedSpan.ToString());
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("There was no active media announcement for a media format attribute, ignoring.");
                                    }
                                    break;

                                case var l when l.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        if (activeAnnouncement.Media == SDPMediaTypesEnum.audio || activeAnnouncement.Media == SDPMediaTypesEnum.video || activeAnnouncement.Media == SDPMediaTypesEnum.text)
                                        {
                                            // Parse the fmtp attribute for audio/video announcements.
                                            if (TrySplitAttributeValue(
                                                l,
                                                SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX.Length,
                                                out var avFormatIDStart,
                                                out var avFormatIDLength,
                                                out var fmtpStart))
                                            {
                                                var avFormatID = l.Slice(avFormatIDStart, avFormatIDLength);
                                                if (int.TryParse(avFormatID, out var fmtpFormatId))
                                                {
                                                    var fmtp = l.Slice(fmtpStart).ToString();
                                                    if (activeAnnouncement.MediaFormats.ContainsKey(fmtpFormatId))
                                                    {
                                                        activeAnnouncement.MediaFormats[fmtpFormatId] = activeAnnouncement.MediaFormats[fmtpFormatId].WithUpdatedFmtp(fmtp);
                                                    }
                                                    else
                                                    {
                                                        // Store the fmtp attribute for use when the rtpmap attribute turns up.
                                                        if (_pendingFmtp.ContainsKey(fmtpFormatId))
                                                        {
                                                            _pendingFmtp.Remove(fmtpFormatId);
                                                        }
                                                        _pendingFmtp.Add(fmtpFormatId, fmtp);
                                                    }
                                                }
                                                else
                                                {
                                                    logger.LogWarning("Invalid media format parameter attribute in SDP: {sdpLine}", sdpLineTrimmedSpan.ToString());
                                                }
                                            }
                                            else
                                            {
                                                activeAnnouncement.AddExtra(sdpLineTrimmedSpan.ToString());
                                            }
                                        }
                                        else
                                        {
                                            // Parse the fmtp attribute for NON audio/video announcements.
                                            if (TrySplitAttributeValue(
                                                l,
                                                SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX.Length,
                                                out var formatIDStart,
                                                out var formatIDLength,
                                                out var fmtpStart))
                                            {
                                                var formatID = l.Slice(formatIDStart, formatIDLength).ToString();
                                                var fmtp = l.Slice(fmtpStart).ToString();

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
                                                activeAnnouncement.AddExtra(sdpLineTrimmedSpan.ToString());
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("There was no active media announcement for a media format parameter attribute, ignoring.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPSecurityDescription.CRYPTO_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    //2018-12-21 rj2: add a=crypto
                                    if (activeAnnouncement != null)
                                    {
                                        try
                                        {
                                            activeAnnouncement.AddCryptoLine(sdpLineTrimmedSpan.ToString());
                                        }
                                        catch (FormatException fex)
                                        {
                                            logger.LogWarning("Error Parsing SDP-Line(a=crypto) {Exception}", fex);
                                        }
                                    }
                                    break;

                                case var _ when StartsWithAttribute(sdpLineTrimmedSpan, MEDIA_ID_ATTRIBUTE_PREFIX):
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.MediaID = SliceAfterColon(sdpLineTrimmedSpan).ToString();
                                    }
                                    else
                                    {
                                        logger.LogWarning("A media ID can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var fields = SliceAfterColon(sdpLineTrimmedSpan);
                                        var fieldIndex = 0;

                                        // Set the ID.
                                        foreach (var fieldRange in fields.Split(' '))
                                        {
                                            var ssrcField = fields[fieldRange];
                                            if (fieldIndex == 0)
                                            {
                                                activeAnnouncement.SsrcGroupID = ssrcField.ToString();
                                            }
                                            else if (uint.TryParse(ssrcField, out var ssrc))
                                            {
                                                // Add attributes for each of the SSRC values.
                                                activeAnnouncement.SsrcAttributes.Add(new SDPSsrcAttribute(ssrc, null, activeAnnouncement.SsrcGroupID));
                                            }

                                            fieldIndex++;
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("A ssrc-group ID can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var ssrcFields = SliceAfterColon(sdpLineTrimmedSpan);
                                        var ssrcField = default(ReadOnlySpan<char>);
                                        var cnameField = default(ReadOnlySpan<char>);
                                        var fieldIndex = 0;

                                        foreach (var fieldRange in ssrcFields.Split(' '))
                                        {
                                            if (fieldIndex == 0)
                                            {
                                                ssrcField = ssrcFields[fieldRange];
                                            }
                                            else if (fieldIndex == 1)
                                            {
                                                cnameField = ssrcFields[fieldRange];
                                                break;
                                            }

                                            fieldIndex++;
                                        }

                                        if (uint.TryParse(ssrcField, out var ssrc))
                                        {
                                            var ssrcAttribute = activeAnnouncement.SsrcAttributes.FirstOrDefault(x => x.SSRC == ssrc);
                                            if (ssrcAttribute == null)
                                            {
                                                ssrcAttribute = new SDPSsrcAttribute(ssrc, null, null);
                                                activeAnnouncement.SsrcAttributes.Add(ssrcAttribute);
                                            }

                                            if (!cnameField.IsEmpty &&
                                                cnameField.StartsWith(SDPSsrcAttribute.MEDIA_CNAME_ATTRIBUE_PREFIX, StringComparison.Ordinal))
                                            {
                                                ssrcAttribute.Cname = cnameField.Slice(cnameField.IndexOf(':') + 1).ToString();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("An ssrc attribute can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when TryParseMediaStreamStatus(sdpLineTrimmedSpan, out var mediaStreamStatus):
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.MediaStreamStatus = mediaStreamStatus;
                                    }
                                    else
                                    {
                                        sdp.SessionMediaStreamStatus = mediaStreamStatus;
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var sctpMapFields = SliceAfterColon(sdpLineTrimmedSpan);
                                        activeAnnouncement.SctpMap = sctpMapFields.ToString();

                                        var sctpPortField = default(ReadOnlySpan<char>);
                                        var maxMessageSizeField = default(ReadOnlySpan<char>);
                                        var fieldIndex = 0;

                                        foreach (var fieldRange in sctpMapFields.Split(' '))
                                        {
                                            if (fieldIndex == 0)
                                            {
                                                sctpPortField = sctpMapFields[fieldRange];
                                            }
                                            else if (fieldIndex == 2)
                                            {
                                                maxMessageSizeField = sctpMapFields[fieldRange];
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
                                            logger.LogWarning("An sctp-port value of {sctpPortStr} was not recognised as a valid port.", sctpPortField.ToString());
                                        }

                                        if (!long.TryParse(maxMessageSizeField, out activeAnnouncement.MaxMessageSize))
                                        {
                                            logger.LogWarning("A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.", maxMessageSizeField.ToString());
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("An sctpmap attribute can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var sctpPortStr = SliceAfterColon(sdpLineTrimmedSpan);

                                        if (ushort.TryParse(sctpPortStr, out var sctpPort))
                                        {
                                            activeAnnouncement.SctpPort = sctpPort;
                                        }
                                        else
                                        {
                                            logger.LogWarning("An sctp-port value of {sctpPortStr} was not recognised as a valid port.", sctpPortStr.ToString());
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("An sctp-port attribute can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var maxMessageSizeStr = SliceAfterColon(sdpLineTrimmedSpan);
                                        if (!long.TryParse(maxMessageSizeStr, out activeAnnouncement.MaxMessageSize))
                                        {
                                            logger.LogWarning("A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.", maxMessageSizeStr.ToString());
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("A max-message-size attribute can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var acceptTypes = SliceAfterColon(sdpLineTrimmedSpan).Trim();
                                        var acceptTypesList = new List<string>();
                                        foreach (var acceptTypeRange in acceptTypes.Split(' '))
                                        {
                                            acceptTypesList.Add(acceptTypes[acceptTypeRange].ToString());
                                        }
                                        activeAnnouncement.MessageMediaFormat.AcceptTypes = acceptTypesList;
                                    }
                                    else
                                    {
                                        logger.LogWarning("A accept-types attribute can only be set on a media announcement.");
                                    }
                                    break;

                                case var _ when sdpLineTrimmedSpan.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_PREFIX, StringComparison.Ordinal):
                                    if (activeAnnouncement != null)
                                    {
                                        var pathStr = SliceAfterColon(sdpLineTrimmedSpan);
                                        var pathTrimmedStr = pathStr.Slice(pathStr.IndexOf(':') + 3);
                                        activeAnnouncement.MessageMediaFormat.IP = pathTrimmedStr.Slice(0, pathTrimmedStr.IndexOf(':')).ToString();

                                        pathTrimmedStr = pathTrimmedStr.Slice(pathTrimmedStr.IndexOf(':') + 1);
                                        activeAnnouncement.MessageMediaFormat.Port = pathTrimmedStr.Slice(0, pathTrimmedStr.IndexOf('/')).ToString();

                                        pathTrimmedStr = pathTrimmedStr.Slice(pathTrimmedStr.IndexOf('/') + 1);
                                        activeAnnouncement.MessageMediaFormat.Endpoint = pathTrimmedStr.ToString();

                                    }
                                    else
                                    {
                                        logger.LogWarning("A path attribute can only be set on a media announcement.");
                                    }
                                    break;

                                default:
                                    if (activeAnnouncement != null)
                                    {
                                        activeAnnouncement.AddExtra(sdpLineTrimmedSpan.ToString());
                                    }
                                    else
                                    {
                                        sdp.AddExtra(sdpLineTrimmedSpan.ToString());
                                    }
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<Range>.Shared.Return(sdpLineRangeBuffer);
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
                logger.LogError(excp, "Exception ParseSDPDescription. {ErrorMessage}", excp.Message);
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
            var sdp = new StringBuilder();
            sdp.Append("v=").Append(SDP_PROTOCOL_VERSION).Append(CRLF)
                .Append("o=").Append(Owner).Append(CRLF)
                .Append("s=").Append(SessionName).Append(CRLF);

            if (Connection != null)
            {
                sdp.Append(Connection);
            }

            foreach (string bandwidth in BandwidthAttributes)
            {
                sdp.Append("b=").Append(bandwidth).Append(CRLF);
            }

            sdp.Append("t=").Append(Timing).Append(CRLF);

            if (!string.IsNullOrWhiteSpace(IceUfrag))
            {
                sdp.Append("a=").Append(ICE_UFRAG_ATTRIBUTE_PREFIX).Append(':').Append(IceUfrag).Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IcePwd))
            {
                sdp.Append("a=").Append(ICE_PWD_ATTRIBUTE_PREFIX).Append(':').Append(IcePwd).Append(CRLF);
            }

            if (IceRole != null)
            {
                sdp.Append("a=").Append(SDP.ICE_SETUP_ATTRIBUTE_PREFIX).Append(':').Append(IceRole).Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(DtlsFingerprint))
            {
                sdp.Append("a=").Append(DTLS_FINGERPRINT_ATTRIBUTE_PREFIX).Append(':').Append(DtlsFingerprint).Append(CRLF);
            }

            if (IceCandidates?.Count > 0)
            {
                foreach (var candidate in IceCandidates)
                {
                    sdp.Append("a=").Append(SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX).Append(':').Append(candidate).Append(CRLF);
                }
            }

            if (!string.IsNullOrWhiteSpace(SessionDescription))
            {
                sdp.Append("i=").Append(SessionDescription).Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(URI))
            {
                sdp.Append("u=").Append(URI).Append(CRLF);
            }

            if (OriginatorEmailAddresses != null && OriginatorEmailAddresses.Length > 0)
            {
                foreach (string originatorAddress in OriginatorEmailAddresses)
                {
                    if (!string.IsNullOrWhiteSpace(originatorAddress))
                    {
                        sdp.Append("e=").Append(originatorAddress).Append(CRLF);
                    }
                }
            }

            if (OriginatorPhoneNumbers != null && OriginatorPhoneNumbers.Length > 0)
            {
                foreach (string originatorNumber in OriginatorPhoneNumbers)
                {
                    if (!string.IsNullOrWhiteSpace(originatorNumber))
                    {
                        sdp.Append("p=").Append(originatorNumber).Append(CRLF);
                    }
                }
            }

            if (Group != null)
            {
                sdp.Append("a=").Append(GROUP_ATRIBUTE_PREFIX).Append(':').Append(Group).Append(CRLF);
            }

            foreach (string extra in ExtraSessionAttributes)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    sdp.Append(extra).Append(CRLF);
                }
            }

            if (SessionMediaStreamStatus != null)
            {
                sdp.Append(MediaStreamStatusType.GetAttributeForMediaStreamStatus(SessionMediaStreamStatus.Value)).Append(CRLF);
            }

            //foreach (SDPMediaAnnouncement media in Media.OrderBy(x => x.MLineIndex).ThenBy(x => x.MediaID))
            foreach (SDPMediaAnnouncement media in Media.OrderBy(x => x.MLineIndex).ThenBy(x => x.MediaID))
            {
                if (media != null)
                {
                    sdp.Append(media);
                }
            }

            return sdp.ToString();
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
