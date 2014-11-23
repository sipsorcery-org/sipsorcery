//-----------------------------------------------------------------------------
// Filename: SIPMessage.cs
//
// Desciption: Functionality to determine whether a SIP message is a request or
// a response and break a message up into its constituent parts.
//
// History:
// 04 May 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
	/// <bnf>
	/// generic-message  =  start-line
    ///                     *message-header
    ///                     CRLF
    ///                     [ message-body ]
    /// start-line       =  Request-Line / Status-Line
	/// </bnf>
    public class SIPMessage
	{		
		private const string SIP_RESPONSE_PREFIX = "SIP";
		private const string SIP_MESSAGE_IDENTIFIER = "SIP";	// String that must be in a message buffer to be recognised as a SIP message and processed.

		private static int m_sipFullVersionStrLen = SIPConstants.SIP_FULLVERSION_STRING.Length;
		private static int m_minFirstLineLength = 7;
		private static string m_CRLF = SIPConstants.CRLF;

        private static ILog logger = AssemblyState.logger;

		public string RawMessage;
		public SIPMessageTypesEnum SIPMessageType = SIPMessageTypesEnum.Unknown;
		public string FirstLine;
		public string[] SIPHeaders;
		public string Body;
		public byte[] RawBuffer;

		public DateTime Created = DateTime.Now;
        public SIPEndPoint RemoteSIPEndPoint;               // The remote IP socket the message was received from or sent to.
        public SIPEndPoint LocalSIPEndPoint;                // The local SIP socket the message was received on or sent from.

        public static SIPMessage ParseSIPMessage(byte[] buffer, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint)
		{
			string message = null;											  

			try
			{
				if(buffer == null || buffer.Length < m_minFirstLineLength)
				{
					// Ignore.
					return null;
				}
                else if (buffer.Length > SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH)
				{
					throw new ApplicationException("SIP message received that exceeded the maximum allowed message length, ignoring.");
				}
				else if(!ByteBufferInfo.HasString(buffer, 0, buffer.Length, SIP_MESSAGE_IDENTIFIER, m_CRLF))
				{
					// Message does not contain "SIP" anywhere on the first line, ignore.
					return null;
				}
				else
				{
					message = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                    SIPMessage sipMessage = ParseSIPMessage(message, localSIPEndPoint, remoteSIPEndPoint);

                    if (sipMessage != null)
                    {
                        sipMessage.RawBuffer = buffer;
                        return sipMessage;
                    }
                    else
                    {
                        return null;
                    }
				}
			}
			catch(Exception excp)
			{
				message = message.Replace("\n", "LF");
				message = message.Replace("\r", "CR");
				logger.Error("Exception ParseSIPMessage. " + excp.Message + "\nSIP Message=" + message + ".");
				return null;
			}
		}

        public static SIPMessage ParseSIPMessage(string message, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint)
		{
			try
			{
				SIPMessage sipMessage = new SIPMessage();
                sipMessage.LocalSIPEndPoint = localSIPEndPoint;
                sipMessage.RemoteSIPEndPoint = remoteSIPEndPoint;

				sipMessage.RawMessage = message;
				int endFistLinePosn = message.IndexOf(m_CRLF);

                if (endFistLinePosn != -1)
                {
                    sipMessage.FirstLine = message.Substring(0, endFistLinePosn);

                    if (sipMessage.FirstLine.Substring(0, 3) == SIP_RESPONSE_PREFIX)
                    {
                        sipMessage.SIPMessageType = SIPMessageTypesEnum.Response;
                    }
                    else
                    {
                        sipMessage.SIPMessageType = SIPMessageTypesEnum.Request;
                    }

                    int endHeaderPosn = message.IndexOf(m_CRLF + m_CRLF);
                    if (endHeaderPosn == -1)
                    {
                        // Assume flakey implementation if message does not contain the required CRLFCRLF sequence and treat the message as having no body.
                        string headerString = message.Substring(endFistLinePosn + 2, message.Length - endFistLinePosn - 2);
                        sipMessage.SIPHeaders = SIPHeader.SplitHeaders(headerString); //Regex.Split(headerString, m_CRLF);
                    }
                    else
                    {
                        string headerString = message.Substring(endFistLinePosn + 2, endHeaderPosn - endFistLinePosn - 2);
                        sipMessage.SIPHeaders = SIPHeader.SplitHeaders(headerString); //Regex.Split(headerString, m_CRLF);

                        if (message.Length > endHeaderPosn + 4)
                        {
                            sipMessage.Body = message.Substring(endHeaderPosn + 4);
                        }
                    }

                    return sipMessage;
                }
                else
                {
                    logger.Warn("Error ParseSIPMessage, there were no end of line characters in the string being parsed.");
                    return null;
                }
			}
			catch(Exception excp)
			{
				logger.Error("Exception ParseSIPMessage. " + excp.Message + "\nSIP Message=" + message + ".");
				return null;
			}
		}
					
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPMessageUnitTest
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
			public void ParseResponseUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipMsg = 
					"SIP/2.0 100 Trying" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKD+ta2mJ+C+VV/L50aPO1lFJnrag=" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64193;branch=z9hG4bKB86FC8D2431F49E9862D1EE439C78AD8" + m_CRLF +
					"From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=3272744142" + m_CRLF +
					"To: <sip:303@bluesipd>" + m_CRLF +
					"Call-ID: FE63F90D-4339-4AD0-9D44-59F44A1935E7@192.168.1.2" + m_CRLF +
					"CSeq: 45560 INVITE" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Content-Length: 0" + m_CRLF + m_CRLF;

                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
				
				Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void ParseResponseWithBodyUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipMsg = 
					"SIP/2.0 200 OK" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKT36BdhXPlT5cqPFQQr81yMmZ37U=" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64216;branch=z9hG4bK7D8B6549580844AEA104BD4A837049DD" + m_CRLF +
					"From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=630217013" + m_CRLF +
					"To: <sip:303@bluesipd>;tag=as46f418e9" + m_CRLF +
					"Call-ID: 9AA41C8F-D691-49F3-B346-2538B5FD962F@192.168.1.2" + m_CRLF +
					"CSeq: 27481 INVITE" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Content-Type: application/sdp" + m_CRLF +
					"Content-Length: 352" + m_CRLF +
					m_CRLF +
					"v=0" + m_CRLF +
					"o=root 24710 24712 IN IP4 213.168.225.133" + m_CRLF +
					"s=session" + m_CRLF +
					"c=IN IP4 213.168.225.133" + m_CRLF +
					"t=0 0" + m_CRLF +
					"m=audio 18656 RTP/AVP 0 8 18 3 97 111 101" + m_CRLF +
					"a=rtpmap:0 PCMU/8000" + m_CRLF +
					"a=rtpmap:8 PCMA/8000" + m_CRLF +
					"a=rtpmap:18 G729/8000" + m_CRLF +
					"a=rtpmap:3 GSM/8000" + m_CRLF +
					"a=rtpmap:97 iLBC/8000" + m_CRLF +
					"a=rtpmap:111 G726-32/8000" + m_CRLF +
					"a=rtpmap:101 telephone-event/8000" + m_CRLF +
					"a=fmtp:101 0-16" + m_CRLF +
					"a=silenceSupp:off - - - -" + m_CRLF;

                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);

				Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}	

			[Test]
			public void ParseResponseNoEndDoubleCRLFUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipMsg = 
					"SIP/2.0 100 Trying" + m_CRLF +
					"Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bKD+ta2mJ+C+VV/L50aPO1lFJnrag=" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64193;branch=z9hG4bKB86FC8D2431F49E9862D1EE439C78AD8" + m_CRLF +
					"From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=3272744142" + m_CRLF +
					"To: <sip:303@bluesipd>" + m_CRLF +
					"Call-ID: FE63F90D-4339-4AD0-9D44-59F44A1935E7@192.168.1.2" + m_CRLF +
					"CSeq: 45560 INVITE" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Content-Length: 0" + m_CRLF;

                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
				
				Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

            [Test]
			public void ParseCiscoOptionsResponseUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string sipMsg = 
					"SIP/2.0 200 OK" + m_CRLF +
                    "Via: SIP/2.0/UDP 213.168.225.133:5060;branch=z9hG4bK7ae332e73550dbdf2f159061651e7ed5bb88ac52, SIP/2.0/UDP 194.213.29.52:5064;branch=z9hG4bK1121681627" + m_CRLF +
                    "From: <sip:natkeepalive@194.213.29.52:5064>;tag=8341482660" + m_CRLF +
                    "To: <sip:user@1.2.3.4:5060>;tag=000e38e46c60ef28651381fe-201e6ab1" + m_CRLF +
                    "Call-ID: 1125158248@194.213.29.52" + m_CRLF +
                    "Date: Wed, 29 Nov 2006 22:31:58 GMT" + m_CRLF +
                    "CSeq: 148 OPTIONS" + m_CRLF +
                    "Server: CSCO/7" + m_CRLF +
                    "Content-Type: application/sdp" + m_CRLF +
                    "Allow: OPTIONS,INVITE,BYE,CANCEL,REGISTER,ACK,NOTIFY,REFER" + m_CRLF +
                    "Content-Length: 193" + m_CRLF +
                    m_CRLF +
                    "v=0" + m_CRLF +
                    "o=Cisco-SIPUA (null) (null) IN IP4 87.198.196.121" + m_CRLF +
                    "s=SIP Call" + m_CRLF +
                    "c=IN IP4 87.198.196.121" + m_CRLF +
                    "t=0 0" + m_CRLF +
                    "m=audio 1 RTP/AVP 18 0 8" + m_CRLF +
                    "a=rtpmap:18 G729/8000" + m_CRLF +
                    "a=rtpmap:0 PCMU/8000" + m_CRLF +
                    "a=rtpmap:8 PCMA/8000" + m_CRLF +
                    m_CRLF;

                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
                SIPResponse sipResponse = SIPResponse.ParseSIPResponse(sipMessage);
				
				Assert.IsTrue(sipMessage != null, "The SIP message not parsed correctly.");
                Assert.IsTrue(sipResponse.Header.Vias.Length == 2, "The SIP reponse did not end up with the right number of Via headers.");

				Console.WriteLine("-----------------------------------------");
			}  
		}

		#endif

		#endregion
	}
}
