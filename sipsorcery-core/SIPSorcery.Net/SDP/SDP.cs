//-----------------------------------------------------------------------------
// Filename: SDP.cs
//
// Description: Session Description Protocol implementation as defined in RFC 2327.
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
//     <media> <port> <transport> <fmt list>
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
// History:
// 20 Oct 2005	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
	public enum SDPMediaTypesEnum
	{
		audio = 1,
		video = 2,
		application = 3,
		data = 4,
		control = 5,
	}

    public class SDPMediaTypes
    {
        public static SDPMediaTypesEnum GetSDPMediaType(string mediaType)
        {
            return (SDPMediaTypesEnum)Enum.Parse(typeof(SDPMediaTypesEnum), mediaType, true);
        }
    }

	public enum SDPMediaFormatTypesEnum
	{
		PCMU = 0,
		GSM = 3,
		PCMA = 8,
		G723 = 96,
	}

	public class SDPMediaFormatAttributes
	{	
		private static Dictionary<string, string> m_formatAttributes = new Dictionary<string,string>();

		static SDPMediaFormatAttributes()
		{
			m_formatAttributes.Add("PCMU", "a=rtpmap:0 PCMU/8000");
			m_formatAttributes.Add("GSM", "a=rtpmap:3 GSM/8000");
			m_formatAttributes.Add("PCMA", "a=rtpmap:8 PCMA/8000");
			m_formatAttributes.Add("G723", "a=rtpmap:96 G723/8000");
		}

		public static string GetFormatAttribute(SDPMediaFormatTypesEnum mediaType)
		{
			//int mediaTypeInt = (int)mediaType;

			try
			{
				return m_formatAttributes[mediaType.ToString()] as string;
			}
			catch
			{
				return null;
			}
		}
	}

	public class MediaAnnouncement
	{
		// Connection Data fields (one for each media stream).
		public const string m_CRLF = "\r\n";
		public string ConnectionNetworkType = "IN";		// Type of network, IN = Internet.
		public string ConnectionAddressType = "IP4";	// Address type, typically IP4 or IP6.
		public string ConnectionAddress;				// IP or mulitcast address for the media connection.
		
		// Media Announcement fields.
		public SDPMediaTypesEnum Media = SDPMediaTypesEnum.audio;	// Media type for the stream.
		public int Port;						// For UDP transports should be in the range 1024 to 65535 and for RTP compliance should be even (only even ports used for data).
		public string Transport = "RTP/AVP";	// Defined types RTP/AVP (RTP Audio Visual Profile) and udp.
		public string FormatList;				// For AVP these will normally be a media payload type as defined in the RTP Audio/Video Profile.

        public List<SDPMediaFormatTypesEnum> MediaFormats = new List<SDPMediaFormatTypesEnum>();

        public MediaAnnouncement()
        { }

		public MediaAnnouncement(string address, int port)
		{
			ConnectionAddress = address;
			Port = port;
		}

        public override string ToString()
        {
            string announcement =
                "c=" + ConnectionNetworkType + " " + ConnectionAddressType + " " + ConnectionAddress + m_CRLF +
                "m=" + Media + " " + Port + " " + Transport + " " + FormatList + m_CRLF;
                
            /*GetFormatListAttributesToString();

            foreach (string attribute in MediaFormats)
            {
                announcement += (attribute == null) ? null : "a=" + attribute + m_CRLF;
            }*/

            return announcement;
        }

        public void AddMediaFormats(SDPMediaFormatTypesEnum[] mediaFormats)
        {
            foreach (SDPMediaFormatTypesEnum mediaFormat in mediaFormats)
            {
                MediaFormats.Add(mediaFormat);
            }
        }

		public string GetFormatListToString()
		{
			string formatString = null;

			if(MediaFormats != null)
			{
				foreach(SDPMediaFormatTypesEnum mediaFormat in MediaFormats)
				{
					int mediaFormatInt = (int)mediaFormat;
					formatString += " " + mediaFormatInt;
				}
			}

			return formatString;
		}

		public string GetFormatListAttributesToString()
		{
			string formatAttributes = null;
			
			if(MediaFormats != null)
			{
				foreach(SDPMediaFormatTypesEnum mediaFormat in MediaFormats)
				{
					string formatAttribute = SDPMediaFormatAttributes.GetFormatAttribute(mediaFormat);

					if(formatAttribute != null)
					{
						formatAttributes += formatAttribute + m_CRLF;
					}
				}
			}

			return formatAttributes;
		}

	}
	
	public class SDP
	{		
		public const string m_CRLF = "\r\n";
		public const string SDP_MIME_CONTENTTYPE = "application/sdp";
		public const int SDP_PROTOCOL_VERSION = 0;

        private static ILog logger = AppState.logger;

        public int Version = SDP_PROTOCOL_VERSION;

		// Owner fields.
		public string Username = "-";		// Username of the session originator.
		public string SessionId = "-";		// Unique Id for the session.
		public int AnnouncementVersion = 0;	// Version number for each announcement, number must be increased for each subsequent SDP modification.
		public string NetworkType = "IN";	// Type of network, IN = Internet.
		public string AddressType = "IP4";	// Address type, typically IP4 or IP6.
		public string Address;				// IP Address of the machine that created the session, either FQDN or dotted quad or textual for IPv6.
		public string Owner
		{
			get{ return Username + " " + SessionId + " " + AnnouncementVersion + " " + NetworkType + " " + AddressType + " " + Address; }
		}

		public string SessionName = "-";			// Common name of the session.
        public string Timing;

		// Optional fields.
		public string SessionDescription;
		public string URI;							// URI for additional information about the session.
		public string[] OriginatorEmailAddresses;	// Email addresses for the person responsible for the session.
		public string[] OriginatorPhoneNumbers;		// Phone numbers for the person responsible for the session.

		// Media.
        public List<MediaAnnouncement> Media = new List<MediaAnnouncement>();

        private List<string> ExtraAttributes = new List<string>();  // Attributes that were note recognised.
	
		public SDP()
		{}

		public SDP(string address)
		{
			Address = address;
		}

		public SDP(IPEndPoint clientEndPoint, string username, int netTestPktSize, int netTestFrameSize, int netTestNumChannels)
		{
			Address = clientEndPoint.Address.ToString();
			AddMedia(clientEndPoint.Address.ToString(), clientEndPoint.Port);
			Username = username;
				
			//SDPMediaFormatTypesEnum[] mediaFormats = new SDPMediaFormatTypesEnum[]{SDPMediaFormatTypesEnum.PCMU, SDPMediaFormatTypesEnum.GSM};
			//SDPMediaFormatTypesEnum[] mediaFormats = new SDPMediaFormatTypesEnum[]{SDPMediaFormatTypesEnum.PCMA};
			//AddMediaFormats(mediaFormats);

			string extraAttribute = "bfnettest: pktsize=" + netTestPktSize + ",framesize=" + netTestFrameSize + ",channels=" + netTestNumChannels;
            ExtraAttributes.Add(extraAttribute);
		}

		public static SDP ParseSDPDescription(string sdpDescription)
		{
            try
            {
                if (sdpDescription != null && sdpDescription.Trim().Length > 0)
                {
                    SDP sdp = new SDP();
                    MediaAnnouncement media = new MediaAnnouncement();

                    string[] sdpLines = Regex.Split(sdpDescription, m_CRLF);

                    foreach (string sdpLine in sdpLines)
                    {
                        if (sdpLine.Trim().StartsWith("v="))
                        {
                            if(!Int32.TryParse(sdpLine.Substring(2), out sdp.Version))
                            {
                                logger.Warn("The Version value in an SDP description could not be parsed as an Int32: " + sdpLine + ".");
                            }
                        }
                        else if (sdpLine.Trim().StartsWith("o="))
                        {
                            string[] ownerFields = sdpLine.Substring(2).Split(' ');
                            sdp.Username = ownerFields[0];
                            sdp.SessionId = ownerFields[1];
                            Int32.TryParse(ownerFields[2], out sdp.AnnouncementVersion);
                            sdp.NetworkType = ownerFields[3];
                            sdp.AddressType = ownerFields[4];
                            sdp.Address = ownerFields[5];
                        }
                        else if (sdpLine.Trim().StartsWith("s="))
                        {
                            sdp.SessionName = sdpLine.Substring(2);
                        }
                        else if (sdpLine.Trim().StartsWith("c="))
                        {
                            string[] connectionFields = sdpLine.Substring(2).Trim().Split(' ');
                            media.ConnectionNetworkType = connectionFields[0].Trim();
                            media.ConnectionAddressType = connectionFields[1].Trim();
                            media.ConnectionAddress = connectionFields[2].Trim();
                        }
                        else if (sdpLine.Trim().StartsWith("t="))
                        {
                            sdp.Timing = sdpLine.Substring(2);
                        }
                        else if (sdpLine.Trim().StartsWith("m="))
                        {
                            string[] mediaFields = sdpLine.Substring(2).Trim().Split(' ');
                            media.Media = SDPMediaTypes.GetSDPMediaType(mediaFields[0]);
                            Int32.TryParse(mediaFields[1], out media.Port);
                            media.Transport = mediaFields[2];
                            media.FormatList = mediaFields[3].Trim();
                        }
                        else
                        {
                            sdp.ExtraAttributes.Add(sdpLine);
                        }
                    }

                    sdp.Media.Add(media);
                    return sdp;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseSDPDescription. " + excp.Message);
                throw excp;
            }
		}

        public MediaAnnouncement AddMedia(string address, int port)
		{
			MediaAnnouncement announcement = new MediaAnnouncement(address, port);	
			Media.Add(announcement);

            return announcement;
		}

		public void AddExtra(string attribute)
		{
            ExtraAttributes.Add(attribute);
		}

        public override string ToString()
        {
            string sdp =
                "v=" + SDP_PROTOCOL_VERSION + m_CRLF +
                "o=" + Owner + m_CRLF +
                "s=" + SessionName + m_CRLF +
                "t=" + Timing + m_CRLF;

            sdp += (SessionDescription == null) ? null : "i=" + SessionDescription + m_CRLF;
            sdp += (URI == null) ? null : "u=" + URI + m_CRLF;

            if (OriginatorEmailAddresses != null && OriginatorEmailAddresses.Length > 0)
            {
                foreach (string originatorAddress in OriginatorEmailAddresses)
                {
                    sdp += (originatorAddress == null) ? null : "e=" + originatorAddress + m_CRLF;
                }
            }

            if (OriginatorPhoneNumbers != null && OriginatorPhoneNumbers.Length > 0)
            {
                foreach (string originatorNumber in OriginatorPhoneNumbers)
                {
                    sdp += (originatorNumber == null) ? null : "p=" + originatorNumber + m_CRLF;
                }
            }

            foreach (MediaAnnouncement media in Media)
            {
                sdp += (media == null) ? null : media.ToString();
            }

            foreach (string extra in ExtraAttributes)
            {
                sdp += (extra == null) ? null : extra + m_CRLF; ;
            }

            return sdp;
        }

		public static IPEndPoint GetSDPRTPEndPoint(string sdpMessage)
		{
			// Process the SDP payload.
			int rtpServerPort = Convert.ToInt32(Regex.Match(sdpMessage, @"m=audio (?<port>\d+)", RegexOptions.Singleline).Result("${port}"));
			string rtpServerAddress = Regex.Match(sdpMessage, @"c=IN IP4 (?<ipaddress>(\d+\.){3}\d+)", RegexOptions.Singleline).Result("${ipaddress}");
			IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(rtpServerAddress), rtpServerPort);
	
			return serverEndPoint;
		}

		public void AddNetTestAttribute(int pktSize, int pktSpacing)
		{
			string extraAttribute = "bfnettest: pktsize=" + pktSize + " pktspace=" + pktSpacing;
            ExtraAttributes.Add(extraAttribute);
		}

		public int GetNetTestPktSize(string sdpMessage)
		{
			try
			{
				int netTestPktSize = Convert.ToInt32(Regex.Match(sdpMessage, @"bfnettest:.*pktsize=(?<size>\d+)", RegexOptions.Singleline).Result("${size}"));
				return netTestPktSize;
			}
			catch
			{
				return 160;
			}
		}

		public int GetNetTestFrameSize(string sdpMessage)
		{
			try
			{
				int netTestPktSpacing = Convert.ToInt32(Regex.Match(sdpMessage, @"bfnettest:.*framesize=(?<space>\d+)", RegexOptions.Singleline).Result("${space}"));
				return netTestPktSpacing;
			}
			catch
			{
				return 15;
			}
		}

		public int GetNetTestChannels(string sdpMessage)
		{
			try
			{
				int netTestPktSpacing = Convert.ToInt32(Regex.Match(sdpMessage, @"bfnettest:.*channels=(?<space>\d+)", RegexOptions.Singleline).Result("${space}"));
				return netTestPktSpacing;
			}
			catch
			{
				return 1;
			}
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SDPMessageUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);	
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void SDPParseUnitTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sdpStr =
                    "v=0" + m_CRLF +
                    "o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                    "s=session" + m_CRLF +
                    "c=IN IP4 10.0.0.4" + m_CRLF +
                    "t=0 0" + m_CRLF +
                    "m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                    "a=rtpmap:0 PCMU/8000" + m_CRLF +
                    "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                    "a=fmtp:101 0-16" + m_CRLF +
                    "a=silenceSupp:off - - - -" + m_CRLF +
                    "a=ptime:20" + m_CRLF +
                    "a=sendrecv";

                SDP sdp = SDP.ParseSDPDescription(sdpStr);

                Console.WriteLine(sdp.ToString());

                Assert.IsTrue(sdp.Media[0].ConnectionAddress == "10.0.0.4", "The connection address was not parsed correctly.");
                Assert.IsTrue(sdp.Media[0].Port == 12228, "The connection port was not parsed correctly.");
            }
		}

		#endif

		#endregion
	}
}
