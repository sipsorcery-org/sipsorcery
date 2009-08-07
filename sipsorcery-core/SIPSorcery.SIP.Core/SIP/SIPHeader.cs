//-----------------------------------------------------------------------------
// Filename: SIPHeader.cs
//
// Description: SIP Header.
// 
// History:
// 17 Sep 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Serialization;
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
    /// Via               =  ( "Via" / "v" ) HCOLON via-parm *(COMMA via-parm)
    /// via-parm          =  sent-protocol LWS sent-by *( SEMI via-params )
    /// via-params        =  via-ttl / via-maddr / via-received / via-branch / via-extension
    /// via-ttl           =  "ttl" EQUAL ttl
    /// via-maddr         =  "maddr" EQUAL host
    /// via-received      =  "received" EQUAL (IPv4address / IPv6address)
    /// via-branch        =  "branch" EQUAL token
    /// via-extension     =  generic-param
    /// sent-protocol     =  protocol-name SLASH protocol-version SLASH transport
    /// protocol-name     =  "SIP" / token
    /// protocol-version  =  token
    /// transport         =  "UDP" / "TCP" / "TLS" / "SCTP" / other-transport
    /// sent-by           =  host [ COLON port ]
    /// ttl               =  1*3DIGIT ; 0 to 255
    /// generic-param     =  token [ EQUAL gen-value ]
    /// gen-value         =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The Via header only has parameters, no headers. Parameters of from ...;name=value;name2=value2
    /// Specific parameters: ttl, maddr, received, branch.
    /// 
    /// From page 179 of RFC3261:
    /// "Even though this specification mandates that the branch parameter be
    /// present in all requests, the BNF for the header field indicates that
    /// it is optional."
    /// 
    /// The branch parameter on a Via therefore appears to be optionally mandatory?!
    ///
    /// Any SIP application element that uses transactions depends on the branch parameter for transaction matching.
    /// Only the top Via header branch is used for transactions though so if the request has made it to this stack
    /// with missing branches then in theory it should be safe to proceed. It will be left up to the SIPTransaction
    /// class to reject any SIP requests that are missing the necessary branch.
    /// </remarks>
	public class SIPViaHeader
	{
        private static ILog logger = AssemblyState.logger;
		
        private static char m_paramDelimChar = ';';
        private static char m_hostDelimChar = ':';

        private static string m_receivedKey = SIPHeaderAncillary.SIP_HEADERANC_RECEIVED;
        private static string m_rportKey = SIPHeaderAncillary.SIP_HEADERANC_RPORT;
        private static string m_branchKey = SIPHeaderAncillary.SIP_HEADERANC_BRANCH;

		public string Version;
        public SIPProtocolsEnum Transport;
		public string Host;
		public int Port = 0;
        public string Branch
        {
            get
            {
                if (ViaParameters != null && ViaParameters.Has(m_branchKey))
                {
                    return ViaParameters.Get(m_branchKey);
                }
                else
                {
                    return null;
                }
            }
            set { ViaParameters.Set(m_branchKey, value); }
        }
        public string ReceivedFromIPAddress		// IP Address contained in the recevied parameter.
        {
            get
            {
                if (ViaParameters != null && ViaParameters.Has(m_receivedKey))
                {
                    return ViaParameters.Get(m_receivedKey);
                }
                else
                {
                    return null;
                }
            }
            set { ViaParameters.Set(m_receivedKey, value); }
        }
        public int ReceivedFromPort		        // Port contained in the rport parameter.
        {
            get
            {
                if (ViaParameters != null && ViaParameters.Has(m_rportKey))
                {
                    return Convert.ToInt32(ViaParameters.Get(m_rportKey));
                }
                else
                {
                    return 0;
                }
            }
            set { ViaParameters.Set(m_rportKey, value.ToString()); } 
        }

        public SIPParameters ViaParameters = new SIPParameters(null, m_paramDelimChar);
		
		public string ContactAddress            // This the address placed into the Via header by the User Agent.
		{
			get
			{
                if (Port != 0)
                {
                    return Host + ":" + Port;
                }
                else
                {
                    return Host;
                }
			}
		}

        public string ReceivedFromAddress       // This is the socket the request was received on and is a combination of the Host and Received fields.
        {
            get
            {
                if (ReceivedFromIPAddress != null && ReceivedFromPort != 0)
                {
                    return ReceivedFromIPAddress + ":" + ReceivedFromPort;
                }
                else if (ReceivedFromIPAddress != null && Port != 0)
                {
                    return ReceivedFromIPAddress + ":" + Port;
                }
                else if (ReceivedFromIPAddress != null)
                {
                    return ReceivedFromIPAddress;
                }
                else if (ReceivedFromPort != 0)
                {
                    return Host + ":" + ReceivedFromPort;
                }
                else if (Port != 0)
                {
                    return Host + ":" + Port;
                }
                else
                {
                    return Host;
                }
            }
        }

		public SIPViaHeader()
		{}
		
		public SIPViaHeader(string contactIPAddress, int contactPort, string branch)
		{
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = SIPProtocolsEnum.udp;
			Host = contactIPAddress;
			Port = contactPort;
			Branch = branch;
		}

        public SIPViaHeader(string contactIPAddress, int contactPort, string branch, SIPProtocolsEnum protocol)
        {
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = protocol;
            Host = contactIPAddress;
            Port = contactPort;
            Branch = branch;
        }

        public SIPViaHeader(IPEndPoint contactEndPoint, string branch)
        {
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = SIPProtocolsEnum.udp;
            Host = contactEndPoint.Address.ToString();
            Port = contactEndPoint.Port;
            Branch = branch;
        }

        public SIPViaHeader(SIPEndPoint localEndPoint, string branch)
        {
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = localEndPoint.SIPProtocol;
            Host = localEndPoint.SocketEndPoint.Address.ToString();
            Port = localEndPoint.SocketEndPoint.Port;
            Branch = branch;
        }

        public SIPViaHeader(string contactEndPoint, string branch)
        {
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = SIPProtocolsEnum.udp;
            IPEndPoint contactSocket = IPSocket.GetIPEndPoint(contactEndPoint);
            Host = contactSocket.Address.ToString();
            Port = contactSocket.Port;
            Branch = branch;
        }

        public SIPViaHeader(IPEndPoint contactEndPoint, string branch, SIPProtocolsEnum protocol)
        {
            Version = SIPConstants.SIP_FULLVERSION_STRING;
            Transport = protocol;
            Host = contactEndPoint.Address.ToString();
            Port = contactEndPoint.Port;
            Branch = branch;
        }

		public static SIPViaHeader[] ParseSIPViaHeader(string viaHeaderStr)
		{
            List<SIPViaHeader> viaHeadersList = new List<SIPViaHeader>();
			
			if(!viaHeaderStr.IsNullOrBlank())
			{
				viaHeaderStr = viaHeaderStr.Trim();

				// Multiple Via headers can be contained in a single line by separating them with a comma.
                string[] viaHeaders = SIPParameters.GetKeyValuePairsFromQuoted(viaHeaderStr, ',');

				foreach(string viaHeaderStrItem in viaHeaders)
				{
                    if (viaHeaderStrItem == null || viaHeaderStrItem.Trim().Length == 0)
                    {
                        throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "No Contact address.");
                    }
                    else
                    {
                        SIPViaHeader viaHeader = new SIPViaHeader();
                        string header = viaHeaderStrItem.Trim();

                        int firstSpacePosn = header.IndexOf(" ");
                        if (firstSpacePosn == -1)
                        {
                            throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "No Contact address.");
                        }
                        else
                        {
                            string versionAndTransport = header.Substring(0, firstSpacePosn);
                            viaHeader.Version = versionAndTransport.Substring(0, versionAndTransport.LastIndexOf('/'));
                            viaHeader.Transport = SIPProtocolsType.GetProtocolType(versionAndTransport.Substring(versionAndTransport.LastIndexOf('/') + 1));

                            string nextField = header.Substring(firstSpacePosn, header.Length - firstSpacePosn).Trim();

                            int delimIndex = nextField.IndexOf(';');
                            string contactAddress = null;

                            // Some user agents include branch but have the semi-colon missing, that's easy to cope with by replacing "branch" with ";branch".
                            if (delimIndex == -1 && nextField.Contains(m_branchKey))
                            {
                                nextField = nextField.Replace(m_branchKey, ";" + m_branchKey);
                                delimIndex = nextField.IndexOf(';');
                            }

                            if (delimIndex == -1)
                            {
                                //logger.Warn("Via header missing semi-colon: " + header + ".");
                                //parserError = SIPValidationError.NoBranchOnVia;
                                //return null;
                                contactAddress = nextField.Trim();
                            }
                            else
                            {
                                contactAddress = nextField.Substring(0, delimIndex).Trim();
                                viaHeader.ViaParameters = new SIPParameters(nextField.Substring(delimIndex, nextField.Length - delimIndex), m_paramDelimChar);
                            }

                            if (contactAddress == null || contactAddress.Trim().Length == 0)
                            {
                                // Check that the branch parameter is present, without it the Via header is illegal.
                                //if (!viaHeader.ViaParameters.Has(m_branchKey))
                                //{
                                //    logger.Warn("Via header missing branch: " + header + ".");
                                //    parserError = SIPValidationError.NoBranchOnVia;
                                //    return null;
                                //}

                                throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "No Contact address.");
                            }

                            // Parse the contact address.
                            int colonIndex = contactAddress.IndexOf(m_hostDelimChar);
                            if (colonIndex != -1)
                            {
                                viaHeader.Host = contactAddress.Substring(0, colonIndex);

                                if (!Int32.TryParse(contactAddress.Substring(colonIndex + 1), out viaHeader.Port))
                                {
                                    throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "Non-numeric port for IP address.");
                                }
                            }
                            else
                            {
                                viaHeader.Host = contactAddress;
                            }

                            viaHeadersList.Add(viaHeader);
                        }
                    }
				}
			}

			if(viaHeadersList.Count > 0)
			{
				return viaHeadersList.ToArray();
			}
			else
			{
                throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "Via list was empty."); ;
			}
		}
		
		public new string ToString()
		{
            // Removing the rport parameter if it is empty. This should not be necessary but a version of the Sipura firmware was rejecting 
            // INVITE requests with an empty rport parameter. Hopefully no UA's break because it's not there.
            if (ViaParameters.Has(m_rportKey) && (ViaParameters.Get(m_rportKey) == null || ViaParameters.Get(m_rportKey).Trim().Length == 0))
            {
                ViaParameters.Remove(m_rportKey);
            }
            
            string sipViaHeader = SIPHeaders.SIP_HEADER_VIA + ": " + this.Version + "/" + this.Transport.ToString().ToUpper() + " " + ContactAddress;
            sipViaHeader += (ViaParameters != null && ViaParameters.Count > 0) ? ViaParameters.ToString() : null;
           
			return sipViaHeader;
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPViaHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void ParseXTenViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

				SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

				Console.WriteLine("Version = " + sipViaHeaders[0].Version + ".");
                Console.WriteLine("Transport = " + sipViaHeaders[0].Transport + ".");
				Console.WriteLine("Contact = " + sipViaHeaders[0].ContactAddress + ".");
				Console.WriteLine("received = " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
				Console.WriteLine("rport = " + sipViaHeaders[0].ReceivedFromPort + ".");
				Console.WriteLine("branch = " + sipViaHeaders[0].Branch + ".");
				Console.WriteLine("Parsed header = " + sipViaHeaders[0].ToString());

				Assert.IsTrue("SIP/2.0" == sipViaHeaders[0].Version, "The Via header Version was not correctly parsed, " + sipViaHeaders[0].Version + ".");
                Assert.IsTrue(SIPProtocolsEnum.udp == sipViaHeaders[0].Transport, "The Via header Transport was not correctly parsed, " + sipViaHeaders[0].Transport + ".");
				Assert.IsTrue("192.168.1.2:5065" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
				Assert.IsTrue(null == sipViaHeaders[0].ReceivedFromIPAddress, "The Via header received field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
				Assert.IsTrue(0 == sipViaHeaders[0].ReceivedFromPort, "The Via header rport field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromPort + ".");
				Assert.IsTrue("z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeaders[0].Branch, "The Via header branch was not correctly parsed, " + sipViaHeaders[0].Branch + ".");
				
				//Assert.IsTrue("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
			public void ParseReceivedFromIPViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;received=88.99.88.99;rport=10060;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

				SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

                Console.WriteLine("Version = " + sipViaHeaders[0].Version + ".");
                Console.WriteLine("Transport = " + sipViaHeaders[0].Transport + ".");
				Console.WriteLine("Contact = " + sipViaHeaders[0].ContactAddress + ".");
				Console.WriteLine("received = " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
				Console.WriteLine("rport = " + sipViaHeaders[0].ReceivedFromPort + ".");
				Console.WriteLine("branch = " + sipViaHeaders[0].Branch + ".");
				Console.WriteLine("Parsed header = " + sipViaHeaders[0].ToString());

                Assert.IsTrue("SIP/2.0" == sipViaHeaders[0].Version, "The Via header Version was not correctly parsed, " + sipViaHeaders[0].Version + ".");
                Assert.IsTrue(SIPProtocolsEnum.udp == sipViaHeaders[0].Transport, "The Via header Transport was not correctly parsed, " + sipViaHeaders[0].Transport + "."); Assert.IsTrue("192.168.1.2:5065" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
				Assert.IsTrue("88.99.88.99" == sipViaHeaders[0].ReceivedFromIPAddress, "The Via header received field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromIPAddress + ".");
				Assert.IsTrue(10060 == sipViaHeaders[0].ReceivedFromPort, "The Via header rport field was not correctly parsed, " + sipViaHeaders[0].ReceivedFromPort + ".");
				Assert.IsTrue("z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeaders[0].Branch, "The Via header branch was not correctly parsed, " + sipViaHeaders[0].Branch + ".");
				
				//Assert.IsTrue("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
			public void ParseNoPortViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string noPortViaHeader = "SIP/2.0/UDP 192.168.1.1;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

				SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

				Console.WriteLine("Via Header Contact Address = " + sipViaHeaders[0].ContactAddress);
                Console.WriteLine("Via Header Received From Address = " + sipViaHeaders[0].ReceivedFromAddress);

                Assert.IsTrue(sipViaHeaders[0].Host == "192.168.1.1", "The Via header host was not parsed correctly");
				Assert.IsTrue("192.168.1.1" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");

				Console.WriteLine("---------------------------------------------------");
			}
		
			[Test]
			public void ParseNoSemiColonViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234";

				SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

                Assert.IsTrue(sipViaHeaders.Length == 1, "The Via header list should have had a single entry.");
                Assert.IsTrue(sipViaHeaders[0].ContactAddress == "192.168.1.1:1234", "The Via header contact address was parsed incorrectly.");

				Console.WriteLine("---------------------------------------------------");
			}

            [Test]
            [ExpectedException(typeof(SIPValidationException))]
            public void ParseNoContactViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string noContactViaHeader = "SIP/2.0/UDP";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noContactViaHeader);

                Console.WriteLine("---------------------------------------------------");
            }

            [Test]
            public void ParseNoSemiButHasBranchColonViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

                Assert.IsTrue(sipViaHeaders[0].Host == "192.168.1.1", "The Via header host was not parsed correctly");
                Assert.IsTrue("192.168.1.1:1234" == sipViaHeaders[0].ContactAddress, "The Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
                Assert.IsTrue(sipViaHeaders[0].Branch == "z9hG4bKFBB7EAC06934405182D13950BD51F001", "The Via header branch was not parsed correctly.");

                Console.WriteLine("---------------------------------------------------");
            }

            [Test]
            public void ParseNoBranchViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234;rport";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

                //Assert.IsNull(sipViaHeaders, "The Via header list should have been empty.");
                Assert.IsTrue(sipViaHeaders[0].ContactAddress == "192.168.1.1:1234", "The Via header contact was not correctly parsed.");
                Assert.IsNull(sipViaHeaders[0].Branch, "The Via branch should have been null.");

                Console.WriteLine("---------------------------------------------------");
            }

			[Test]
            [ExpectedException(typeof(SIPValidationException))]
			public void ParseBadAastraViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string noSemiColonViaHeader = "SIP/2.0/UDP 192.168.1.1:1234port;branch=213123";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noSemiColonViaHeader);

				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
			public void MaintainUnknownHeaderViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;received=88.99.88.99;unknown=12234;unknown2;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001;rport";

				SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

				Console.WriteLine("Via Header=" + sipViaHeaders[0].ToString() + ".");

				//Assert.IsTrue(Regex.Match(sipViaHeaders[0].ToString(), "rport").Success, "The Via header did not maintain the unknown rport parameter.");
				Assert.IsTrue(Regex.Match(sipViaHeaders[0].ToString(), "unknown=12234").Success, "The Via header did not maintain the unrecognised unknown parameter.");
                Assert.IsTrue(Regex.Match(sipViaHeaders[0].ToString(), "unknown2").Success, "The Via header did not maintain the unrecognised unknown2 parameter.");

				//Assert.IsTrue("SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipViaHeader.ToString(), "The Via header was not parsed correctly.");

				Console.WriteLine("---------------------------------------------------");
			}

            [Test]
            public void GetIPEndPointViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

                Assert.IsTrue(sipViaHeaders[0].ContactAddress == "192.168.1.2:5065", "Incorrect endpoint address for Via header.");

                Console.WriteLine("---------------------------------------------------");
            }

            [Test]
            public void CreateNewViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPViaHeader viaHeader = new SIPViaHeader("192.168.1.2", 5063, "abcdefgh");

                Assert.IsTrue(viaHeader.Host == "192.168.1.2", "Incorrect Host for Via header.");
                Assert.IsTrue(viaHeader.Port == 5063, "Incorrect Port for Via header.");
                Assert.IsTrue(viaHeader.Branch == "abcdefgh", "Incorrect Branch for Via header.");

                Console.WriteLine("---------------------------------------------------");
            }

            [Test]
            public void ParseMultiViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string noPortViaHeader = "SIP/2.0/UDP 192.168.1.1:5060;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001, SIP/2.0/UDP 192.168.0.1:5061;branch=z9hG4bKFBB7EAC06";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

                Assert.IsTrue(sipViaHeaders[0].Host == "192.168.1.1", "The first Via header host was not parsed correctly");
                Assert.IsTrue("192.168.1.1:5060" == sipViaHeaders[0].ContactAddress, "The first Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
                Assert.IsTrue(sipViaHeaders[1].Host == "192.168.0.1", "The second Via header host was not parsed correctly");
                Assert.IsTrue("192.168.0.1:5061" == sipViaHeaders[1].ContactAddress, "The second Via header contact address was not correctly parsed, " + sipViaHeaders[1].ContactAddress + ".");

                Console.WriteLine("---------------------------------------------------");
            }

            [Test]
            public void ParseMultiViaHeaderTest2()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string noPortViaHeader = "SIP/2.0/UDP 194.213.29.100:5060;branch=z9hG4bK5feb18267ce40fb05969b4ba843681dbfc9ffcff, SIP/2.0/UDP 194.213.29.54:5061;branch=z9hG4bK52b6a8b7";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(noPortViaHeader);

                Assert.IsTrue(sipViaHeaders[0].Host == "194.213.29.100", "The first Via header host was not parsed correctly");
                Assert.IsTrue("194.213.29.100:5060" == sipViaHeaders[0].ContactAddress, "The first Via header contact address was not correctly parsed, " + sipViaHeaders[0].ContactAddress + ".");
                Assert.IsTrue(sipViaHeaders[1].Host == "194.213.29.54", "The second Via header host was not parsed correctly");
                Assert.IsTrue("194.213.29.54:5061" == sipViaHeaders[1].ContactAddress, "The second Via header contact address was not correctly parsed, " + sipViaHeaders[1].ContactAddress + ".");

                Console.WriteLine("---------------------------------------------------");
            }
		}

		#endif

		#endregion
	}

    /// <bnf>
    /// From            =  ( "From" / "f" ) HCOLON from-spec
    /// from-spec       =  ( name-addr / addr-spec ) *( SEMI from-param )
    /// from-param      =  tag-param / generic-param
    /// name-addr		=  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec		=  SIP-URI / SIPS-URI / absoluteURI
    /// tag-param       =  "tag" EQUAL token
    /// generic-param   =  token [ EQUAL gen-value ]
    /// gen-value       =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The From header only has parameters, no headers. Parameters of from ...;name=value;name2=value2.
    /// Specific parameters: tag.
    /// </remarks>
	public class SIPFromHeader
	{
		//public const string DEFAULT_FROM_NAME = SIPConstants.SIP_DEFAULT_USERNAME;
		public const string DEFAULT_FROM_URI = SIPConstants.SIP_DEFAULT_FROMURI;
		public const string PARAMETER_TAG = SIPHeaderAncillary.SIP_HEADERANC_TAG;

		public string FromName
		{
			get{ return m_userField.Name; }
			set{ m_userField.Name = value; }
		}

		public SIPURI FromURI
		{
			get{ return m_userField.URI; }
			set{ m_userField.URI = value; }
		}

		public string FromTag
		{
            get { return FromParameters.Get(PARAMETER_TAG); }
            set {            
                if (value != null && value.Trim().Length > 0)
                {
                    FromParameters.Set(PARAMETER_TAG, value);
                }
                else
                {
                    if (FromParameters.Has(PARAMETER_TAG))
                    {
                        FromParameters.Remove(PARAMETER_TAG);
                    }
                }
            }
		}

        public SIPParameters FromParameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

		private SIPUserField m_userField = new SIPUserField();
		public SIPUserField FromUserField
		{
			get{ return m_userField; }
			set{ m_userField = value; }
		}

		private SIPFromHeader()
		{}

		public SIPFromHeader(string fromName, SIPURI fromURI, string fromTag)
		{ 		
			m_userField = new SIPUserField(fromName, fromURI, null);
            FromTag = fromTag;
		}
	
        public static SIPFromHeader ParseFromHeader(string fromHeaderStr)
		{
			try
			{
				SIPFromHeader fromHeader = new SIPFromHeader();
			
				fromHeader.m_userField = SIPUserField.ParseSIPUserField(fromHeaderStr);

				return fromHeader;
			}
			catch
			{
				throw new SIPValidationException(SIPValidationFieldsEnum.FromHeader, "The SIP From header was invalid.");
			}
		}

		public override string ToString()
		{
            return m_userField.ToString();
		}
	
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPFromHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void ParseFromHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "\"User\" <sip:user@domain.com>;tag=abcdef";

				SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Console.WriteLine("From header=" + sipFromHeader.ToString() + ".");

				Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domain.com", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == "abcdef", "The From header Tag was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.ToString() == testFromHeader, "The From header ToString method did not produce the correct results.");
			}
			
			[Test]
			public void ParseFromHeaderNoTagTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "User <sip:user@domain.com>";

				SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domain.com", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
			}

			[Test]
			public void ParseFromHeaderSocketDomainTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "User <sip:user@127.0.0.1:5090>";

                SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@127.0.0.1:5090", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
			}

			[Test]
			public void ParseFromHeaderSocketDomainAndTagTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "User <sip:user@127.0.0.1:5090>;tag=abcdef";

				SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == "User", "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@127.0.0.1:5090", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == "abcdef", "The From header Tag was not parsed correctly.");
			}

			[Test]
			public void ParseFromHeaderNoNameTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "<sip:user@domaintest.com>";

				SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
			}
			
			[Test]
			public void ParseFromHeaderNoAngleBracketsTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "sip:user@domaintest.com";

				SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == null, "The From header Tag was not parsed correctly.");
			}

			[Test]
			public void ParseFromHeaderNoSpaceTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "UNAVAILABLE<sip:user@domaintest.com:5060>;tag=abcd";

				SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == "UNAVAILABLE", "The From header name was not parsed correctly, name=" + sipFromHeader.FromName + ".");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:user@domaintest.com:5060", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == "abcd", "The From header Tag was not parsed correctly.");
			}
			
			[Test]
			public void ParseFromHeaderNoUserTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testFromHeader = "<sip:sip.domain.com>;tag=as6900b876";

                SIPFromHeader sipFromHeader = SIPFromHeader.ParseFromHeader(testFromHeader);

				Assert.IsTrue(sipFromHeader.FromName == null, "The From header name was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromURI.ToString() == "sip:sip.domain.com", "The From header URI was not parsed correctly.");
				Assert.IsTrue(sipFromHeader.FromTag == "as6900b876", "The From header Tag was not parsed correctly.");
			}
		}

		#endif

		#endregion
	}

	/// <bnf>
	/// To				=  ( "To" / "t" ) HCOLON ( name-addr / addr-spec ) *( SEMI to-param )
	/// to-param		=  tag-param / generic-param
	/// name-addr		=  [ display-name ] LAQUOT addr-spec RAQUOT
	/// addr-spec		=  SIP-URI / SIPS-URI / absoluteURI
    /// tag-param       =  "tag" EQUAL token
    /// generic-param   =  token [ EQUAL gen-value ]
    /// gen-value       =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The To header only has parameters, no headers. Parameters of from ...;name=value;name2=value2.
    /// Specific parameters: tag.
    /// </remarks>
	public class SIPToHeader
	{
		public const string PARAMETER_TAG = SIPHeaderAncillary.SIP_HEADERANC_TAG;
		
		public string ToName
		{
			get{ return m_userField.Name; }
			set{ m_userField.Name = value; }
		}

		public SIPURI ToURI
		{
			get{ return m_userField.URI; }
			set{ m_userField.URI = value; }
		}

        public string ToTag
        {
            get { return ToParameters.Get(PARAMETER_TAG); }
            set 
            {
                if (value != null && value.Trim().Length > 0)
                {
                    ToParameters.Set(PARAMETER_TAG, value);
                }
                else
                {
                    if (ToParameters.Has(PARAMETER_TAG))
                    {
                        ToParameters.Remove(PARAMETER_TAG);
                    }
                }
            }
        }

        public SIPParameters ToParameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

		private SIPUserField m_userField;
		public SIPUserField ToUserField
		{
			get{ return m_userField; }
			set{ m_userField = value; }
		}

		private SIPToHeader()
		{}

		public SIPToHeader(string toName, SIPURI toURI, string toTag)
		{
			m_userField = new SIPUserField(toName, toURI, null);
            ToTag = toTag;
		}

		public static SIPToHeader ParseToHeader(string toHeaderStr)
		{
			try
			{
				SIPToHeader toHeader = new SIPToHeader();

				toHeader.m_userField = SIPUserField.ParseSIPUserField(toHeaderStr);

				return toHeader;
			}
			catch
			{
				throw new SIPValidationException(SIPValidationFieldsEnum.ToHeader, "The SIP To header was invalid.");
			}
		}

		public override string ToString()
		{
            return m_userField.ToString();
		}
		
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPToHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			
			[Test]
			public void ParseToHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

				SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

				Assert.IsTrue(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToTag == "abcdef", "The To header Tag was not parsed correctly.");
			}

			[Test]
			public void ParseMSCToHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testToHeader = "sip:xxx@127.0.110.30;tag=AZHf2-ZMfDX0";

                SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

				Console.WriteLine("To header: " + sipToHeader.ToString());

				Assert.IsTrue(sipToHeader.ToName == null, "The To header name was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:xxx@127.0.110.30", "The To header URI was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToTag == "AZHf2-ZMfDX0", "The To header Tag was not parsed correctly.");
			}

			[Test]
			public void ToStringToHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

				SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

				Assert.IsTrue(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToTag == "abcdef", "The To header Tag was not parsed correctly.");
                Assert.IsTrue(sipToHeader.ToString() == "\"User\" <sip:user@domain.com>;tag=abcdef", "The To header was not put ToString correctly.");
			}

			/// <summary>
			/// New requests should be received with no To header tag. It is up to the recevier to populate the To header tag.
			/// This test makes sure that changing the tag works correctly.
			/// </summary>
			[Test]
			public void ChangeTagToHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testToHeader = "User <sip:user@domain.com>;tag=abcdef";

				SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testToHeader);

				string newTag = "123456";
				sipToHeader.ToTag = newTag;

				Console.WriteLine("To header with new tag: " + sipToHeader.ToString());

				Assert.IsTrue(sipToHeader.ToName == "User", "The To header name was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:user@domain.com", "The To header URI was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToTag == newTag, "The To header Tag was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToString() == "\"User\" <sip:user@domain.com>;tag=123456", "The To header was not put ToString correctly.");
			}

			[Test]
			public void ParseByeToHeader()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				string testHeader = "\"Joe Bloggs\" <sip:joe@sip.blueface.ie>;tag=0013c339acec34652d988c7e-4fddcdef";

				SIPToHeader sipToHeader = SIPToHeader.ParseToHeader(testHeader);

				Console.WriteLine("To header: " + sipToHeader.ToString());

				Assert.IsTrue(sipToHeader.ToName == "Joe Bloggs", "The To header name was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToURI.ToString() == "sip:joe@sip.blueface.ie", "The To header URI was not parsed correctly.");
				Assert.IsTrue(sipToHeader.ToTag == "0013c339acec34652d988c7e-4fddcdef", "The To header Tag was not parsed correctly.");
			}
		}

		#endif

		#endregion
	}

	/// <bnf>
	/// Contact        =  ("Contact" / "m" ) HCOLON ( STAR / (contact-param *(COMMA contact-param)))
	/// contact-param  =  (name-addr / addr-spec) *(SEMI contact-params)
	/// name-addr      =  [ display-name ] LAQUOT addr-spec RAQUOT
	/// addr-spec      =  SIP-URI / SIPS-URI / absoluteURI
	/// display-name   =  *(token LWS)/ quoted-string
	///
	/// contact-params     =  c-p-q / c-p-expires / contact-extension
	/// c-p-q              =  "q" EQUAL qvalue
	/// c-p-expires        =  "expires" EQUAL delta-seconds
	/// contact-extension  =  generic-param
	/// delta-seconds      =  1*DIGIT
    /// generic-param  =  token [ EQUAL gen-value ]
    /// gen-value      =  token / host / quoted-string
	/// </bnf>
    /// <remarks>
    /// The Contact header only has parameters, no headers. Parameters of from ...;name=value;name2=value2
    /// Specific parameters: q, expires.
    /// </remarks>
	[DataContract]
    public class SIPContactHeader
	{
        public const string EXPIRES_PARAMETER_KEY = "expires";
        public const string QVALUE_PARAMETER_KEY = "q";

        private static ILog logger = AssemblyState.logger;

        private static char[] m_nonStandardURIDelimChars = new char[]{'\n','\r',' '};	// Characters that can delimit a SIP URI, supposed to be > but it is sometimes missing.

		public string RawHeader;

		public string ContactName
		{
			get{ return m_userField.Name; }
			set{ m_userField.Name = value; }
		}

		public SIPURI ContactURI
		{
			get{ return m_userField.URI; }
			set{ m_userField.URI = value; }
		}

        public SIPParameters ContactParameters
        {
            get { return m_userField.Parameters; }
            set { m_userField.Parameters = value; }
        }

        // A value of -1 indicates the header did not contain an expires parameter setting.
        public int Expires
        {
            get 
            {
                if (ContactParameters.Has(EXPIRES_PARAMETER_KEY))
                {
                    string expiresStr = ContactParameters.Get(EXPIRES_PARAMETER_KEY);

                    try
                    {
                        return Convert.ToInt32(expiresStr);
                    }
                    catch(Exception excp)
                    {
                        logger.Error("Exception converting Expires header of " + expiresStr + " to Int32. " + excp.Message);
                        return -1;
                    }
                }
                else
                {
                    return -1;
                }
            }
            set{ ContactParameters.Set(EXPIRES_PARAMETER_KEY, value.ToString()); }
        }
        public string Q
        {
            get{ return ContactParameters.Get(QVALUE_PARAMETER_KEY);}
            set {ContactParameters.Set(QVALUE_PARAMETER_KEY, value);}
        }

		private SIPUserField m_userField;

		private SIPContactHeader()
		{}

		public SIPContactHeader(string contactName, SIPURI contactURI)
		{
			m_userField = new SIPUserField(contactName, contactURI, null);
		}

        public SIPContactHeader(SIPUserField contactUserField)
        {
            m_userField = contactUserField;
        }
	
        public static List<SIPContactHeader> ParseContactHeader(string contactHeaderStr)
		{
            try
            {
                if (contactHeaderStr == null || contactHeaderStr.Trim().Length == 0)
                {
                    return null;
                }

                string[] contactHeaders = null;

                // Broken User Agent fix (Aastra looking at you!)
                if (contactHeaderStr.IndexOf('<') != -1 && contactHeaderStr.IndexOf('>') == -1)
                {
                    int nonStandardDelimPosn = contactHeaderStr.IndexOfAny(m_nonStandardURIDelimChars);

                    if (nonStandardDelimPosn != -1)
                    {
                        // Add on the missing RQUOT and ignore whatever the rest of the header is.
                        contactHeaders = new string[] { contactHeaderStr.Substring(0, nonStandardDelimPosn) + ">" };
                    }
                    else
                    {
                        // Can't work out what is going on with this header bomb out.
                        throw new SIPValidationException(SIPValidationFieldsEnum.ContactHeader, "Contact header invalid.");
                    }
                }
                else
                {
                    contactHeaders = SIPParameters.GetKeyValuePairsFromQuoted(contactHeaderStr, ',');
                }

                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (string contactHeaderItemStr in contactHeaders)
                {
                    SIPContactHeader contactHeader = new SIPContactHeader();
                    contactHeader.RawHeader = contactHeaderStr;
                    contactHeader.m_userField = SIPUserField.ParseSIPUserField(contactHeaderItemStr);
                    contactHeaderList.Add(contactHeader);
                }

                return contactHeaderList;
            }
            catch (SIPValidationException sipValidationExcp)
            {
                throw sipValidationExcp;
            }
            catch(Exception excp)
            {
                logger.Error("Exception ParseContactHeader. " + excp.Message);
                throw new SIPValidationException(SIPValidationFieldsEnum.ContactHeader, "Contact header invalid.");
            }
		}

        public static List<SIPContactHeader> CreateSIPContactList(SIPURI sipURI)
        {
            List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();
            contactHeaderList.Add(new SIPContactHeader(null, sipURI));

            return contactHeaderList;
        }

        /// <summary>
        /// Compares two contact headers to determine contact address equality.
        /// </summary>
        public static bool AreEqual(SIPContactHeader contact1, SIPContactHeader contact2)
        {
            if (!SIPURI.AreEqual(contact1.ContactURI, contact2.ContactURI))
            {
                return false;
            }
            else
            {
                // Compare invaraiant parameters.
                 string[] contact1Keys = contact1.ContactParameters.GetKeys();

                 if (contact1Keys != null && contact1Keys.Length > 0)
                 {
                     foreach (string key in contact1Keys)
                     {
                         if (key == EXPIRES_PARAMETER_KEY || key == QVALUE_PARAMETER_KEY)
                         {
                             continue;
                         }
                         else if (contact1.ContactParameters.Get(key) != contact2.ContactParameters.Get(key))
                         {
                             return false;
                         }
                     }
                 }

                // Need to do the reverse as well
                 string[] contact2Keys = contact2.ContactParameters.GetKeys();

                 if (contact2Keys != null && contact2Keys.Length > 0)
                 {
                     foreach (string key in contact2Keys)
                     {
                         if (key == EXPIRES_PARAMETER_KEY || key == QVALUE_PARAMETER_KEY)
                         {
                             continue;
                         }
                         else if (contact2.ContactParameters.Get(key) != contact1.ContactParameters.Get(key))
                         {
                             return false;
                         }
                     }
                 }
            }

            return true;
        }
		
		public override string ToString()
		{
            if (m_userField.URI.Host == SIPConstants.SIP_REGISTER_REMOVEALL)
            {
                return SIPConstants.SIP_REGISTER_REMOVEALL;
            }
            else
            {
                //if (m_userField.URI.Protocol == SIPProtocolsEnum.UDP)
                //{
                    return m_userField.ToString();
                //}
                //else
                //{
                //    return m_userField.ToContactString();
                //}
            }
		}

        public SIPContactHeader CopyOf()
        {
            SIPContactHeader copy = new SIPContactHeader();
            copy.RawHeader = RawHeader;
            copy.m_userField = m_userField.CopyOf();

            return copy;
        }
		
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPContactHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}
	
			[Test]
			public void ParseContactHeaderDomainForUserTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testContactHeader = "<sip:sip.domain.com@sip.domain.com>";

				List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:sip.domain.com@sip.domain.com", "The Contact header URI was not parsed correctly.");
			}
	
			[Test]
			public void ParseBadAastraContactHeaderUserTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testContactHeader = "<sip:10001@127.0.0.1:5060\n";

                List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI);

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
			}
			
			[Test]
			public void ParseNoAngleQuotesContactHeaderUserTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testContactHeader = "sip:10001@127.0.0.1:5060";

				List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI);

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
			}

			[Test]
			public void ParseCiscoContactHeaderUserTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testContactHeader = "<sip:user@127.0.0.1:5060;user=phone;transport=udp>;+sip.instance=\"<urn:uuid:00000000-0000-0000-0000-0006d74b0e72>\";+u.sip!model.ccm.cisco.com=\"7\"";

				List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

				Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
				Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060;user=phone;transport=udp", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
                Assert.IsTrue(sipContactHeaderList[0].ContactParameters.ToString() == ";+sip.instance=\"<urn:uuid:00000000-0000-0000-0000-0006d74b0e72>\";+u.sip!model.ccm.cisco.com=\"7\"", "The Contact header Parameters were not parsed correctly.");
			}
		
			[Test]
			public void ParseNoLineBreakContactHeaderUserTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string testContactHeader = "<sip:10001@127.0.0.1:5060\nAllow: OPTIONS";

				Console.WriteLine("Contact Header = " + testContactHeader + ".");

				List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());

				Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:10001@127.0.0.1:5060", "The Contact header URI was not parsed correctly.");
			}

            [Test]
            public void ParseContactWithParamHeaderUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testContactHeader = "<sip:user@127.0.0.1:5060;ftag=1233>";

                List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
                Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060;ftag=1233", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.Parameters.Get("ftag") == "1233", "The Contact header ftag URI parameter was not parsed correctly.");
            }

            [Test]
            public void ParseExpiresContactHeaderUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testContactHeader = "<sip:user@127.0.0.1:5060>; expires=60";

                List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
                Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());
                Console.WriteLine("Contact = " + sipContactHeaderList[0].ToString());

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
                Assert.IsTrue(sipContactHeaderList[0].Expires == 60, "The Contact header Expires parameter was not parsed correctly.");
            }

            [Test]
            public void ParseZeroExpiresContactHeaderUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testContactHeader = "<sip:user@127.0.0.1:5060>; expires=0";

                List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
                Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());
                Console.WriteLine("Contact = " + sipContactHeaderList[0].ToString());

                Assert.IsTrue(sipContactHeaderList[0].ContactName == null, "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:user@127.0.0.1:5060", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
                Assert.IsTrue(sipContactHeaderList[0].Expires == 0, "The Contact header Expires parameter was not parsed correctly.");
            }

            [Test]
            public void MultipleContactsHeaderUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testContactHeader = "\"Mr. Watson\" <sip:watson@worcester.bell-telephone.com>;q=0.7; expires=3600, \"Mr. Watson\" <sip:watson@bell-telephone.com> ;q=0.1";

                List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header Count=" + sipContactHeaderList.Count + ".");
                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
                Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

                Assert.IsTrue(sipContactHeaderList[0].ContactName == "Mr. Watson", "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:watson@worcester.bell-telephone.com", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
                Assert.IsTrue(sipContactHeaderList[0].Expires == 3600, "The Contact header Expires parameter was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].Q == "0.7", "The Contact header Q parameter was not parsed correctly.");
            }

            [Test]
            public void MultipleContactsWithURIParamsHeaderUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testContactHeader = "\"Mr. Watson\" <sip:watson@worcester.bell-telephone.com;ftag=1232>;q=0.7; expires=3600, \"Mr. Watson\" <sip:watson@bell-telephone.com?nonsense=yes> ;q=0.1";

                List<SIPContactHeader> sipContactHeaderList = SIPContactHeader.ParseContactHeader(testContactHeader);

                Console.WriteLine("Contact Header Count=" + sipContactHeaderList.Count + ".");
                Console.WriteLine("Contact Header ContactURI = " + sipContactHeaderList[0].ContactURI.ToString());
                Console.WriteLine("Contact Header ContactParams = " + sipContactHeaderList[0].ContactParameters.ToString());

                Assert.IsTrue(sipContactHeaderList[0].ContactName == "Mr. Watson", "The Contact header name was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.ToString() == "sip:watson@worcester.bell-telephone.com;ftag=1232", "The Contact header URI was not parsed correctly, parsed valued = " + sipContactHeaderList[0].ContactURI.ToString() + ".");
                Assert.IsTrue(sipContactHeaderList[0].Expires == 3600, "The Contact header Expires parameter was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].Q == "0.7", "The Contact header Q parameter was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[0].ContactURI.Parameters.Get("ftag") == "1232", "The Contact header URI ftag parameter was not parsed correctly.");
                Assert.IsTrue(sipContactHeaderList[1].ContactURI.Headers.Get("nonsense") == "yes", "The Contact header URI nonsense header was not parsed correctly.");
            }

            [Test]
            public void SimpleAreEqualUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPContactHeader contactHeader1 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
                SIPContactHeader contactHeader2 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
                
                Assert.IsTrue(AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");    
            }

            [Test]
            public void SimpleNotEqualUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPContactHeader contactHeader1 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.1:5060"));
                SIPContactHeader contactHeader2 = new SIPContactHeader(null, SIPURI.ParseSIPURI("sip:user@127.0.0.2:5060"));

                Assert.IsFalse(AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
            }

            [Test]
            public void WithParametersAreEqualUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;param1=value1"));
                SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;param1=value1"));

                Assert.IsTrue(AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
            }

            [Test]
            public void WithExpiresParametersAreEqualUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060> ;expires=0; param1=value1"));
                SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("<sip:user@127.0.0.1:5060>;expires=50;param1=value1"));

                Assert.IsTrue(AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
            }

            [Test]
            public void WithDifferentNamesAreEqualUserTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPContactHeader contactHeader1 = new SIPContactHeader(SIPUserField.ParseSIPUserField("\"Joe Bloggs\" <sip:user@127.0.0.1:5060> ;expires=0; param1=value1"));
                SIPContactHeader contactHeader2 = new SIPContactHeader(SIPUserField.ParseSIPUserField("\"Jane Doe\" <sip:user@127.0.0.1:5060>;expires=50;param1=value1"));

                Assert.IsTrue(AreEqual(contactHeader1, contactHeader2), "The Contact headers were not correctly identified as equal.");
            }
		}

		#endif

		#endregion
	}

	public class SIPAuthenticationHeader
	{
        public SIPAuthorisationDigest SIPDigest;
		
		//public string Realm;
		//public string Nonce;
		//public string Username;
		//public string URI;
		//public string Response;

		private SIPAuthenticationHeader()
		{
            SIPDigest = new SIPAuthorisationDigest();
		}

        public SIPAuthenticationHeader(SIPAuthorisationDigest sipDigest)
		{
            SIPDigest = sipDigest;
		}

        public SIPAuthenticationHeader(SIPAuthorisationHeadersEnum authorisationType, string realm, string nonce)
		{
            SIPDigest = new SIPAuthorisationDigest(authorisationType);
            SIPDigest.Realm = realm;
            SIPDigest.Nonce = nonce;
		}

        public static SIPAuthenticationHeader ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum authorizationType, string headerValue)
		{
			try
			{
				SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader();
                authHeader.SIPDigest = SIPAuthorisationDigest.ParseAuthorisationDigest(authorizationType, headerValue);
				return authHeader;
			}
			catch
			{
				throw new ApplicationException("Error parsing SIP authentication header request, " + headerValue);
			}
		}

		public override string ToString()
		{
            if (SIPDigest != null)
			{
                string authHeader = null;
                SIPAuthorisationHeadersEnum authorisationHeaderType = (SIPDigest.AuthorisationResponseType != SIPAuthorisationHeadersEnum.Unknown) ? SIPDigest.AuthorisationResponseType : SIPDigest.AuthorisationType;

                if (authorisationHeaderType == SIPAuthorisationHeadersEnum.Authorize)
                {
                    authHeader = SIPHeaders.SIP_HEADER_AUTHORIZATION + ": ";
                }
                else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.ProxyAuthenticate)
                {
                    authHeader = SIPHeaders.SIP_HEADER_PROXYAUTHENTICATION + ": ";
                }
                else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.ProxyAuthorization)
                {
                    authHeader = SIPHeaders.SIP_HEADER_PROXYAUTHORIZATION + ": ";
                }
                else if (authorisationHeaderType == SIPAuthorisationHeadersEnum.WWWAuthenticate)
                {
                    authHeader = SIPHeaders.SIP_HEADER_WWWAUTHENTICATE + ": ";
                }
                else
                {
                    authHeader = SIPHeaders.SIP_HEADER_AUTHORIZATION + ": ";
                }

                return authHeader + SIPDigest.ToString();
			}
			else
			{
				return null;
			}
		}

        #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
        public class SIPAuthenticationHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

            [TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void ParseAuthHeaderUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPAuthenticationHeader authHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthorization, "Digest realm=\"o-fone.com\",nonce=\"mv1keFTRX4yYVsHb/E+rviOflIurIw\",algorithm=MD5,qop=\"auth\",username=\"joe.bloggs\", response=\"1234\",uri=\"sip:o-fone.com\"");

                Console.WriteLine("SIP Auth Header=" + authHeader.ToString() + ".");

                Assert.AreEqual(authHeader.SIPDigest.Realm, "o-fone.com", "The SIP auth header realm was not parsed correctly.");
                Assert.AreEqual(authHeader.SIPDigest.Nonce, "mv1keFTRX4yYVsHb/E+rviOflIurIw", "The SIP auth header nonce was not parsed correctly.");
                Assert.AreEqual(authHeader.SIPDigest.URI, "sip:o-fone.com", "The SIP URI was not parsed correctly.");
                Assert.AreEqual(authHeader.SIPDigest.Username, "joe.bloggs", "The SIP username was not parsed correctly.");
                Assert.AreEqual(authHeader.SIPDigest.Response, "1234", "The SIP response was not parsed correctly.");
			}
        }

        #endif

        #endregion
    }

    /// <summary>
    /// The SIPRoute class is used to represent both Route and Record-Route headers.
    /// </summary>
    /// <bnf>
    /// Route               =  "Route" HCOLON route-param *(COMMA route-param)
    /// route-param         =  name-addr *( SEMI rr-param )
    /// 
    /// Record-Route        =  "Record-Route" HCOLON rec-route *(COMMA rec-route)
    /// rec-route           =  name-addr *( SEMI rr-param )
    /// rr-param            =  generic-param
    ///
    /// name-addr           =  [ display-name ] LAQUOT addr-spec RAQUOT
    /// addr-spec           =  SIP-URI / SIPS-URI / absoluteURI
    /// display-name        =  *(token LWS)/ quoted-string
    /// generic-param       =  token [ EQUAL gen-value ]
    /// gen-value           =  token / host / quoted-string
    /// </bnf>
    /// <remarks>
    /// The Route and Record-Route headers only have parameters, no headers. Parameters of from ...;name=value;name2=value2
    /// There are no specific parameters.
    /// </remarks>
	public class SIPRoute
	{
		private static string m_looseRouterParameter = SIPConstants.SIP_LOOSEROUTER_PARAMETER;

		private static char[] m_angles = new char[]{'<', '>'};
		
        private SIPUserField m_userField;

		public string Host
		{
            get { return m_userField.URI.Host; }
            set { m_userField.URI.Host = value; }
		}
	
		public SIPURI URI
		{
            get { return m_userField.URI; }
		}

		public bool IsStrictRouter
		{
            get { return !m_userField.URI.Parameters.Has(m_looseRouterParameter); }
            set 
             {
                 if (value)
                 {
                     m_userField.URI.Parameters.Remove(m_looseRouterParameter);
                 }
                 else
                 {
                     m_userField.URI.Parameters.Set(m_looseRouterParameter, null);
                 }
             }
		}

		private SIPRoute()
		{}

		public SIPRoute(string host)
		{
            if (host.IsNullOrBlank())
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, "Cannot create a Route from an blank string.");
            } 

            m_userField = SIPUserField.ParseSIPUserField(host);
		}

        public SIPRoute(string host, bool looseRouter)
        {
            if (host.IsNullOrBlank())
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, "Cannot create a Route from an blank string.");
            } 

            m_userField = SIPUserField.ParseSIPUserField(host);
            this.IsStrictRouter = !looseRouter;
        }

		public SIPRoute(SIPURI uri)
		{
            m_userField = new SIPUserField();
            m_userField.URI = uri;
		}

        public SIPRoute(SIPURI uri, bool looseRouter)
        {
            m_userField = new SIPUserField();
            m_userField.URI = uri;
            this.IsStrictRouter = !looseRouter;
        }

		public static SIPRoute ParseSIPRoute(string route)
		{
            if (route.IsNullOrBlank())
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, "Cannot create a Route from an blank string.");
            }

            try
            {
                SIPRoute sipRoute = new SIPRoute();
                sipRoute.m_userField = SIPUserField.ParseSIPUserField(route);

                return sipRoute;
            }
            catch (Exception excp)
            {
                throw new SIPValidationException(SIPValidationFieldsEnum.RouteHeader, excp.Message);
            }
		}

		public override string ToString()
        {
            //if (m_userField.URI.Protocol == SIPProtocolsEnum.UDP)
            //{
                return m_userField.ToString();
            //}
            //else
            //{
                //return m_userField.ToContactString();
            //}
		}

        public SIPEndPoint ToSIPEndPoint() {
            return URI.ToSIPEndPoint();
        }
		
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPRouteHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

            [TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void MissingBracketsRouteTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				SIPRoute newRoute = new SIPRoute("sip:127.0.0.1:5060");

                Console.WriteLine(newRoute.ToString());

                Assert.IsTrue(newRoute.URI.ToString() == "sip:127.0.0.1:5060", "The Route header URI was not correctly parsed.");
			}

			[Test]
			public void ParseRouteTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060;lr>");

				Console.WriteLine("SIP Route=" + route.ToString() + ".");
 
				Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
				Assert.AreEqual(route.ToString(), "<sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
                Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
			}

            [Test]
            public void SetLooseRouteTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060>");
                route.IsStrictRouter = false;

                Console.WriteLine("SIP Route=" + route.ToString() + ".");

                Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
                Assert.AreEqual(route.ToString(), "<sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
                Assert.IsFalse(route.IsStrictRouter, "Route was not correctly settable as a loose router.");
            }

            [Test]
            public void RemoveLooseRouterTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRoute route = SIPRoute.ParseSIPRoute("<sip:127.0.0.1:5060;lr>");
                route.IsStrictRouter = true;

                Console.WriteLine("SIP Route=" + route.ToString() + ".");

                Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
                Assert.AreEqual(route.ToString(), "<sip:127.0.0.1:5060>", "The SIP route string was not correct.");
                Assert.IsTrue(route.IsStrictRouter, "Route was not correctly settable as a strict router.");
            }

            [Test]
            public void ParseRouteWithDisplayNameTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRoute route = SIPRoute.ParseSIPRoute("12345656 <sip:127.0.0.1:5060;lr>");

                Console.WriteLine("SIP Route=" + route.ToString() + ".");
                Console.WriteLine("Route to SIPEndPoint=" + route.ToSIPEndPoint().ToString() + ".");

                Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
                Assert.AreEqual(route.ToString(), "\"12345656\" <sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
                Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
                Assert.AreEqual(route.ToSIPEndPoint().ToString(), "udp:127.0.0.1:5060", "The SIP route did not produce the correct SIP End Point.");
            }

            [Test]
            public void ParseRouteWithDoubleQuotedDisplayNameTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRoute route = SIPRoute.ParseSIPRoute("\"Joe Bloggs\" <sip:127.0.0.1:5060;lr>");

                Console.WriteLine("SIP Route=" + route.ToString() + ".");

                Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
                Assert.AreEqual(route.ToString(), "\"Joe Bloggs\" <sip:127.0.0.1:5060;lr>", "The SIP route string was not correct.");
                Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
            }

            [Test]
            public void ParseRouteWithUserPortionTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string routeStr = "<sip:0033820600000@127.0.0.1:5060;lr;transport=udp>";
                SIPRoute route = SIPRoute.ParseSIPRoute(routeStr);

                Console.WriteLine("SIP Route=" + route.ToString() + ".");
                Console.WriteLine("Route to SIPEndPoint=" + route.ToSIPEndPoint().ToString() + ".");

                Assert.AreEqual(route.Host, "127.0.0.1:5060", "The SIP route host was not parsed correctly.");
                Assert.AreEqual(route.ToString(), routeStr, "The SIP route string was not correct.");
                Assert.IsFalse(route.IsStrictRouter, "Route was not correctly passed as a loose router.");
                Assert.AreEqual(route.ToSIPEndPoint().ToString(), "udp:127.0.0.1:5060", "The SIP route did not produce the correct SIP End Point.");
            }
		}

		#endif

		#endregion
	}	
	
	public class SIPRouteSet
	{
        private static ILog logger = AssemblyState.logger;

		private List<SIPRoute> m_sipRoutes = new List<SIPRoute>();
		
		public int Length
		{
			get{ return m_sipRoutes.Count; }
		}

        public static SIPRouteSet ParseSIPRouteSet(string routeSet)
        {
            SIPRouteSet sipRouteSet = new SIPRouteSet();

            string[] routes = SIPParameters.GetKeyValuePairsFromQuoted(routeSet, ',');
            foreach (string route in routes)
            {
                SIPRoute sipRoute = SIPRoute.ParseSIPRoute(route);
                sipRouteSet.AddBottomRoute(sipRoute);
            }

            return sipRouteSet;
        }

        public SIPRoute GetAt(int index) {
            return m_sipRoutes[index];
        }

        public void SetAt(int index, SIPRoute sipRoute) {
            m_sipRoutes[index] = sipRoute;
        }

		public SIPRoute TopRoute
		{
			get
			{
				if(m_sipRoutes != null && m_sipRoutes.Count > 0)
				{	
					return m_sipRoutes[0];
				}
				else
				{
					return null;
				}
			}
		}

		public SIPRoute BottomRoute
		{
			get
			{
				if(m_sipRoutes != null && m_sipRoutes.Count > 0)
				{	
					return m_sipRoutes[m_sipRoutes.Count-1];
				}
				else
				{
					return null;
				}
			}
		}

		public void PushRoute(SIPRoute route)
		{
			m_sipRoutes.Insert(0, route);
		}

        public void PushRoute(string host)
        {
            m_sipRoutes.Insert(0, new SIPRoute(host, true));
        }

        public void PushRoute(IPEndPoint socket, SIPSchemesEnum scheme, SIPProtocolsEnum protcol)
        {
            m_sipRoutes.Insert(0, new SIPRoute(scheme + ":" + socket.ToString(), true));
        }

		public void AddBottomRoute(SIPRoute route)
		{
			m_sipRoutes.Insert(m_sipRoutes.Count, route);
		}
	
		public SIPRoute PopRoute()
		{
			SIPRoute route = null;
			
			if(m_sipRoutes.Count > 0)
			{
				route = m_sipRoutes[0];
				m_sipRoutes.RemoveAt(0);
			}

			return route;
		}

		public void RemoveBottomRoute()
		{		
			if(m_sipRoutes.Count > 0)
			{
				m_sipRoutes.RemoveAt(m_sipRoutes.Count-1);
			};
		}

        public SIPRouteSet Reversed()
        {
            if (m_sipRoutes != null && m_sipRoutes.Count > 0)
            {
                SIPRouteSet reversedSet = new SIPRouteSet();

                for (int index = 0; index < m_sipRoutes.Count; index++)
                {
                    reversedSet.PushRoute(m_sipRoutes[index]);
                }

                return reversedSet;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// If a route set is travelling from the public side of a proxy to the private side it can be required that the Record-Route set is modified.
        /// </summary>
        /// <param name="origSocket">The socket string in the original route set that needs to be replace.</param>
        /// <param name="replacementSocket">The socket string the original route is being replaced with.</param>
        public void ReplaceRoute(string origSocket, string replacementSocket)
        {
            foreach (SIPRoute route in m_sipRoutes)
            {
                if (route.Host == origSocket)
                {
                    route.Host = replacementSocket;
                }
            }
        }
			
		public new string ToString()
		{
			string routeStr = null;
			
			if(m_sipRoutes != null && m_sipRoutes.Count > 0)
			{
				for(int routeIndex=0; routeIndex<m_sipRoutes.Count; routeIndex++)
				{
					routeStr += (routeStr != null) ? "," + m_sipRoutes[routeIndex].ToString() :  m_sipRoutes[routeIndex].ToString();
				}
			}

			return routeStr;
		}

       #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RouteSetUnitTest
		{
            [Test]
            public void SampleTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            }

            [Test]
            public void ParseSIPRouteSetTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string routeSetString = "<sip:127.0.0.1:5434;lr>,<sip:10.0.0.1>,<sip:192.168.0.1;ftag=12345;lr=on>";
                SIPRouteSet routeSet = ParseSIPRouteSet(routeSetString);

               Console.WriteLine(routeSet.ToString());

                Assert.IsTrue(routeSet.Length == 3, "The parsed route set had an incorrect length.");
                Assert.IsTrue(routeSet.ToString() == routeSetString, "The parsed route set did not produce the same string as the original parsed value.");
                SIPRoute topRoute = routeSet.PopRoute();
                Assert.IsTrue(topRoute.Host == "127.0.0.1:5434", "The first route host was not parsed correctly.");
                Assert.IsFalse(topRoute.IsStrictRouter, "The first route host was not correctly recognised as a loose router.");
                topRoute = routeSet.PopRoute();
                Assert.IsTrue(topRoute.Host == "10.0.0.1", "The second route host was not parsed correctly.");
                Assert.IsTrue(topRoute.IsStrictRouter, "The second route host was not correctly recognised as a strict router.");
                topRoute = routeSet.PopRoute();
                Assert.IsTrue(topRoute.Host == "192.168.0.1", "The third route host was not parsed correctly.");
                Assert.IsFalse(topRoute.IsStrictRouter, "The third route host was not correctly recognised as a loose router.");
                Assert.IsTrue(topRoute.URI.Parameters.Get("ftag") == "12345", "The ftag parameter on the third route was not correctly parsed.");
            }
        }

        #endif

       #endregion
    }

	public class SIPViaSet
	{
		private int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
		private static string m_CRLF = SIPConstants.CRLF;

        private List<SIPViaHeader> m_viaHeaders = new List<SIPViaHeader>();

		public int Length
		{
			get{ return m_viaHeaders.Count; }
		}

        public List<SIPViaHeader> Via
		{
			get
			{
                return m_viaHeaders;
			}
			set
			{
				m_viaHeaders = value;
			}
		}
		
		public SIPViaHeader TopViaHeader
		{
			get
			{
				if(m_viaHeaders != null && m_viaHeaders.Count > 0)
				{	
					return m_viaHeaders[0];
				}
				else
				{
					return null;
				}
			}
		}

        public SIPViaHeader BottomViaHeader
        {
            get
            {
                if (m_viaHeaders != null && m_viaHeaders.Count > 0)
                {
                    return m_viaHeaders[m_viaHeaders.Count-1];
                }
                else
                {
                    return null;
                }
            }
        }

		/// <summary>
		/// Pops top Via header off the array.
		/// </summary>
		public SIPViaHeader PopTopViaHeader()
		{
			SIPViaHeader topHeader = m_viaHeaders[0];
			m_viaHeaders.RemoveAt(0);

			return topHeader;
		}
		
		public void AddBottomViaHeader(SIPViaHeader viaHeader)
		{
			m_viaHeaders.Add(viaHeader);
		}
		
		public void UpateTopViaHeader(IPEndPoint msgRcvdEndPoint)
		{
			// Update the IP Address and port that this request was received on.
			SIPViaHeader topViaHeader = this.TopViaHeader;

            if (msgRcvdEndPoint.Address.ToString() != topViaHeader.Host)
			{
				topViaHeader.ReceivedFromIPAddress = msgRcvdEndPoint.Address.ToString();
			}

			if((topViaHeader.Port == 0 &&	msgRcvdEndPoint.Port != m_defaultSIPPort) ||
                (topViaHeader.Port != 0 && topViaHeader.Port != msgRcvdEndPoint.Port))
			{
				topViaHeader.ReceivedFromPort = msgRcvdEndPoint.Port;
			}
		}

		/// <summary>
		/// Pushes a new Via header onto the top of the array.
		/// </summary>
		public void PushViaHeader(SIPViaHeader viaHeader)
		{
			m_viaHeaders.Insert(0, viaHeader);
		}

		public new string ToString()
		{
			string viaStr = null;
			
			if(m_viaHeaders != null && m_viaHeaders.Count > 0)
			{
				for(int viaIndex=0; viaIndex<m_viaHeaders.Count; viaIndex++)
				{
					viaStr += (m_viaHeaders[viaIndex]).ToString() + m_CRLF;
				}
			}

			return viaStr;
		}

       	#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPViaHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void AdjustReceivedViaHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

                SIPViaSet viaSet = new SIPViaSet();
                viaSet.PushViaHeader(sipViaHeaders[0]);

                viaSet.UpateTopViaHeader(IPSocket.ParseSocketString("88.88.88.88:1234"));

                Assert.IsTrue(viaSet.Length == 1, "Incorrect number of Via headers in set.");
                Assert.IsTrue(viaSet.TopViaHeader.Host == "192.168.1.2", "Top Via Host was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.Port == 5065, "Top Via Port was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.ContactAddress == "192.168.1.2:5065", "Top Via ContactAddress was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromIPAddress == "88.88.88.88", "Top Via received was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromPort == 1234, "Top Via rport was incorrect.");

				Console.WriteLine("---------------------------------------------------");
			}

            [Test]
            public void AdjustReceivedCorrectAlreadyViaHeaderTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string xtenViaHeader = "SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001";

                SIPViaHeader[] sipViaHeaders = SIPViaHeader.ParseSIPViaHeader(xtenViaHeader);

                SIPViaSet viaSet = new SIPViaSet();
                viaSet.PushViaHeader(sipViaHeaders[0]);

                viaSet.UpateTopViaHeader(IPSocket.ParseSocketString("192.168.1.2:5065"));

                Assert.IsTrue(viaSet.Length == 1, "Incorrect number of Via headers in set.");
                Assert.IsTrue(viaSet.TopViaHeader.Host == "192.168.1.2", "Top Via Host was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.Port == 5065, "Top Via Port was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.ContactAddress == "192.168.1.2:5065", "Top Via ContactAddress was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromIPAddress == null, "Top Via received was incorrect.");
                Assert.IsTrue(viaSet.TopViaHeader.ReceivedFromPort == 0, "Top Via rport was incorrect.");

                Console.WriteLine("---------------------------------------------------");
            }
        }

        #endif

        #endregion
    }
	
	/// <bnf>
    /// header  =  "header-name" HCOLON header-value *(COMMA header-value)
	/// field-name: field-value CRLF
	/// </bnf>
	public class SIPHeader
	{
		public const int DEFAULT_CSEQ = 100;

        private static ILog logger = AssemblyState.logger;		
		private static string m_CRLF = SIPConstants.CRLF;
	
		// Core SIP headers.
        public string Accept;
        public SIPAuthenticationHeader AuthenticationHeader;
		public string CallId;
        public List<SIPContactHeader> Contact = new List<SIPContactHeader>();
        public int ContentLength = 0;
        public string ContentType;
        public int CSeq = -1;
		public SIPMethodsEnum CSeqMethod;
        public string Date;
        public int Expires = -1;
		public SIPFromHeader From;
        public int MaxForwards = SIPConstants.DEFAULT_MAX_FORWARDS;
        public string ProxyRequire;
        public string Reason;
        public SIPRouteSet RecordRoutes = new SIPRouteSet();
        public string ReferTo;
        public string ReferredBy;
        public string Require;
        public SIPRouteSet Routes = new SIPRouteSet();
        public string Server;
        public string Supported;
        public string Timestamp;
		public SIPToHeader To;
		public string UserAgent;
        public SIPViaSet Vias = new SIPViaSet();

		// Non-core SIP headers from RFC 3265 SIP Event Package.
		public string Event;
		public string SubscriptionState;

        // Non-core custom SIP headers used to allow a SIP Proxy to communicate network info to internal server agents.
        public string ProxyReceivedOn;
        public string ProxyReceivedFrom;

        public List<string> UnknownHeaders = new List<string>();	// Holds any unrecognised headers.

        public SIPHeader()
		{}

        public SIPHeader(string fromHeader, string toHeader, int cseq, string callId)
        {
            SIPFromHeader from = SIPFromHeader.ParseFromHeader(fromHeader);
            SIPToHeader to = SIPToHeader.ParseToHeader(toHeader);
            Initialise(null, from, to, cseq, callId);
        }

        public SIPHeader(string fromHeader, string toHeader, string contactHeader, int cseq, string callId)
        {
            SIPFromHeader from = SIPFromHeader.ParseFromHeader(fromHeader);
            SIPToHeader to = SIPToHeader.ParseToHeader(toHeader);
            List<SIPContactHeader> contact = SIPContactHeader.ParseContactHeader(contactHeader);
            Initialise(contact, from, to, cseq, callId);
        }

        public SIPHeader(SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            Initialise(null, from, to, cseq, callId);
        }

        public SIPHeader(SIPContactHeader contact, SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            List<SIPContactHeader> contactList = new List<SIPContactHeader>();
            if (contact != null)
            {
                contactList.Add(contact);
            }

            Initialise(contactList, from, to, cseq, callId);
        }	

		public SIPHeader(List<SIPContactHeader> contactList, SIPFromHeader from, SIPToHeader to, int cseq, string callId)
		{
            Initialise(contactList, from, to, cseq, callId);
		}

        private void Initialise(List<SIPContactHeader> contact, SIPFromHeader from, SIPToHeader to, int cseq, string callId)
        {
            if (from == null)
            {
                throw new ApplicationException("The From header cannot be empty when creating a new SIP header.");
            }

            if (to == null)
            {
                throw new ApplicationException("The To header cannot be empty when creating a new SIP header.");
            }

            if (callId == null || callId.Trim().Length == 0)
            {
                throw new ApplicationException("The CallId header cannot be empty when creating a new SIP header.");
            }

            From = from;
            To = to;
            Contact = contact;
            CallId = callId;

            if (cseq > 0 && cseq < Int32.MaxValue)
            {
                CSeq = cseq;
            }
            else
            {
                CSeq = DEFAULT_CSEQ;
            }
        }
		
		public static string[] SplitHeaders(string message)
		{
			// SIP headers can be extended across lines if the first character of the next line is at least on whitespace character.
			message = Regex.Replace(message, m_CRLF + @"\s+", " ", RegexOptions.Singleline);

			// Some user agents couldn't get the \r\n bit right.
			message = Regex.Replace(message, "\r ",  m_CRLF, RegexOptions.Singleline);
			
			return Regex.Split(message, m_CRLF);
		}

		public static SIPHeader ParseSIPHeaders(string[] headersCollection)
		{
			try
			{
				SIPHeader sipHeader = new SIPHeader();
				sipHeader.MaxForwards = -1;		// This allows detection of whether this header is present or not.
				string lastHeader = null;
						
				for(int lineIndex = 0; lineIndex<headersCollection.Length; lineIndex++)
				{
					string headerLine = headersCollection[lineIndex];

					if(headerLine.IsNullOrBlank())
					{
						// No point processing blank headers.
						continue;
					}
					
					string headerName = null;
					string headerValue = null;
					
					// If the first character of a line is whitespace it's a contiuation of the previous line.
					if(headerLine.StartsWith(" "))
					{
						headerName = lastHeader;
						headerValue = headerLine.Trim();
					}
					else
					{
                        headerLine = headerLine.Trim();
                        int delimiterIndex = headerLine.IndexOf(SIPConstants.HEADER_DELIMITER_CHAR);

                        if (delimiterIndex == -1)
						{
							logger.Warn("Invalid SIP header, ignoring, " + headerLine + ".");
							continue;
						}

                        headerName = headerLine.Substring(0, delimiterIndex).Trim();
                        headerValue = headerLine.Substring(delimiterIndex + 1).Trim();
					}

					try
					{
                        string headerNameLower = headerName.ToLower();

                        #region Via
                        if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_VIA.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_HEADER_VIA.ToLower())
                        {
                            //sipHeader.RawVia += headerValue;

                            SIPViaHeader[] viaHeaders = SIPViaHeader.ParseSIPViaHeader(headerValue);

                            if (viaHeaders != null && viaHeaders.Length > 0)
                            {
                                foreach (SIPViaHeader viaHeader in viaHeaders)
                                {
                                    sipHeader.Vias.AddBottomViaHeader(viaHeader);
                                }
                            }
                        }
                        #endregion
                        #region CallId
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CALLID.ToLower() ||
                                headerNameLower == SIPHeaders.SIP_HEADER_CALLID.ToLower())
                        {
                            sipHeader.CallId = headerValue;
                        }
                        #endregion
                        #region CSeq
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_CSEQ.ToLower())
                        {
                            //sipHeader.RawCSeq += headerValue;

                            string[] cseqFields = headerValue.Split(' ');
                            if (cseqFields == null || cseqFields.Length == 0)
                            {
                                logger.Warn("The " + SIPHeaders.SIP_HEADER_CSEQ + " was empty.");
                            }
                            else
                            {
                                if (!Int32.TryParse(cseqFields[0], out sipHeader.CSeq))
                                {
                                    logger.Warn(SIPHeaders.SIP_HEADER_CSEQ + " did not contain a valid integer, " + headerLine + ".");
                                }

                                if (cseqFields != null && cseqFields.Length > 1)
                                {
                                    sipHeader.CSeqMethod = SIPMethods.GetMethod(cseqFields[1]);
                                }
                                else
                                {
                                    logger.Warn("There was no " + SIPHeaders.SIP_HEADER_CSEQ + " method, " + headerLine + ".");
                                }
                            }
                        }
                        #endregion
                        #region Expires
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_EXPIRES.ToLower())
                        {
                            //sipHeader.RawExpires += headerValue;

                            if (!Int32.TryParse(headerValue, out sipHeader.Expires))
                            {
                                logger.Warn("The Expires value was not a valid integer, " + headerLine + ".");
                            }
                        }
                        #endregion
                        #region Contact
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CONTACT.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_HEADER_CONTACT.ToLower())
                        {
                            //sipHeader.RawContact += headerValue;
                            sipHeader.Contact = SIPContactHeader.ParseContactHeader(headerValue);
                        }
                        #endregion
                        #region From
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_FROM.ToLower() ||
                             headerNameLower == SIPHeaders.SIP_HEADER_FROM.ToLower())
                        {
                            //sipHeader.RawFrom = headerValue;
                            sipHeader.From = SIPFromHeader.ParseFromHeader(headerValue);
                        }
                        #endregion
                        #region To
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_TO.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_HEADER_TO.ToLower())
                        {
                            //sipHeader.RawTo = headerValue;
                            sipHeader.To = SIPToHeader.ParseToHeader(headerValue);
                        }
                        #endregion
                        #region WWWAuthenticate
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_WWWAUTHENTICATE.ToLower())
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, headerValue);
                        }
                        #endregion
                        #region Authorization
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_AUTHORIZATION.ToLower())
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.Authorize, headerValue);
                        }
                        #endregion
                        #region ProxyAuthentication
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXYAUTHENTICATION.ToLower())
                        {
                            //sipHeader.RawAuthentication = headerValue;
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthenticate, headerValue);
                        }
                        #endregion
                        #region ProxyAuthorization
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXYAUTHORIZATION.ToLower())
                        {
                            sipHeader.AuthenticationHeader = SIPAuthenticationHeader.ParseSIPAuthenticationHeader(SIPAuthorisationHeadersEnum.ProxyAuthorization, headerValue);
                        }
                        #endregion
                        #region UserAgent
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_USERAGENT.ToLower())
                        {
                            sipHeader.UserAgent = headerValue;
                        }
                        #endregion
                        #region MaxForwards
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_MAXFORWARDS.ToLower())
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.MaxForwards))
                            {
                                logger.Warn("The " + SIPHeaders.SIP_HEADER_MAXFORWARDS + " could not be parsed as a valid integer, " + headerLine + ".");
                            }
                        }
                        #endregion
                        #region ContentLength
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_HEADER_CONTENTLENGTH.ToLower())
                        {
                            if (!Int32.TryParse(headerValue, out sipHeader.ContentLength))
                            {
                                logger.Warn("The " + SIPHeaders.SIP_HEADER_CONTENTLENGTH + " could not be parsed as a valid integer.");
                            }
                        }
                        #endregion
                        #region ContentType
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_CONTENTTYPE.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_HEADER_CONTENTTYPE.ToLower())
                        {
                            sipHeader.ContentType = headerValue;
                        }
                        #endregion
                        #region Accept
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ACCEPT.ToLower())
                        {
                            sipHeader.Accept = headerValue;
                        }
                        #endregion
                        #region Route
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_ROUTE.ToLower())
                        {
                            SIPRouteSet routeSet = SIPRouteSet.ParseSIPRouteSet(headerValue);
                            if (routeSet != null)
                            {
                                while (routeSet.Length > 0)
                                {
                                    sipHeader.Routes.AddBottomRoute(routeSet.PopRoute());
                                }
                            }
                        }
                        #endregion
                        #region RecordRoute
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_RECORDROUTE.ToLower())
                        {
                            SIPRouteSet recordRouteSet = SIPRouteSet.ParseSIPRouteSet(headerValue);
                            if (recordRouteSet != null)
                            {
                                while (recordRouteSet.Length > 0)
                                {
                                    sipHeader.RecordRoutes.AddBottomRoute(recordRouteSet.PopRoute());
                                }
                            }
                        }
                        #endregion
                        #region Event.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_EVENT.ToLower())
                        {
                            sipHeader.Event = headerValue;
                        }
                        #endregion
                        #region SubscriptionState.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_SUBSCRIPTIONSTATE.ToLower())
                        {
                            sipHeader.SubscriptionState = headerValue;
                        }
                        #endregion
                        #region Timestamp.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_TIMESTAMP.ToLower())
                        {
                            sipHeader.Timestamp = headerValue;
                        }
                        #endregion
                        #region Date.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_DATE.ToLower())
                        {
                            sipHeader.Date = headerValue;
                        }
                        #endregion
                        #region Refer-To.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REFERTO.ToLower())
                        {
                            sipHeader.ReferTo = headerValue;
                        }
                        #endregion
                        #region Referred-By.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REFERREDBY.ToLower())
                        {
                            sipHeader.ReferredBy = headerValue;
                        }
                        #endregion
                        #region Require.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REQUIRE.ToLower())
                        {
                            sipHeader.Require = headerValue;
                        }
                        #endregion
                        #region Reason.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_REASON.ToLower())
                        {
                            sipHeader.Reason = headerValue;
                        }
                        #endregion
                        #region Proxy-ReceivedFrom.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXY_RECEIVEDFROM.ToLower()) {
                            sipHeader.ProxyReceivedFrom = headerValue;
                        }
                        #endregion
                        #region Proxy-ReceivedOn.
                        else if (headerNameLower == SIPHeaders.SIP_HEADER_PROXY_RECEIVEDON.ToLower()) {
                            sipHeader.ProxyReceivedOn = headerValue;
                        }
                        #endregion
                        #region Supported
                        else if (headerNameLower == SIPHeaders.SIP_COMPACTHEADER_SUPPORTED.ToLower() ||
                            headerNameLower == SIPHeaders.SIP_HEADER_SUPPORTED.ToLower()) {
                            sipHeader.Supported = headerValue;
                        }
                        #endregion

                        else
                        {
                            sipHeader.UnknownHeaders.Add(headerLine);
                        }

						lastHeader = headerName;
					}
					catch(Exception parseExcp)
					{
						logger.Error("Error parsing SIP header " + headerLine + ". " + parseExcp.Message);
						throw new SIPValidationException(SIPValidationFieldsEnum.Headers, "Unknown error parsing Header.");
					}
				}

                sipHeader.Validate();

				return sipHeader;
			}
			catch(SIPValidationException)
			{
				throw;
			}
			catch(Exception excp)
			{
				logger.Error("Exception ParseSIPHeaders. " + excp.Message);
				throw new SIPValidationException(SIPValidationFieldsEnum.Headers, "Unknown error parsing Headers.");
			}
		}

		/// <summary>
		/// Puts the SIP headers together into a string ready for transmission.
		/// </summary>
		/// <returns>String representing the SIP headers.</returns>
		public new string ToString()
		{
			try
			{
				StringBuilder headersBuilder = new StringBuilder();

				headersBuilder.Append(Vias.ToString());

				string cseqField = null;
				if(this.CSeq != 0)
				{
					cseqField = (this.CSeqMethod != SIPMethodsEnum.NONE) ? this.CSeq + " " + this.CSeqMethod.ToString() : this.CSeq.ToString();
				}

				headersBuilder.Append( (To != null) ? SIPHeaders.SIP_HEADER_TO + ": " + this.To.ToString() + m_CRLF : null );
				headersBuilder.Append( (From != null) ? SIPHeaders.SIP_HEADER_FROM + ": " + this.From.ToString()  + m_CRLF : null );
				headersBuilder.Append( (CallId != null) ? SIPHeaders.SIP_HEADER_CALLID + ": " + this.CallId + m_CRLF : null );
				headersBuilder.Append( (CSeq > 0) ? SIPHeaders.SIP_HEADER_CSEQ + ": " + cseqField  + m_CRLF : null );

                #region Appending Contact header.

                if (Contact != null && Contact.Count == 1)
                {
                     headersBuilder.Append(SIPHeaders.SIP_HEADER_CONTACT + ": " + Contact[0].ToString() + m_CRLF);
                }
                else if (Contact != null && Contact.Count > 1)
                {
                    StringBuilder contactsBuilder = new StringBuilder();
                    contactsBuilder.Append(SIPHeaders.SIP_HEADER_CONTACT + ": ");

                    bool firstContact = true;
                    foreach (SIPContactHeader contactHeader in Contact)
                    {
                        if (firstContact)
                        {
                            contactsBuilder.Append(contactHeader.ToString());
                        }
                        else
                        {
                            contactsBuilder.Append("," + contactHeader.ToString());
                        }

                        firstContact = false;
                    }

                    headersBuilder.Append(contactsBuilder.ToString() + m_CRLF);
                }

                #endregion

                headersBuilder.Append( (MaxForwards >= 0) ? SIPHeaders.SIP_HEADER_MAXFORWARDS + ": " + this.MaxForwards + m_CRLF : null );
				headersBuilder.Append( (Routes != null && Routes.Length > 0) ? SIPHeaders.SIP_HEADER_ROUTE + ": " + Routes.ToString() + m_CRLF : null);
				headersBuilder.Append( (RecordRoutes != null && RecordRoutes.Length > 0) ? SIPHeaders.SIP_HEADER_RECORDROUTE + ": " + RecordRoutes.ToString() + m_CRLF : null);
				headersBuilder.Append( (UserAgent != null && UserAgent.Trim().Length != 0) ? SIPHeaders.SIP_HEADER_USERAGENT + ": " + this.UserAgent + m_CRLF : null );
				headersBuilder.Append( (Expires != -1) ? SIPHeaders.SIP_HEADER_EXPIRES + ": " + this.Expires + m_CRLF : null );
				headersBuilder.Append( (Accept != null) ? SIPHeaders.SIP_HEADER_ACCEPT + ": " + this.Accept + m_CRLF : null);
                headersBuilder.Append( (AuthenticationHeader != null) ? AuthenticationHeader.ToString() + m_CRLF : null);
                headersBuilder.Append( (Server != null && Server.Trim().Length != 0) ? SIPHeaders.SIP_HEADER_SERVER + ": " + this.Server + m_CRLF : null);
                headersBuilder.Append(SIPHeaders.SIP_HEADER_CONTENTLENGTH + ": " + this.ContentLength + m_CRLF);
                if (this.ContentType != null && this.ContentType.Trim().Length > 0)
                {
                    headersBuilder.Append(SIPHeaders.SIP_HEADER_CONTENTTYPE + ": " + this.ContentType + m_CRLF);
                }

				// Non-core SIP headers.
				headersBuilder.Append( (Event != null) ? SIPHeaders.SIP_HEADER_EVENT + ": " + Event + m_CRLF : null );
				headersBuilder.Append( (SubscriptionState != null) ? SIPHeaders.SIP_HEADER_SUBSCRIPTIONSTATE + ": " + SubscriptionState + m_CRLF : null );
                headersBuilder.Append( (Date != null) ? SIPHeaders.SIP_HEADER_DATE + ": " + Date + m_CRLF : null);
                headersBuilder.Append( (Timestamp != null) ? SIPHeaders.SIP_HEADER_TIMESTAMP + ": " + Timestamp + m_CRLF : null);
                headersBuilder.Append( (ReferTo != null) ? SIPHeaders.SIP_HEADER_REFERTO + ": " + ReferTo + m_CRLF : null);
                headersBuilder.Append( (ReferredBy != null) ? SIPHeaders.SIP_HEADER_REFERREDBY + ": " + ReferredBy + m_CRLF : null);
                headersBuilder.Append( (Require != null) ? SIPHeaders.SIP_HEADER_REQUIRE + ": " + Require + m_CRLF : null);
                headersBuilder.Append( (Reason != null) ? SIPHeaders.SIP_HEADER_REASON + ": " + Reason + m_CRLF : null);
                headersBuilder.Append( (Supported != null) ? SIPHeaders.SIP_HEADER_SUPPORTED + ": " + Supported + m_CRLF : null);
                headersBuilder.Append( (ProxyReceivedFrom != null) ? SIPHeaders.SIP_HEADER_PROXY_RECEIVEDFROM + ": " + ProxyReceivedFrom + m_CRLF : null);
                headersBuilder.Append( (ProxyReceivedOn != null) ? SIPHeaders.SIP_HEADER_PROXY_RECEIVEDON + ": " + ProxyReceivedOn + m_CRLF : null);

				// Unknown SIP headers
				foreach(string unknownHeader in UnknownHeaders)
				{
					headersBuilder.Append(unknownHeader + m_CRLF);
				}
				
				return headersBuilder.ToString();
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPHeader ToString. " + excp.Message);
				throw excp;
			}
		}

        private void Validate()
		{
            if(Vias == null || Vias.Length == 0)
			{
                throw new SIPValidationException(SIPValidationFieldsEnum.ViaHeader, "Invalid header, no Via.");
			}
		}
       
        public void SetDateHeader()
        {
            Date = DateTime.Now.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss") + " GMT";
        }

        public SIPHeader Copy()
        {
            string headerString = this.ToString();
            string[] sipHeaders = SIPHeader.SplitHeaders(headerString);
            return ParseSIPHeaders(sipHeaders);
        }

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
                log4net.Config.BasicConfigurator.Configure();
            }

			[TestFixtureTearDown]
			public void Dispose()
			{}
	
			[Test]
			public void ParseXTenHeadersTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string xtenInviteHeaders = 
					"Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
					"From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
					"To: <sip:303@sip.blueface.ie>" + m_CRLF +
					"Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
					"Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
					"CSeq: 49429 INVITE" + m_CRLF +
					"Max-Forwards: 70" + m_CRLF +
					"Content-Type: application/sdp" + m_CRLF +
					"User-Agent: X-PRO release 1103v" + m_CRLF +
					"Content-Length: 271" + m_CRLF;

				Console.WriteLine("Original SIP Headers:\n" + xtenInviteHeaders);

				string[] headersCollection = Regex.Split(xtenInviteHeaders, "\r\n");

				SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

				Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());

                Assert.IsTrue("Via: SIP/2.0/UDP 192.168.1.2:5065;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" == sipHeader.Vias.TopViaHeader.ToString(), "The Via header was not parsed correctly," + sipHeader.Vias.TopViaHeader.ToString() + ".");
				Assert.IsTrue("SER Test X" == sipHeader.From.FromName, "The From Name value was not parsed correctly, " + sipHeader.From.FromName + ".");
				Assert.IsTrue("sip:aaronxten@sip.blueface.ie:5065" == sipHeader.From.FromURI.ToString(), "The From URI value was not parsed correctly, " + sipHeader.From.FromURI + ".");
				Assert.IsTrue("196468136" == sipHeader.From.FromTag, "The From tag value was not parsed correctly, " + sipHeader.From.FromTag + ".");
				Assert.IsTrue(null == sipHeader.To.ToName, "The To Name value was not parsed correctly, " + sipHeader.To.ToName + ".");
				Assert.IsTrue("sip:303@sip.blueface.ie" == sipHeader.To.ToURI.ToString(), "The To URI value was not parsed correctly, " + sipHeader.To.ToURI + ".");
				Assert.IsTrue(null == sipHeader.To.ToTag, "The To tag value was not parsed correctly, " + sipHeader.To.ToTag + ".");
				Assert.IsTrue("A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" == sipHeader.CallId, "The Call ID values was not parsed correctly, " + sipHeader.CallId + ".");
				Assert.IsTrue(49429 == sipHeader.CSeq, "The CSeq value was not parsed correctly, " + sipHeader.CSeq + ".");
				Assert.IsTrue(SIPMethodsEnum.INVITE == sipHeader.CSeqMethod, "The CSeq Method value was not parsed correctly, " + sipHeader.CSeqMethod + ".");
				Assert.IsTrue(70 == sipHeader.MaxForwards, "The MaxForwards value was not parsed correctly, " + sipHeader.MaxForwards + ".");
				Assert.IsTrue("X-PRO release 1103v" == sipHeader.UserAgent, "The UserAgent value was not parsed correctly, " + sipHeader.UserAgent + ".");
				Assert.IsTrue("application/sdp" == sipHeader.ContentType, "The ContentType value was not parsed correctly, " + sipHeader.ContentType + ".");
				Assert.IsTrue(271 == sipHeader.ContentLength, "The ContentLength value was not parsed correctly, " + sipHeader.ContentLength + ".");

				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
			public void ParseAsteriskRecordRouteHeadersTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string xtenInviteHeaders = 
					"Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bK8Z4EIWBeY45fRGwC0qIeu/xpw3A=" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64091;branch=z9hG4bK4E0728C26A0640E7830D7C9179D08D67" + m_CRLF +
					"Record-Route: <sip:213.168.225.133:5060;lr>,<sip:220.240.255.198:64091;lr>" + m_CRLF +
					"From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=457825353" + m_CRLF +
					"To: <sip:303@bluesipd>;tag=as02a64a42" + m_CRLF +
					"Call-ID: 8A702FA2-18F0-4DFC-AED5-C1A883EADB84@192.168.1.2" + m_CRLF +
					"CSeq: 38002 INVITE" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Content-Type: application/sdp" + m_CRLF +
					"Content-Length: 350" + m_CRLF;
				Console.WriteLine("Original SIP Headers:\n" + xtenInviteHeaders);

				string[] headersCollection = Regex.Split(xtenInviteHeaders, "\r\n");

				SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

				Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());
			
				SIPRoute topRoute = sipHeader.RecordRoutes.PopRoute();
				Assert.IsTrue(topRoute.Host == "213.168.225.133:5060", "The top record route was not parsed correctly.");

				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
			public void ParseAMulitLineHeaderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string mulitLineHeader = 
					"Via: SIP/2.0/UDP 213.168.225.135:5060;branch=z9hG4bK8Z4EIWBeY45fRGwC0qIeu/xpw3A=" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5065;received=220.240.255.198:64091;branch=z9hG4bK4E0728C26A0640E7830D7C9179D08D67" + m_CRLF +
					"Record-Route: <sip:213.168.225.133:5060;lr>," + m_CRLF +
					" <sip:220.240.255.198:64091;lr>" + m_CRLF +
					"From: bluesipd <sip:bluesipd@bluesipd:5065>;tag=457825353" + m_CRLF +
					"To: <sip:303@bluesipd>;tag=as02a64a42" + m_CRLF +
					"Call-ID: 8A702FA2-18F0-4DFC-AED5-C1A883EADB84@192.168.1.2" + m_CRLF +
					"CSeq: 38002 INVITE" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Content-Type: application/sdp" + m_CRLF +
					"Content-Length: 350" + m_CRLF;

				Console.WriteLine("Original SIP Headers:\n" + mulitLineHeader);

				string[] headersCollection = SIPHeader.SplitHeaders(mulitLineHeader);
				foreach(string headerStr in headersCollection)
				{
					Console.WriteLine("Header => " + headerStr + ".");
				}

				Assert.IsTrue(headersCollection.Length == 12, "The headers were not split properly.");

				Console.WriteLine();

				SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

				Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());

				Assert.IsTrue(sipHeader.RecordRoutes.Length == 2, "An incorrect number of record route entries was extracted, number was " + sipHeader.RecordRoutes.Length + ".");

				SIPRoute topRoute = sipHeader.RecordRoutes.PopRoute();
				Assert.IsTrue(topRoute.Host == "213.168.225.133:5060", "The top record route was not parsed correctly.");
				
				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
			public void ParseAuthenticationRequiredHeadersTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string authReqdHeaders = 
					"SIP/2.0 407 Proxy Authentication Required" + m_CRLF +
					"Via: SIP/2.0/UDP 192.168.1.2:5066;received=220.240.255.198:64066;branch=65cacee9-25b6-405c-8f82-e40427438af7" + m_CRLF +
					"From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
					"To: <sip:303@sip.blueface.ie>;tag=as67b6416e" + m_CRLF +
					"Contact: <sip:303@213.168.225.133>" + m_CRLF +
					"Call-ID: 5bcb927f-9571-47d0-a2a1-36226bcf7665@192.168.1.2" + m_CRLF +
					"CSeq: 908 INVITE" + m_CRLF +
					"Max-Forwards: 70" + m_CRLF +
					"User-Agent: asterisk" + m_CRLF +
					"Proxy-Authenticate: Digest realm=\"asterisk\", nonce=\"15aeff81\"" + m_CRLF +
					"Record-Route: <sip:213.168.225.135:5060;lr>" + m_CRLF +
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY" + m_CRLF + m_CRLF;
				Console.WriteLine("Original SIP Headers:\n" + authReqdHeaders);

				string[] headersCollection = Regex.Split(authReqdHeaders, "\r\n");

				SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

				Console.WriteLine("Parsed SIP Headers:\n" + sipHeader.ToString());
			
				Console.WriteLine("---------------------------------------------------");
			}

			[Test]
            [ExpectedException(typeof(SIPValidationException))]
			public void ParseNoViaHeadersUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				string noViaHeaders = 
					"SIP/2.0 407 Proxy Authentication Required" + m_CRLF + 
					"From: dev <sip:aarondev@84.203.144.70>;tag=0013c339acec050c0635cf7b-48c41caf" + m_CRLF + 
					"To: <sip:303@84.203.144.70>;tag=as019f14fe" + m_CRLF + 
					"Call-ID: 0013c339-acec0011-7181eff5-7cfa0e24@89.100.92.186" + m_CRLF + 
					"CSeq: 101 INVITE" + m_CRLF + 
					"User-Agent: asterisk" + m_CRLF + 
					"Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, SUBSCRIBE, NOTIFY" + m_CRLF + 
					"Contact: <sip:303@213.168.225.133:5061>" + m_CRLF + 
					"Proxy-Authenticate: Digest algorithm=MD5, realm=\"sip.blueface.ie\", nonce=\"789f00ab\"" + m_CRLF + 
					"Content-Length: 0" + m_CRLF + m_CRLF;

				Console.WriteLine("Original SIP Headers:\n" + noViaHeaders);

				string[] headersCollection = Regex.Split(noViaHeaders, "\r\n");

				SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

				Console.WriteLine("---------------------------------------------------");
			}

            [Test]
            public void LowerCaseExpiresUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipMsg =
                    "Via: SIP/2.0/UDP 192.168.1.32:10254;branch=z9hG4bK-d87543-eb7c9f44883c5955-1--d87543-;rport;received=89.100.104.191" + m_CRLF +
                    "To: aaronxten <sip:aaronxten@213.200.94.181>" + m_CRLF +
                    "From: aaronxten <sip:aaronxten@213.200.94.181>;tag=774d2561" + m_CRLF +
                    "Call-ID: MTBhNGZjZmQ2OTc3MWU5MTZjNWUxMDYxOTk1MjdmYzk." + m_CRLF +
                    "CSeq: 2 REGISTER" + m_CRLF +
                    "Contact: <sip:aaronxten@192.168.1.32:10254;rinstance=6d2bbd8014ca7a76>;expires=0" + m_CRLF +
                    "Max-Forwards: 69" + m_CRLF +
                    "expires: 60" + m_CRLF +
                    "User-Agent: X-Lite release 1006e stamp 34025" + m_CRLF +
                    "Allow: INVITE, ACK, CANCEL, OPTIONS, BYE, REFER, NOTIFY, MESSAGE, SUBSCRIBE, INFO" + m_CRLF + m_CRLF;

                Console.WriteLine("Original SIP Headers:\n" + sipMsg);

                string[] headersCollection = Regex.Split(sipMsg, "\r\n");

				SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

                Assert.IsTrue(sipHeader.Expires == 60, "The expires values was parsed incorrectly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void HuaweiRegisterUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipMsg =
                    "From: <sip:10000579@200.170.136.196>;tag=0477183750" + m_CRLF +
                    "To: <sip:10000579@200.170.136.196>;tag=414dedfe" + m_CRLF +
                    "CSeq: 1 REGISTER" + m_CRLF +
                    "Call-ID: 438676792abe47328fc557da2d84d0ee" + m_CRLF +
                    "Via: SIP/2.0/UDP 192.168.1.102:7246;branch=z9hG4bK92460620adf84edab2341899a3453f79;received=124.168.235.200;rport=10552" + m_CRLF +
                    "Server: Huawei SoftX3000 R006B03D" + m_CRLF +
                    "WWW-Authenticate: Digest realm=\"huawei\"," + m_CRLF +
                    " nonce=\"248e4b4457f252ae53c859bfe03c4f93\",domain=\"sip:huawei.com\"," + m_CRLF +
                    " stale=false,algorithm=MD5" + m_CRLF +
                    "Content-Length: 0" + m_CRLF + m_CRLF;

                Console.WriteLine("Original SIP Headers:\n" + sipMsg);

                string[] headersCollection = SplitHeaders(sipMsg);

                SIPHeader sipHeader = SIPHeader.ParseSIPHeaders(headersCollection);

                Console.WriteLine(sipHeader.ToString());
                Console.WriteLine(sipHeader.AuthenticationHeader.ToString());

                Assert.IsTrue(Regex.Match(sipHeader.AuthenticationHeader.ToString(), "nonce").Success, "The WWW-Authenticate header was not correctly parsed across multpiple lines.");

                Console.WriteLine("-----------------------------------------");
            }
		}

		#endif

		#endregion
	}
}
