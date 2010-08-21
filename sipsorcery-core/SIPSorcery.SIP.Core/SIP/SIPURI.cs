//-----------------------------------------------------------------------------
// Filename: SIPURI.cs
//
// Description: SIP URI.
//
// History:
// 09 Apr 2006	Aaron Clauson	Created.
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
using System.Collections.Specialized;
using System.Net;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Implements the the absoluteURI structure from the SIP RFC (incomplete as at 17 nov 2006, AC).
    /// </summary>
    /// <bnf>
    /// absoluteURI    =  scheme ":" ( hier-part / opaque-part )
    /// hier-part      =  ( net-path / abs-path ) [ "?" query ]
    /// net-path       =  "//" authority [ abs-path ]
    /// abs-path       =  "/" path-segments
    ///
    /// opaque-part    =  uric-no-slash *uric
    /// uric           =  reserved / unreserved / escaped
    /// uric-no-slash  =  unreserved / escaped / ";" / "?" / ":" / "@" / "&" / "=" / "+" / "$" / ","
    /// path-segments  =  segment *( "/" segment )
    /// segment        =  *pchar *( ";" param )
    /// param          =  *pchar
    /// pchar          =  unreserved / escaped / ":" / "@" / "&" / "=" / "+" / "$" / ","
    /// scheme         =  ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
    /// authority      =  srvr / reg-name
    /// srvr           =  [ [ userinfo "@" ] hostport ]
    /// reg-name       =  1*( unreserved / escaped / "$" / "," / ";" / ":" / "@" / "&" / "=" / "+" )
    /// query          =  *uric
    ///
    /// SIP-URI          =  "sip:" [ userinfo ] hostport uri-parameters [ headers ]
    /// SIPS-URI         =  "sips:" [ userinfo ] hostport uri-parameters [ headers ]
    /// userinfo         =  ( user / telephone-subscriber ) [ ":" password ] "@"
    /// user             =  1*( unreserved / escaped / user-unreserved )
    /// user-unreserved  =  "&" / "=" / "+" / "$" / "," / ";" / "?" / "/"
    /// password         =  *( unreserved / escaped / "&" / "=" / "+" / "$" / "," )
    /// hostport         =  host [ ":" port ]
    /// host             =  hostname / IPv4address / IPv6reference
    /// hostname         =  *( domainlabel "." ) toplabel [ "." ]
    /// domainlabel      =  alphanum / alphanum *( alphanum / "-" ) alphanum
    /// toplabel         =  ALPHA / ALPHA *( alphanum / "-" ) alphanum
    /// IPv4address    =  1*3DIGIT "." 1*3DIGIT "." 1*3DIGIT "." 1*3DIGIT
    /// IPv6reference  =  "[" IPv6address "]"
    /// IPv6address    =  hexpart [ ":" IPv4address ]
    /// hexpart        =  hexseq / hexseq "::" [ hexseq ] / "::" [ hexseq ]
    /// hexseq         =  hex4 *( ":" hex4)
    /// hex4           =  1*4HEXDIG
    /// port           =  1*DIGIT
    ///
    /// The BNF for telephone-subscriber can be found in RFC 2806 [9].  Note,
    /// however, that any characters allowed there that are not allowed in
    /// the user part of the SIP URI MUST be escaped.
    /// 
    /// uri-parameters    =  *( ";" uri-parameter)
    /// uri-parameter     =  transport-param / user-param / method-param / ttl-param / maddr-param / lr-param / other-param
    /// transport-param   =  "transport=" ( "udp" / "tcp" / "sctp" / "tls" / other-transport)
    /// other-transport   =  token
    /// user-param        =  "user=" ( "phone" / "ip" / other-user)
    /// other-user        =  token
    /// method-param      =  "method=" Method
    /// ttl-param         =  "ttl=" ttl
    /// maddr-param       =  "maddr=" host
    /// lr-param          =  "lr"
    /// other-param       =  pname [ "=" pvalue ]
    /// pname             =  1*paramchar
    /// pvalue            =  1*paramchar
    /// paramchar         =  param-unreserved / unreserved / escaped
    /// param-unreserved  =  "[" / "]" / "/" / ":" / "&" / "+" / "$"
    ///
    /// headers         =  "?" header *( "&" header )
    /// header          =  hname "=" hvalue
    /// hname           =  1*( hnv-unreserved / unreserved / escaped )
    /// hvalue          =  *( hnv-unreserved / unreserved / escaped )
    /// hnv-unreserved  =  "[" / "]" / "/" / "?" / ":" / "+" / "$"
    /// </bnf>
    /// <remarks>
    /// Specific parameters for URIs: transport, maddr, ttl, user, method, lr.
    /// </remarks>
    [DataContract]
    public class SIPURI
    {
        public const int DNS_RESOLUTION_TIMEOUT = 2000;    // Timeout for resolving DNS hosts in milliseconds.

        public const char SCHEME_ADDR_SEPARATOR = ':';
        public const char USER_HOST_SEPARATOR = '@';
        public const char PARAM_TAG_DELIMITER = ';';
        public const char HEADER_START_DELIMITER = '?';
        private const char HEADER_TAG_DELIMITER = '&';
        private const char TAG_NAME_VALUE_SEPERATOR = '=';

        private static ILog logger = AssemblyState.logger;

        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;

        private static SIPProtocolsEnum m_defaultSIPTransport = SIPProtocolsEnum.udp;
        private static SIPSchemesEnum m_defaultSIPScheme = SIPSchemesEnum.sip;
        private static string m_sipRegisterRemoveAll = SIPConstants.SIP_REGISTER_REMOVEALL;
        private static string m_uriParamTransportKey = SIPHeaderAncillary.SIP_HEADERANC_TRANSPORT;

        [DataMember]
        public SIPSchemesEnum Scheme = m_defaultSIPScheme;

        [DataMember]
        public string User;

        [DataMember]
        public string Host;

        [DataMember]
        public SIPParameters Parameters = new SIPParameters(null, PARAM_TAG_DELIMITER);

        [DataMember]
        public SIPParameters Headers = new SIPParameters(null, HEADER_TAG_DELIMITER);

        /// <summary>
        /// The protocol for a SIP URI is dicatated by the scheme of the URI and then by the transport parameter and finally by the 
        /// use fo a default protocol. If the URI is a sips one then the protocol must be TLS. After that if there is a transport
        /// parameter specified for the URI it dictates the protocol for the URI. Finally if there is no transport parameter for a sip
        /// URI then the default UDP transport is used.
        /// </summary>
        public SIPProtocolsEnum Protocol
        {
            get
            {
                if (Scheme == SIPSchemesEnum.sips)
                {
                    return SIPProtocolsEnum.tls;
                }
                else
                {
                    if (Parameters != null && Parameters.Has(m_uriParamTransportKey))
                    {
                        if (SIPProtocolsType.IsAllowedProtocol(Parameters.Get(m_uriParamTransportKey)))
                        {
                            return SIPProtocolsType.GetProtocolType(Parameters.Get(m_uriParamTransportKey));
                        }
                    }

                    return m_defaultSIPTransport;
                }
            }
            set
            {
                if (value == SIPProtocolsEnum.udp)
                {
                    Scheme = SIPSchemesEnum.sip;
                    if (Parameters != null && Parameters.Has(m_uriParamTransportKey))
                    {
                        Parameters.Remove(m_uriParamTransportKey);
                    }
                }
                else
                {
                    Parameters.Set(m_uriParamTransportKey, value.ToString());
                }
            }
        }

        /// <summary>
        /// Returns a string that can be used to compare SIP URI addresses.
        /// </summary>
        public string CanonicalAddress
        {
            get
            {
                string canonicalAddress = Scheme + ":";
                canonicalAddress += (User != null && User.Trim().Length > 0) ? User + "@" : null;

                if (Host.IndexOf(':') != -1)
                {
                    canonicalAddress += Host;
                }
                else
                {
                    canonicalAddress += Host + ":" + m_defaultSIPPort;
                }

                return canonicalAddress;
            }
        }

        private SIPURI()
        { }

        public SIPURI(string user, string host, string paramsAndHeaders)
        {
            User = user;
            Host = host;
            ParseParamsAndHeaders(paramsAndHeaders);
        }

        public SIPURI(string user, string host, string paramsAndHeaders, SIPSchemesEnum scheme)
        {
            User = user;
            Host = host;
            ParseParamsAndHeaders(paramsAndHeaders);
            Scheme = scheme;
        }

        public SIPURI(string user, string host, string paramsAndHeaders, SIPSchemesEnum scheme, SIPProtocolsEnum protocol)
        {
            User = user;
            Host = host;
            ParseParamsAndHeaders(paramsAndHeaders);
            Scheme = scheme;

            if (protocol != SIPProtocolsEnum.udp)
            {
                Parameters.Set(m_uriParamTransportKey, protocol.ToString());
            }
        }

        public SIPURI(SIPSchemesEnum scheme, SIPEndPoint sipEndPoint)
        {
            Scheme = scheme;
            Host = sipEndPoint.GetIPEndPoint().ToString();

            if (sipEndPoint.Protocol != SIPProtocolsEnum.udp)
            {
                Parameters.Set(m_uriParamTransportKey, sipEndPoint.Protocol.ToString());
            }
        }

        public static SIPURI ParseSIPURI(string uri)
        {
            try
            {
                SIPURI sipURI = new SIPURI();

                if (uri == null || uri.Trim().Length == 0)
                {
                    throw new SIPValidationException(SIPValidationFieldsEnum.URI, "A SIP URI cannot be parsed from an empty string.");
                }
                else
                {
                    if (uri == m_sipRegisterRemoveAll)
                    {
                        sipURI.Host = m_sipRegisterRemoveAll;
                    }
                    else
                    {
                        int colonPosn = uri.IndexOf(SCHEME_ADDR_SEPARATOR);

                        if (colonPosn == -1)
                        {
                            throw new SIPValidationException(SIPValidationFieldsEnum.URI, "SIP URI did not contain compulsory colon");
                        }
                        else
                        {
                            try
                            {
                                sipURI.Scheme = SIPSchemesType.GetSchemeType(uri.Substring(0, colonPosn));
                            }
                            catch
                            {
                                throw new SIPValidationException(SIPValidationFieldsEnum.URI, SIPResponseStatusCodesEnum.UnsupportedURIScheme, "SIP scheme " + uri.Substring(0, colonPosn) + " was not understood");
                            }

                            string uriHostPortion = uri.Substring(colonPosn + 1);
                            int ampPosn = uriHostPortion.IndexOf(USER_HOST_SEPARATOR);
                            int paramHeaderPosn = -1;
                            if (ampPosn != -1)
                            {
                                paramHeaderPosn = uriHostPortion.IndexOfAny(new char[] { PARAM_TAG_DELIMITER, HEADER_START_DELIMITER }, ampPosn);
                            }
                            else
                            {
                                // Host only SIP URI.
                                paramHeaderPosn = uriHostPortion.IndexOfAny(new char[] { PARAM_TAG_DELIMITER, HEADER_START_DELIMITER });
                            }

                            if (ampPosn != -1 && paramHeaderPosn != -1)
                            {
                                sipURI.User = uriHostPortion.Substring(0, ampPosn);
                                sipURI.Host = uriHostPortion.Substring(ampPosn + 1, paramHeaderPosn - ampPosn - 1);
                                string paramsAndHeaders = uriHostPortion.Substring(paramHeaderPosn);

                                sipURI.ParseParamsAndHeaders(paramsAndHeaders);
                            }
                            else if (ampPosn == -1 && paramHeaderPosn == 0)
                            {
                                throw new SIPValidationException(SIPValidationFieldsEnum.URI, "No Host portion in SIP URI");
                            }
                            else if (ampPosn == -1 && paramHeaderPosn != -1)
                            {
                                sipURI.Host = uriHostPortion.Substring(0, paramHeaderPosn);
                                string paramsAndHeaders = uriHostPortion.Substring(paramHeaderPosn);

                                sipURI.ParseParamsAndHeaders(paramsAndHeaders);
                            }
                            else if (ampPosn != -1)
                            {
                                sipURI.User = uriHostPortion.Substring(0, ampPosn);
                                sipURI.Host = uriHostPortion.Substring(ampPosn + 1, uriHostPortion.Length - ampPosn - 1);
                            }
                            else
                            {
                                sipURI.Host = uriHostPortion;
                            }
                        }
                    }

                    return sipURI;
                }
            }
            catch (SIPValidationException validationExcp)
            {
                throw validationExcp;
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseSIPURI (URI=" + uri + "). " + excp.Message);
                throw new SIPValidationException(SIPValidationFieldsEnum.URI, "Unknown error parsing SIP URI.");
            }
        }

        public static SIPURI ParseSIPURIRelaxed(string partialURI)
        {
            if (partialURI == null || partialURI.Trim().Length == 0)
            {
                return null;
            }
            else
            {
                string regexSchemePattern = "^(" + SIPSchemesEnum.sip + "|" + SIPSchemesEnum.sips + "):";

                if (Regex.Match(partialURI, regexSchemePattern + @"\S+").Success)
                {
                    // The partial uri is already valid.
                    return SIPURI.ParseSIPURI(partialURI);
                }
                else
                {
                    // The partial URI is missing the scheme.
                    return SIPURI.ParseSIPURI(m_defaultSIPScheme.ToString() + SCHEME_ADDR_SEPARATOR.ToString() + partialURI);
                }
            }
        }

        public new string ToString()
        {
            try
            {
                string uriStr = Scheme.ToString() + SCHEME_ADDR_SEPARATOR;

                uriStr = (User != null) ? uriStr + User + USER_HOST_SEPARATOR + Host : uriStr + Host;

                if (Parameters != null && Parameters.Count > 0)
                {
                    uriStr += Parameters.ToString();
                }

                // If the URI's protocol is not implied already set the transport parameter.
                if (Scheme != SIPSchemesEnum.sips && Protocol != SIPProtocolsEnum.udp && !Parameters.Has(m_uriParamTransportKey))
                {
                    uriStr += PARAM_TAG_DELIMITER + m_uriParamTransportKey + TAG_NAME_VALUE_SEPERATOR + Protocol.ToString();
                }

                if (Headers != null && Headers.Count > 0)
                {
                    string headerStr = Headers.ToString();
                    uriStr += HEADER_START_DELIMITER + headerStr.Substring(1);
                }

                return uriStr;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPURI ToString. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Returns a string representation of the URI with any parameter and headers ommitted exceot for the transport
        /// parameter. The string returned by this function is used amonst other things to match Route headers set by this
        /// SIP agent.
        /// </summary>
        /// <returns>A string represenation of the URI with headers and parameteres ommitted except for the trnaport parameter if it is required.</returns>
        public string ToParameterlessString()
        {
            try
            {
                string uriStr = Scheme.ToString() + SCHEME_ADDR_SEPARATOR;

                uriStr = (User != null) ? uriStr + User + USER_HOST_SEPARATOR + Host : uriStr + Host;

                // If the URI's protocol is not implied already set the transport parameter.
                if (Scheme != SIPSchemesEnum.sips && Protocol != SIPProtocolsEnum.udp && !Parameters.Has(m_uriParamTransportKey))
                {
                    uriStr += PARAM_TAG_DELIMITER + m_uriParamTransportKey + TAG_NAME_VALUE_SEPERATOR + Protocol.ToString();
                }

                return uriStr;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPURI ToParamaterlessString. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Returns an address of record for the SIP URI which is a string in the format user@host.
        /// </summary>
        /// <returns>A string representing the address of record for the URI.</returns>
        public string ToAOR()
        {
            return User + USER_HOST_SEPARATOR + Host;
        }

        public SIPEndPoint ToSIPEndPoint()
        {
            if (IPSocket.IsIPSocket(Host) || IPSocket.IsIPAddress(Host))
            {
                return new SIPEndPoint(Protocol, IPSocket.GetIPEndPoint(Host));
            }
            else
            {
                return null;
            }
        }

        public static bool AreEqual(SIPURI uri1, SIPURI uri2)
        {
            return uri1 == uri2;
        }

        private void ParseParamsAndHeaders(string paramsAndHeaders)
        {
            if (paramsAndHeaders != null && paramsAndHeaders.Trim().Length > 0)
            {
                int headerDelimPosn = paramsAndHeaders.IndexOf(HEADER_START_DELIMITER);

                if (headerDelimPosn == -1)
                {
                    Parameters = new SIPParameters(paramsAndHeaders, PARAM_TAG_DELIMITER);
                }
                else
                {
                    Parameters = new SIPParameters(paramsAndHeaders.Substring(0, headerDelimPosn), PARAM_TAG_DELIMITER);
                    Headers = new SIPParameters(paramsAndHeaders.Substring(headerDelimPosn + 1), HEADER_TAG_DELIMITER);
                }
            }
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (SIPURI)obj);
        }

        public static bool operator ==(SIPURI uri1, SIPURI uri2)
        {
            if ((object)uri1 == null && (object)uri2 == null)
            //if (uri1 == null && uri2 == null)
            {
                return true;
            }
            else if ((object)uri1 == null || (object)uri2 == null)
            //if (uri1 == null || uri2 == null)
            {
                return false;
            }
            else if (uri1.Host == null || uri2.Host == null)
            {
                return false;
            }
            else if (uri1.CanonicalAddress != uri2.CanonicalAddress)
            {
                return false;
            }
            else
            {
                // Compare parameters.
                if (uri1.Parameters.Count != uri2.Parameters.Count)
                {
                    return false;
                }
                else
                {
                    string[] uri1Keys = uri1.Parameters.GetKeys();

                    if (uri1Keys != null && uri1Keys.Length > 0)
                    {
                        foreach (string key in uri1Keys)
                        {
                            if (uri1.Parameters.Get(key) != uri2.Parameters.Get(key))
                            {
                                return false;
                            }
                        }
                    }
                }

                // Compare headers.
                if (uri1.Headers.Count != uri2.Headers.Count)
                {
                    return false;
                }
                else
                {
                    string[] uri1Keys = uri1.Headers.GetKeys();

                    if (uri1Keys != null && uri1Keys.Length > 0)
                    {
                        foreach (string key in uri1Keys)
                        {
                            if (uri1.Headers.Get(key) != uri2.Headers.Get(key))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        public static bool operator !=(SIPURI x, SIPURI y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return CanonicalAddress.GetHashCode() + Parameters.GetHashCode() + Headers.GetHashCode();
        }

        public SIPURI CopyOf()
        {
            SIPURI copy = new SIPURI();
            copy.Scheme = Scheme;
            copy.Host = Host;
            copy.User = User;

            if (Parameters.Count > 0)
            {
                copy.Parameters = Parameters.CopyOf();
            }

            if (Headers.Count > 0)
            {
                copy.Headers = Headers.CopyOf();
            }

            return copy;
        }

        #region Unit testing.

#if UNITTEST
	
		[TestFixture]
		public class SIPURIUnitTest
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
			public void ParseHostOnlyURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("sip:sip.domain.com");
				
				Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void ParseHostAndUserURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com");
				
				Assert.IsTrue(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void ParseWithParamURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com;param=1234");
				
				Assert.IsTrue(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
				Assert.IsTrue(sipURI.Parameters.Get("PARAM") == "1234", "The SIP URI Parameter was not parsed correctly.");
                Assert.IsTrue(sipURI.ToString() == "sip:user@sip.domain.com;param=1234", "The SIP URI was not correctly to string'ed.");

				Console.WriteLine("-----------------------------------------");
			}
			
			[Test]
			public void ParseWithParamAndPortURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("sip:1234@sip.domain.com:5060;TCID-0");

				Console.WriteLine("URI Name = " + sipURI.User);
				Console.WriteLine("URI Host = " + sipURI.Host);
				
				Assert.IsTrue(sipURI.User == "1234", "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "sip.domain.com:5060", "The SIP URI Host was not parsed correctly.");
				Assert.IsTrue(sipURI.Parameters.Has("TCID-0"), "The SIP URI Parameter was not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void ParseWithHeaderURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("sip:user@sip.domain.com?header=1234");
				
				Assert.IsTrue(sipURI.User == "user", "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
				Assert.IsTrue(sipURI.Headers.Get("header") == "1234", "The SIP URI Header was not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

			[Test]
			public void SpaceInHostNameURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("sip:Blue Face");
				
				Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "Blue Face", "The SIP URI Host was not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}
	
			[Test]
			public void ContactAsteriskURIUnitTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
	
				SIPURI sipURI = SIPURI.ParseSIPURI("*");
				
				Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
				Assert.IsTrue(sipURI.Host == "*", "The SIP URI Host was not parsed correctly.");

				Console.WriteLine("-----------------------------------------");
			}

            [Test]
            public void AreEqualNoParamsURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com");

                Assert.IsTrue(AreEqual(sipURI1, sipURI2), "The SIP URIs were not correctly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void AreEqualIPAddressNoParamsURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@192.168.1.101");

                Assert.IsTrue(AreEqual(sipURI1, sipURI2), "The SIP URIs were not correctly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void AreEqualWithParamsURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");

                Assert.IsTrue(AreEqual(sipURI1, sipURI2), "The SIP URIs were not correctly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void NotEqualWithParamsURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value2");

                Assert.IsFalse(AreEqual(sipURI1, sipURI2), "The SIP URIs were incorrectly equated as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void AreEqualWithHeadersURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value1&header2=value2");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

                Assert.IsTrue(AreEqual(sipURI1, sipURI2), "The SIP URIs were not correctly identified as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void NotEqualWithHeadersURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2?header1=value2&header2=value2");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1?header2=value2&header1=value1");

                Assert.IsFalse(AreEqual(sipURI1, sipURI2), "The SIP URIs were not correctly identified as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void GetHashCodeEqualityURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");

                Assert.AreEqual(sipURI1.GetHashCode(), sipURI2.GetHashCode(), "The SIP URIs did not have equal hash codes.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void GetHashCodeNotEqualURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value2");

                Assert.AreNotEqual(sipURI1.GetHashCode(), sipURI2.GetHashCode(), "The SIP URIs did not have equal hash codes.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void GetHashCodeDiffParamOrderURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key2=value2;key1=value1");
                SIPURI sipURI2 = SIPURI.ParseSIPURI("sip:abcd@adcb.com;key1=value1;key2=value2");

                Assert.AreEqual(sipURI1.GetHashCode(), sipURI2.GetHashCode(), "The SIP URIs did not have equal hash codes.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void AreEqualNullURIsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = null;
                SIPURI sipURI2 = null;

                Assert.IsTrue(sipURI1 == sipURI2, "The SIP URIs were not correctly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void NotEqualOneNullURIUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 =  SIPURI.ParseSIPURI("sip:abcd@adcb.com");
                SIPURI sipURI2 = null;

                Assert.IsFalse(sipURI1 == sipURI2, "The SIP URIs were incorrectly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void AreEqualNullEqualsOverloadUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = null;

                Assert.IsTrue(sipURI1 == null, "The SIP URIs were not correctly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void AreEqualNullNotEqualsOverloadUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI1 = null;

                Assert.IsFalse(sipURI1 != null, "The SIP URIs were incorrectly found as equal.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            [ExpectedException(typeof(SIPValidationException))]
            public void UnknownSchemeUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURI("tel:1234565");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void ParamsInUserPortionURITest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURI("sip:C=on;t=DLPAN@10.0.0.1:5060;lr");

                Assert.IsTrue("C=on;t=DLPAN" == sipURI.User, "SIP user portion parsed incorrectly.");
                Assert.IsTrue("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void SwitchTagParameterUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURI("sip:joebloggs@sip.mysipswitch.com;switchtag=119651");

                Assert.IsTrue("joebloggs" == sipURI.User, "SIP user portion parsed incorrectly.");
                Assert.IsTrue("sip.mysipswitch.com" == sipURI.Host, "SIP host portion parsed incorrectly.");
                Assert.IsTrue("119651" == sipURI.Parameters.Get("switchtag"), "switchtag parameter parsed incorrectly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void LongUserUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURI("sip:EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4@10.0.0.1:5060");

                Assert.IsTrue("EhZgKgLM9CwGqYDAECqDpL5MNrM_sKN5NurN5q_pssAk4oxhjKEMT4" == sipURI.User, "SIP user portion parsed incorrectly.");
                Assert.IsTrue("10.0.0.1:5060" == sipURI.Host, "SIP host portion parsed incorrectly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void ParsePartialURINoSchemeUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip.domain.com");

                Assert.IsTrue(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
                Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
                Assert.IsTrue(sipURI.Host == "sip.domain.com", "The SIP URI Host was not parsed correctly.");
                Assert.IsTrue(sipURI.Protocol == SIPProtocolsEnum.udp, "The SIP URI protocol was not parsed correctly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void ParsePartialURISIPSSchemeUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sips:sip.domain.com:1234");

                Assert.IsTrue(sipURI.Scheme == SIPSchemesEnum.sips, "The SIP URI scheme was not parsed correctly.");
                Assert.IsTrue(sipURI.User == null, "The SIP URI User was not parsed correctly.");
                Assert.IsTrue(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void ParsePartialURIWithUserUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("sip:joe.bloggs@sip.domain.com:1234;transport=tcp");

                Assert.IsTrue(sipURI.Scheme == SIPSchemesEnum.sip, "The SIP URI scheme was not parsed correctly.");
                Assert.IsTrue(sipURI.User == "joe.bloggs", "The SIP URI User was not parsed correctly.");
                Assert.IsTrue(sipURI.Host == "sip.domain.com:1234", "The SIP URI Host was not parsed correctly.");
                Assert.IsTrue(sipURI.Protocol == SIPProtocolsEnum.tcp, "The SIP URI protocol was not parsed correctly.");

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Got a URI like this from Zoiper.
            /// </summary>
            [Test]
            [ExpectedException(typeof(SIPValidationException))]
            public void ParseHoHostUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURI("sip:;transport=UDP");

                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void UDPProtocolToStringTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = new SIPURI(SIPSchemesEnum.sip, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"));
                Console.WriteLine(sipURI.ToString());
                Assert.IsTrue(sipURI.ToString() == "sip:127.0.0.1:5060", "The SIP URI was not ToString'ed correctly.");
                Console.WriteLine("-----------------------------------------");
            }

            [Test]
            public void ParseUDPProtocolToStringTest() {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPURI sipURI = SIPURI.ParseSIPURIRelaxed("127.0.0.1");
                Console.WriteLine(sipURI.ToString());
                Assert.IsTrue(sipURI.ToString() == "sip:127.0.0.1", "The SIP URI was not ToString'ed correctly.");
                Console.WriteLine("-----------------------------------------");
            }
        }

#endif

        #endregion
    }
}
