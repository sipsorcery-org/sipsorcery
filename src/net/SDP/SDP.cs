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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.sys;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public partial class SDP
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

        private static ILogger logger = Log.Logger;

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

        public static SDP? ParseSDPDescription(string sdpDescription)
        {
            if (string.IsNullOrWhiteSpace(sdpDescription))
            {
                return null;
            }

            try
            {
                var sdp = new SDP();
                sdp.m_rawSdp = sdpDescription;

                var mLineIndex = 0;
                SDPMediaAnnouncement? activeAnnouncement = null;

                // If a media announcement fmtp atribute is found before the rtpmap it will be stored
                // in this dictionary. A dynamic media format type cannot be created without an rtpmap.
                var pendingFmtp = new Dictionary<int, string>();

                var span = sdpDescription.AsSpan();

                while (!span.IsEmpty)
                {
                    var line = ReadNextLine(ref span);

                    if (line.Length < 2 || line[1] != '=')
                    {
                        continue;
                    }

                    var type = line[0];
                    var value = line.Slice(2);

                    switch (type)
                    {
                        case 'v':
                            if (!TypeExtensions.TryParseDecimal(value, out sdp.Version))
                            {
                                logger.LogWarning("The Version value in an SDP description could not be parsed as a decimal: {sdpLine}.", line.ToString());
                            }
                            break;

                        case 'o':
                            ParseOrigin(value, sdp);
                            break;

                        case 's':
                            sdp.SessionName = value.ToString();
                            break;

                        case 'i':
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.MediaDescription = value.ToString();
                            }
                            else
                            {
                                sdp.SessionDescription = value.ToString();
                            }

                            break;

                        case 'c':
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.Connection = SDPConnectionInformation.ParseConnectionInformation(line);
                            }
                            else if (sdp.Connection == null)
                            {
                                sdp.Connection = SDPConnectionInformation.ParseConnectionInformation(line);
                            }
                            else
                            {
                                logger.LogWarning("The SDP message had a duplicate connection attribute which was ignored.");
                            }

                            break;

                        case 'b':
                            ParseBandwidth(value, sdp, activeAnnouncement);
                            break;

                        case 't':
                            sdp.Timing = value.ToString();
                            break;

                        case 'm':
                            ParseMedia(line, sdp, ref activeAnnouncement, ref mLineIndex);
                            break;

                        case 'a':
                            ParseAttribute(line, sdp, activeAnnouncement, pendingFmtp);
                            break;

                        default:
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.AddExtra(line.ToString());
                            }
                            else
                            {
                                sdp.AddExtra(line.ToString());
                            }
                            break;
                    }

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    static ReadOnlySpan<char> ReadNextLine(ref ReadOnlySpan<char> span)
                    {
                        var lineEnd = span.IndexOfAny('\r', '\n');
                        ReadOnlySpan<char> line;

                        if (lineEnd == -1)
                        {
                            line = span.Trim();
                            span = ReadOnlySpan<char>.Empty;
                        }
                        else
                        {
                            line = span.Slice(0, lineEnd).Trim();
                            var next = lineEnd + 1;
                            if (span.Length > next && span[lineEnd] == '\r' && span[lineEnd + 1] == '\n')
                            {
                                next++;
                            }
                            span = span.Slice(next);
                        }

                        return line;
                    }

                    static void ParseOrigin(ReadOnlySpan<char> value, SDP sdp)
                    {
                        Span<Range> fields = stackalloc Range[6];
                        var count = value.Split(fields, ' ', StringSplitOptions.RemoveEmptyEntries);

                        if (count >= 5)
                        {
                            sdp.Username = value[fields[0]].ToString();
                            sdp.SessionId = value[fields[1]].ToString();
                            sdp.AnnouncementVersion = TypeExtensions.TryParseUInt64(value[fields[2]], out var version) ? version : 0;
                            sdp.NetworkType = value[fields[3]].ToString();
                            sdp.AddressType = value[fields[4]].ToString();
                            sdp.AddressOrHost = count > 5 ? value[fields[5]].ToString() : null;
                        }
                        else
                        {
                            logger.LogWarning("The SDP message had an invalid SDP line format for 'o=': {sdpLineTrimmed}", value.ToString());
                        }
                    }

                    static void ParseBandwidth(ReadOnlySpan<char> value, SDP sdp, SDPMediaAnnouncement? activeAnnouncement)
                    {
                        if (activeAnnouncement != null)
                        {
                            var colonIndex = value.IndexOf(':');
                            var key = colonIndex != -1 ? value.Slice(0, colonIndex) : value;
                            var attrValue = colonIndex != -1 && colonIndex + 1 < value.Length
                                ? value.Slice(colonIndex + 1)
                                : ReadOnlySpan<char>.Empty;
                            if (key.SequenceEqual(SDPMediaAnnouncement.TIAS_BANDWIDTH_ATTRIBUE_NAME.AsSpan()))
                            {
                                if (TypeExtensions.TryParseUInt32(attrValue, out var tias))
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

                    static void ParseMedia(ReadOnlySpan<char> line, SDP sdp, ref SDPMediaAnnouncement activeAnnouncement, ref int mLineIndex)
                    {
                        if (TryParseMediaDescription(line.Slice(2), out var type, out var port, out var portCount, out var transport, out var formats))
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
                                announcement.MediaStreamStatus = sdp.SessionMediaStreamStatus != null ? sdp.SessionMediaStreamStatus.Value :
                                    MediaStreamStatusEnum.SendRecv;
                            }
                            sdp.Media.Add(announcement);

                            activeAnnouncement = announcement;
                        }
                        else
                        {
                            logger.LogWarning("A media line in SDP was invalid: {sdpLine}.", line.Slice(2).ToString());
                        }

                        mLineIndex++;

                        /// <summary>(?&lt;type&gt;\w+)\s+(?&lt;port&gt;\d+)(?:\/(?&lt;portCount&gt;\d+))?\s+(?&lt;transport&gt;\S+)\s*(?&lt;formats&gt;.*)</summary>
                        static bool TryParseMediaDescription(
                            ReadOnlySpan<char> input,
                            out string type,
                            out int port,
                            out int? portCount,
                            out string transport,
                            out string formats)
                        {
                            type = default;
                            port = default;
                            portCount = default;
                            transport = default;
                            formats = default;

                            // Parse type
                            var typeEnd = input.IndexOfAny(SearchValuesExtensions.WhitespaceChars);
                            if (typeEnd <= 0)
                            {
                                return false;
                            }

                            type = input[..typeEnd].ToString();

                            // Skip whitespace after type
                            var i = typeEnd + input[typeEnd..].IndexOfAnyExcept(SearchValuesExtensions.WhitespaceChars);
                            if (i >= input.Length)
                            {
                                return false;
                            }

                            // Parse port
                            var portStart = i;
                            var portEnd = input[portStart..].IndexOfAnyExcept(SearchValuesExtensions.DigitChars);
                            if (portEnd <= 0)
                            {
                                return false;
                            }

                            portEnd += portStart;
                            if (!TypeExtensions.TryParseInt32(input[portStart..portEnd], out port))
                            {
                                return false;
                            }

                            i = portEnd;

                            // Optional: /<portCount>
                            if (i < input.Length && input[i] == '/')
                            {
                                i++;
                                var portCountStart = i;
                                var portCountEnd = input[portCountStart..].IndexOfAnyExcept(SearchValuesExtensions.DigitChars);
                                if (portCountEnd <= 0)
                                {
                                    return false;
                                }

                                portCountEnd += portCountStart;
                                if (!TypeExtensions.TryParseInt32(input[portCountStart..portCountEnd], out var parsedPortCount))
                                {
                                    return false;
                                }

                                portCount = parsedPortCount;
                                i = portCountEnd;
                            }

                            // Skip whitespace before transport
                            var transportStartOffset = input[i..].IndexOfAnyExcept(SearchValuesExtensions.WhitespaceChars);
                            if (transportStartOffset == -1)
                            {
                                return false;
                            }

                            i += transportStartOffset;

                            // Parse transport
                            var transportEndOffset = input[i..].IndexOfAny(SearchValuesExtensions.WhitespaceChars);
                            var transportEnd = transportEndOffset == -1 ? input.Length : i + transportEndOffset;
                            transport = input[i..transportEnd].ToString();

                            i = transportEnd;

                            // Skip whitespace before formats
                            var formatsStartOffset = input[i..].IndexOfAnyExcept(SearchValuesExtensions.WhitespaceChars);
                            i = formatsStartOffset == -1 ? input.Length : i + formatsStartOffset;

                            formats = input[i..].ToString();
                            return true;
                        }
                    }

                    static void ParseAttribute(
                            ReadOnlySpan<char> line,
                            SDP sdp,
                            SDPMediaAnnouncement activeAnnouncement,
                            Dictionary<int, string> pendingFmtp)
                    {
                        var value = line.Slice(2);
                        var colonIndex = value.IndexOf(':');
                        var key = colonIndex != -1 ? value.Slice(0, colonIndex) : value;
                        var attrValue = colonIndex != -1 && colonIndex + 1 < value.Length
                            ? value.Slice(colonIndex + 1)
                            : ReadOnlySpan<char>.Empty;

                        if (key.SequenceEqual(GROUP_ATRIBUTE_PREFIX.AsSpan()))
                        {
                            sdp.Group = attrValue.ToString();
                        }
                        else if (key.SequenceEqual(ICE_LITE_IMPLEMENTATION_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            sdp.IceImplementation = IceImplementationEnum.lite;
                        }
                        else if (key.SequenceEqual(ICE_UFRAG_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.IceUfrag = attrValue.ToString();
                            }
                            else
                            {
                                sdp.IceUfrag = attrValue.ToString();
                            }
                        }
                        else if (key.SequenceEqual(ICE_PWD_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.IcePwd = attrValue.ToString();
                            }
                            else
                            {
                                sdp.IcePwd = attrValue.ToString();
                            }
                        }
                        else if (key.SequenceEqual(ICE_SETUP_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            if (!attrValue.IsEmpty)
                            {
                                if (IceRolesEnumExtensions.TryParse(attrValue, out var iceRole, true))
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
                                    logger.LogWarning("ICE role was not recognised from SDP attribute: {sdpLineTrimmed}.", line.ToString());
                                }
                            }
                            else
                            {
                                logger.LogWarning("ICE role SDP attribute was missing the mandatory colon: {sdpLineTrimmed}.", line.ToString());
                            }
                        }
                        else if (key.SequenceEqual(DTLS_FINGERPRINT_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.DtlsFingerprint = attrValue.ToString();
                            }
                            else
                            {
                                sdp.DtlsFingerprint = attrValue.ToString();
                            }
                        }
                        else if (key.SequenceEqual(ICE_CANDIDATE_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.IceCandidates ??= new List<string>();
                                activeAnnouncement.IceCandidates.Add(attrValue.ToString());
                            }
                            else
                            {
                                sdp.IceCandidates ??= new List<string>();
                                sdp.IceCandidates.Add(attrValue.ToString());
                            }
                        }
                        else if (key.SequenceEqual(END_ICE_CANDIDATES_ATTRIBUTE.AsSpan()))
                        {
                            // TODO: Set a flag.
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video)
                                {
                                    if (TryParseNumericIdAndUrl(attrValue, out var extensionId, out var uri))
                                    {
                                        var rtpExtension = RTPHeaderExtension.GetRTPHeaderExtension(extensionId, uri, activeAnnouncement.Media);
                                        if ((rtpExtension != null) && !activeAnnouncement.HeaderExtensions.ContainsKey(extensionId))
                                        {
                                            activeAnnouncement.HeaderExtensions.Add(extensionId, rtpExtension);
                                        }
                                    }
                                    else
                                    {
                                        logger.LogWarning("Invalid header extension in " + SDPMediaAnnouncement.MEDIA_EXTENSION_MAP_ATTRIBUE_PREFIX);
                                    }
                                }
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUTE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                                {
                                    // Parse the rtpmap attribute for audio/video announcements.    
                                    if (TryParseNumericIdAndStringAttribute(attrValue, out var formatId, out var rtpmap))
                                    {
                                        if (activeAnnouncement.MediaFormats.TryGetValue(formatId, out var mediaFormat))
                                        {
                                            activeAnnouncement.MediaFormats[formatId] = mediaFormat.WithUpdatedRtpmap(rtpmap);
                                        }
                                        else
                                        {
                                            _ = pendingFmtp.TryGetValue(formatId, out var fmtp);
                                            activeAnnouncement.MediaFormats.Add(formatId, new SDPAudioVideoMediaFormat(activeAnnouncement.Media, formatId, rtpmap, fmtp));
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
                                logger.LogWarning("There was no active media announcement for a media format attribute, ignoring.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                if (activeAnnouncement.Media is SDPMediaTypesEnum.audio or SDPMediaTypesEnum.video or SDPMediaTypesEnum.text)
                                {
                                    // Parse the fmtp attribute for audio/video announcements.
                                    if (TryParseNumericIdAndStringAttribute(attrValue, out var avFormatID, out var fmtp))
                                    {
                                        if (activeAnnouncement.MediaFormats.TryGetValue(avFormatID, out var mediaFormat))
                                        {
                                            activeAnnouncement.MediaFormats[avFormatID] = mediaFormat.WithUpdatedFmtp(fmtp.ToString());
                                        }
                                        else
                                        {
                                            // Store the fmtp attribute for use when the rtpmap attribute turns up.
                                            pendingFmtp.Remove(avFormatID);
                                            pendingFmtp.Add(avFormatID, fmtp.ToString());
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
                                logger.LogWarning("There was no active media announcement for a media format parameter attribute, ignoring.");
                            }
                        }
                        else if (key.SequenceEqual(SDPSecurityDescription.CRYPTO_ATTRIBUTE_NAME.AsSpan()))
                        {
                            //2018-12-21 rj2: add a=crypto
                            if (activeAnnouncement != null)
                            {
                                try
                                {
                                    activeAnnouncement.AddCryptoLine(line.ToString());
                                }
                                catch (FormatException fex)
                                {
                                    logger.LogWarning("Error Parsing SDP-Line(a=crypto) {Exception}", fex);
                                }
                            }
                        }
                        else if (key.SequenceEqual(MEDIA_ID_ATTRIBUTE_PREFIX.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.MediaID = attrValue.ToString();
                            }
                            else
                            {
                                logger.LogWarning("A media ID can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_GROUP_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
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
                                while (!span.IsEmpty)
                                {
                                    spaceIndex = span.IndexOf(' ');
                                    ReadOnlySpan<char> token;

                                    if (spaceIndex != -1)
                                    {
                                        token = span.Slice(0, spaceIndex);
                                        span = span.Slice(spaceIndex + 1);
                                    }
                                    else
                                    {
                                        token = span;
                                        span = ReadOnlySpan<char>.Empty;
                                    }

                                    if (TypeExtensions.TryParseUInt32(token, out var ssrc))
                                    {
                                        activeAnnouncement.SsrcAttributes.Add(new SDPSsrcAttribute(ssrc, null, activeAnnouncement.SsrcGroupID));
                                    }
                                }
                            }
                            else
                            {
                                logger.LogWarning("A ssrc-group ID can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_SSRC_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                var firstSpace = attrValue.IndexOf(' ');
                                if (firstSpace == -1)
                                {
                                    return;
                                }

                                var firstField = attrValue[..firstSpace];
                                if (TypeExtensions.TryParseUInt32(firstField, out var ssrc))
                                {
                                    if (activeAnnouncement.SsrcAttributes.FirstOrDefault(x => x.SSRC == ssrc) is not { } ssrcAttribute)
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
                                }
                            }
                            else
                            {
                                logger.LogWarning("An ssrc attribute can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_MAP_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.SctpMap = attrValue.ToString();

                                (var sctpPortStr, _, var maxMessageSizeStr) = activeAnnouncement.SctpMap.Split(' ');

                                if (ushort.TryParse(sctpPortStr, out var sctpPort))
                                {
                                    activeAnnouncement.SctpPort = sctpPort;
                                }
                                else
                                {
                                    logger.LogWarning("An sctp-port value of {sctpPortStr} was not recognised as a valid port.", sctpPortStr);
                                }

                                if (!long.TryParse(maxMessageSizeStr, out activeAnnouncement.MaxMessageSize))
                                {
                                    logger.LogWarning("A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.", maxMessageSizeStr);
                                }
                            }
                            else
                            {
                                logger.LogWarning("An sctpmap attribute can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_SCTP_PORT_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                if (TypeExtensions.TryParseUInt16(attrValue, out var sctpPort))
                                {
                                    activeAnnouncement.SctpPort = sctpPort;
                                }
                                else
                                {
                                    logger.LogWarning("An sctp-port value of {sctpPortStr} was not recognised as a valid port.", attrValue.ToString());
                                }
                            }
                            else
                            {
                                logger.LogWarning("An sctp-port attribute can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_MAX_MESSAGE_SIZE_ATTRIBUE_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                if (!TypeExtensions.TryParseInt64(attrValue, out activeAnnouncement.MaxMessageSize))
                                {
                                    logger.LogWarning("A max-message-size value of {maxMessageSizeStr} was not recognised as a valid long.", attrValue.ToString());
                                }
                            }
                            else
                            {
                                logger.LogWarning("A max-message-size attribute can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_PATH_ACCEPT_TYPES_NAME.AsSpan()))
                        {
                            if (activeAnnouncement != null)
                            {
                                var acceptTypesList = attrValue.Trim().SplitToList(' ');
                                activeAnnouncement.MessageMediaFormat.AcceptTypes = acceptTypesList;
                            }
                            else
                            {
                                logger.LogWarning("A accept-types attribute can only be set on a media announcement.");
                            }
                        }
                        else if (key.SequenceEqual(SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_NAME.AsSpan()))
                        {
                            const string mediaFormatPathMsrpSchemeAndDelimiter = SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_SCHEME + "://";
                            if (activeAnnouncement != null && attrValue.StartsWith(mediaFormatPathMsrpSchemeAndDelimiter.AsSpan()))
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
                                logger.LogWarning("A path attribute can only be set on a media announcement.");
                            }
                        }
                        else if (MediaStreamStatusType.IsMediaStreamStatusAttribute(line.ToString(), out var mediaStreamStatus))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.MediaStreamStatus = mediaStreamStatus;
                            }
                            else
                            {
                                sdp.SessionMediaStreamStatus = mediaStreamStatus;
                            }
                        }
                    }

                    /// <summary>^(?&lt;id&gt;\d+)\s+(?&lt;attribute&gt;.*)$</summary>
                    static bool TryParseNumericIdAndStringAttribute(ReadOnlySpan<char> input, out int id, out string attribute)
                    {
                        id = default;
                        attribute = default;

                        var digitEnd = input.IndexOfAnyExcept(SearchValuesExtensions.DigitChars);

                        if (digitEnd <= 0)
                        {
                            // No digits at start or input is all digits (no attribute)
                            return false;
                        }

                        _ = TypeExtensions.TryParseInt32(input[..digitEnd], out id); // not expected to fail

                        input = input[digitEnd..];
                        var nonWhitespaceIndex = input.IndexOfAnyExcept(SearchValuesExtensions.WhitespaceChars);

                        if (nonWhitespaceIndex < 0)
                        {
                            // No non white spaces after id
                            return false;
                        }

                        attribute = input[nonWhitespaceIndex..].ToString();
                        return true;
                    }

                    /// <summary>^(?&lt;id&gt;\S+)\s+(?&lt;attribute&gt;.*)$</summary>
                    static bool TryParseStringIdAndStringAttribute(ReadOnlySpan<char> input, out string id, out string attribute)
                    {
                        id = default;
                        attribute = default;

                        // Find the first whitespace (end of ID)
                        var idEnd = input.IndexOfAny(SearchValuesExtensions.WhitespaceChars);
                        if (idEnd <= 0)
                        {
                            // Either starts with whitespace or no whitespace at all
                            return false;
                        }

                        id = input[..idEnd].ToString();

                        // Skip all whitespace after the ID
                        var attrStart = input[idEnd..].IndexOfAnyExcept(SearchValuesExtensions.WhitespaceChars);
                        attribute = attrStart == -1
                            ? string.Empty
                            : input[(idEnd + attrStart)..].ToString();

                        return true;
                    }


                    /// <summary>^(?&lt;id&gt;\d+)\s+(?&lt;url&gt;.*)$</summary>
                    static bool TryParseNumericIdAndUrl(ReadOnlySpan<char> input, out int id, out string url)
                    {
                        id = default;
                        url = default;

                        // Find where the digits end
                        var digitEnd = input.IndexOfAnyExcept(SearchValuesExtensions.DigitChars);
                        if (digitEnd <= 0 || digitEnd >= input.Length)
                        {
                            return false;
                        }

                        // Expect exactly one space after the digits
                        if (input[digitEnd] != ' ')
                        {
                            return false;
                        }

                        _ = TypeExtensions.TryParseInt32(input[..digitEnd], out id); // not expected to fail

                        // The URL must be non-empty and contain no whitespace
                        var urlSpan = input[(digitEnd + 1)..];
                        if (urlSpan.IsEmpty || urlSpan.IndexOfAny(SearchValuesExtensions.WhitespaceChars) != -1)
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
            builder.Append("v=");
            builder.Append(SDP_PROTOCOL_VERSION);
            builder.Append(CRLF);

            builder.Append("o=");
            builder.Append(Owner);
            builder.Append(CRLF);

            builder.Append("s=");
            builder.Append(SessionName);
            builder.Append(CRLF);

            if (Connection != null)
            {
                Connection.ToString(ref builder);
            }

            foreach (string bandwidth in BandwidthAttributes)
            {
                builder.Append("b=");
                builder.Append(bandwidth);
                builder.Append(CRLF);
            }

            builder.Append("t=");
            builder.Append(Timing);
            builder.Append(CRLF);

            if (!string.IsNullOrWhiteSpace(IceUfrag))
            {
                builder.Append("a=");
                builder.Append(ICE_UFRAG_ATTRIBUTE_PREFIX);
                builder.Append(":");
                builder.Append(IceUfrag);
                builder.Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(IcePwd))
            {
                builder.Append("a=");
                builder.Append(ICE_PWD_ATTRIBUTE_PREFIX);
                builder.Append(":");
                builder.Append(IcePwd);
                builder.Append(CRLF);
            }

            if (IceRole != null)
            {
                builder.Append("a=");
                builder.Append(SDP.ICE_SETUP_ATTRIBUTE_PREFIX);
                builder.Append(":");
                builder.Append(IceRole.ToString());
                builder.Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(DtlsFingerprint))
            {
                builder.Append("a=");
                builder.Append(DTLS_FINGERPRINT_ATTRIBUTE_PREFIX);
                builder.Append(":");
                builder.Append(DtlsFingerprint);
                builder.Append(CRLF);
            }

            if (IceCandidates?.Count > 0)
            {
                foreach (var candidate in IceCandidates)
                {
                    builder.Append("a=");
                    builder.Append(SDP.ICE_CANDIDATE_ATTRIBUTE_PREFIX);
                    builder.Append(":");
                    builder.Append(candidate);
                    builder.Append(CRLF);
                }
            }

            if (!string.IsNullOrWhiteSpace(SessionDescription))
            {
                builder.Append("i=");
                builder.Append(SessionDescription);
                builder.Append(CRLF);
            }

            if (!string.IsNullOrWhiteSpace(URI))
            {
                builder.Append("u=");
                builder.Append(URI);
                builder.Append(CRLF);
            }

            if (OriginatorEmailAddresses != null)
            {
                foreach (string originatorAddress in OriginatorEmailAddresses)
                {
                    if (!string.IsNullOrWhiteSpace(originatorAddress))
                    {
                        builder.Append("e=");
                        builder.Append(originatorAddress);
                        builder.Append(CRLF);
                    }
                }
            }

            if (OriginatorPhoneNumbers != null)
            {
                foreach (string originatorNumber in OriginatorPhoneNumbers)
                {
                    if (!string.IsNullOrWhiteSpace(originatorNumber))
                    {
                        builder.Append("p=");
                        builder.Append(originatorNumber);
                        builder.Append(CRLF);
                    }
                }
            }

            if (Group != null)
            {
                builder.Append("a=");
                builder.Append(GROUP_ATRIBUTE_PREFIX);
                builder.Append(":");
                builder.Append(Group);
                builder.Append(CRLF);
            }

            foreach (string extra in ExtraSessionAttributes)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    builder.Append(extra);
                    builder.Append(CRLF);
                }
            }

            if (SessionMediaStreamStatus != null)
            {
                builder.Append(MediaStreamStatusType.GetAttributeForMediaStreamStatus(SessionMediaStreamStatus.Value));
                builder.Append(CRLF);
            }

            foreach (SDPMediaAnnouncement media in Media.OrderBy(x => x.MLineIndex).ThenBy(x => x.MediaID))
            {
                if (media != null)
                {
                    media.ToString(ref builder);
                }
            }
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
