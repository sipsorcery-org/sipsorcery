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
// announcement, session inviatation, and other forms of multimedia session
// initiation." 
//
// SDP Includes:
// - Session name and Purpose,
// - Time(s) the session is active,
// - The media comprising the session,
// - Information to receive those media (addresses, ports, formats etc.)
// As resources to participate in the session may be limited, some additional information
// may also be deisreable:
// - Information about the bandwidth to be used,
// - Contatc information for the person responsible for the conference.
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
        public const string ICE_UFRAG_ATTRIBUTE_PREFIX = "ice-ufrag";
        public const string ICE_PWD_ATTRIBUTE_PREFIX = "ice-pwd";
        public const string ICE_CANDIDATE_ATTRIBUTE_PREFIX = "candidate";
        public const string ADDRESS_TYPE_IPV4 = "IP4";
        public const string ADDRESS_TYPE_IPV6 = "IP6";

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

        public string SessionName = "-";            // Common name of the session.
        public string Timing;
        public List<string> BandwidthAttributes = new List<string>();

        // Optional fields.
        public string SessionDescription;
        public string URI;                          // URI for additional information about the session.
        public string[] OriginatorEmailAddresses;   // Email addresses for the person responsible for the session.
        public string[] OriginatorPhoneNumbers;     // Phone numbers for the person responsible for the session.
        public string IceUfrag;                     // If ICE is being used the username for the STUN requests.
        public string IcePwd;                       // If ICE is being used the password for the STUN requests.
        public List<IceCandidate> IceCandidates;

        public SDPConnectionInformation Connection;

        // Media.
        public List<SDPMediaAnnouncement> Media = new List<SDPMediaAnnouncement>();

        /// <summary>
        /// The stream status of this session. Note that None means no explicit value has been set
        /// and the default is sendrecv. Also if child media announcements have an explicit status set then 
        /// it takes precedence.
        /// </summary>
        public MediaStreamStatusEnum SessionMediaStreamStatus { get; set; } = MediaStreamStatusEnum.None;

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
                    SDPMediaAnnouncement activeAnnouncement = null;

                    string[] sdpLines = Regex.Split(sdpDescription, CRLF);

                    foreach (string sdpLine in sdpLines)
                    {
                        string sdpLineTrimmed = sdpLine.Trim();
                        if (sdpLineTrimmed.StartsWith("v="))
                        {
                            if (!Decimal.TryParse(sdpLineTrimmed.Substring(2), out sdp.Version))
                            {
                                logger.LogWarning("The Version value in an SDP description could not be parsed as a decimal: " + sdpLine + ".");
                            }
                        }
                        else if (sdpLineTrimmed.StartsWith("o="))
                        {
                            string[] ownerFields = sdpLineTrimmed.Substring(2).Split(' ');
                            sdp.Username = ownerFields[0];
                            sdp.SessionId = ownerFields[1];
                            Int32.TryParse(ownerFields[2], out sdp.AnnouncementVersion);
                            sdp.NetworkType = ownerFields[3];
                            sdp.AddressType = ownerFields[4];
                            sdp.AddressOrHost = ownerFields[5];
                        }
                        else if (sdpLineTrimmed.StartsWith("s="))
                        {
                            sdp.SessionName = sdpLineTrimmed.Substring(2);
                        }
                        else if (sdpLineTrimmed.StartsWith("c="))
                        {
                            if (sdp.Connection == null)
                            {
                                sdp.Connection = SDPConnectionInformation.ParseConnectionInformation(sdpLineTrimmed);
                            }

                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.Connection = SDPConnectionInformation.ParseConnectionInformation(sdpLineTrimmed);
                            }
                        }
                        else if (sdpLineTrimmed.StartsWith("b="))
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.BandwidthAttributes.Add(sdpLineTrimmed.Substring(2));
                            }
                            else
                            {
                                sdp.BandwidthAttributes.Add(sdpLineTrimmed.Substring(2));
                            }
                        }
                        else if (sdpLineTrimmed.StartsWith("t="))
                        {
                            sdp.Timing = sdpLineTrimmed.Substring(2);
                        }
                        else if (sdpLineTrimmed.StartsWith("m="))
                        {
                            Match mediaMatch = Regex.Match(sdpLineTrimmed.Substring(2), @"(?<type>\w+)\s+(?<port>\d+)\s+(?<transport>\S+)(\s*)(?<formats>.*)$");
                            if (mediaMatch.Success)
                            {
                                SDPMediaAnnouncement announcement = new SDPMediaAnnouncement();
                                announcement.Media = SDPMediaTypes.GetSDPMediaType(mediaMatch.Result("${type}"));
                                Int32.TryParse(mediaMatch.Result("${port}"), out announcement.Port);
                                announcement.Transport = mediaMatch.Result("${transport}");
                                announcement.ParseMediaFormats(mediaMatch.Result("${formats}"));
                                sdp.Media.Add(announcement);

                                activeAnnouncement = announcement;
                            }
                            else
                            {
                                logger.LogWarning("A media line in SDP was invalid: " + sdpLineTrimmed.Substring(2) + ".");
                            }
                        }
                        else if (sdpLineTrimmed.StartsWith("a=" + ICE_UFRAG_ATTRIBUTE_PREFIX))
                        {
                            sdp.IceUfrag = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                        }
                        else if (sdpLineTrimmed.StartsWith("a=" + ICE_PWD_ATTRIBUTE_PREFIX))
                        {
                            sdp.IcePwd = sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1);
                        }
                        else if (sdpLineTrimmed.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX))
                        {
                            if (activeAnnouncement != null)
                            {
                                Match formatAttributeMatch = Regex.Match(sdpLineTrimmed, SDPMediaAnnouncement.MEDIA_FORMAT_ATTRIBUE_PREFIX + @"(?<id>\d+)\s+(?<attribute>.*)$");
                                if (formatAttributeMatch.Success)
                                {
                                    int formatID;
                                    if (Int32.TryParse(formatAttributeMatch.Result("${id}"), out formatID))
                                    {
                                        activeAnnouncement.AddFormatAttribute(formatID, formatAttributeMatch.Result("${attribute}"));
                                    }
                                    else
                                    {
                                        logger.LogWarning("Invalid media format attribute in SDP: " + sdpLine);
                                    }
                                }
                                else
                                {
                                    activeAnnouncement.AddExtra(sdpLineTrimmed);
                                }
                            }
                            else
                            {
                                logger.LogWarning("There was no active media announcement for a media format attribute, ignoring.");
                            }
                        }
                        else if (sdpLineTrimmed.StartsWith(SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX))
                        {
                            if (activeAnnouncement != null)
                            {
                                Match formatAttributeMatch = Regex.Match(sdpLineTrimmed, SDPMediaAnnouncement.MEDIA_FORMAT_PARAMETERS_ATTRIBUE_PREFIX + @"(?<id>\d+)\s+(?<attribute>.*)$");
                                if (formatAttributeMatch.Success)
                                {
                                    int formatID;
                                    if (Int32.TryParse(formatAttributeMatch.Result("${id}"), out formatID))
                                    {
                                        activeAnnouncement.AddFormatParameterAttribute(formatID, formatAttributeMatch.Result("${attribute}"));
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
                                logger.LogWarning("There was no active media announcement for a media format parameter attribute, ignoring.");
                            }
                        }
                        else if (sdpLineTrimmed.StartsWith("a=" + ICE_CANDIDATE_ATTRIBUTE_PREFIX))
                        {
                            if (sdp.IceCandidates == null)
                            {
                                sdp.IceCandidates = new List<IceCandidate>();
                            }

                            sdp.IceCandidates.Add(IceCandidate.Parse(sdpLineTrimmed.Substring(sdpLineTrimmed.IndexOf(':') + 1)));
                        }
                        //2018-12-21 rj2: add a=crypto
                        else if (sdpLineTrimmed.StartsWith(SDPSecurityDescription.CRYPTO_ATTRIBUE_PREFIX))
                        {
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
                        }
                        else if (MediaStreamStatusType.IsMediaStreamStatusAttribute(sdpLine.Trim(), out var mediaStreamStatus))
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
                        else
                        {
                            if (activeAnnouncement != null)
                            {
                                activeAnnouncement.AddExtra(sdpLineTrimmed);
                            }
                            else
                            {
                                sdp.AddExtra(sdpLineTrimmed);
                            }
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
                throw excp;
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

            foreach (string extra in ExtraSessionAttributes)
            {
                sdp += string.IsNullOrWhiteSpace(extra) ? null : extra + CRLF;
            }

            if (SessionMediaStreamStatus != MediaStreamStatusEnum.None)
            {
                sdp += MediaStreamStatusType.GetAttributeForMediaStreamStatus(SessionMediaStreamStatus) + CRLF;
            }

            foreach (SDPMediaAnnouncement media in Media)
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
        /// Gets the media stream staus for the specified media announcement.
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
                return MediaStreamStatusEnum.None;
            }
            else
            {
                var announcement = announcements[announcementIndex];

                if (announcement.MediaStreamStatus != MediaStreamStatusEnum.None)
                {
                    return announcement.MediaStreamStatus;
                }
                else if (SessionMediaStreamStatus != MediaStreamStatusEnum.None)
                {
                    return SessionMediaStreamStatus;
                }
                else
                {
                    return MediaStreamStatusEnum.SendRecv;
                }
            }
        }
    }
}
