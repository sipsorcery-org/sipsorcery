//-----------------------------------------------------------------------------
// Filename: SIPDNSManager.cs
//
// Description: An implementation of RFC 3263 to resolve SIP URIs using NAPTR, SRV and A records (could it be anymore convoluted?).
//
// History:
// 11 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Net;
using Heijden.DNS;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// 1. If transport parameter is specified it takes precedence,
    /// 2. If no transport parameter and target is an IP address then sip should use udp and sips tcp,
    /// 3. If no transport parameter and target is a host name with an explicit port then sip should use 
    ///    udp and sips tcp and host should be resolved using an A or AAAA record DNS lookup (section 4.2),
    /// 4. If no transport protocol and no explicit port and target is a host name then the client should no
    ///    an NAPTR lookup and utilise records for services SIP+D2U, SIP+D2T, SIP+D2S, SIPS+D2T and SIPS+D2S,
    /// 5. If NAPTR record(s) are found select the desired transport and lookup the SRV record,
    /// 6. If no NAPT records are found lookup SRV record for desired protocol _sip._udp, _sip._tcp, _sips._tcp,
    ///    _sip._tls,
    /// 7. If no SRV records found lookup A or AAAA record.
    /// </remarks>
    public class SIPDNSManager
    {
        private const int DNS_LOOKUP_TIMEOUT = 2;   // 2 second timeout for DNS lookups.

        public const string NAPTR_SIP_UDP_SERVICE = "SIP+D2U";
        public const string NAPTR_SIP_TCP_SERVICE = "SIP+D2T";
        public const string NAPTR_SIPS_TCP_SERVICE = "SIPS+D2T";
       
        public const string SRV_SIP_TCP_QUERY_PREFIX = "_sip._tcp.";
        public const string SRV_SIP_UDP_QUERY_PREFIX = "_sip._udp.";
        public const string SRV_SIP_TLS_QUERY_PREFIX = "_sip._tls.";
        public const string SRV_SIPS_TCP_QUERY_PREFIX = "_sips._tcp.";

        private static ILog logger = AssemblyState.logger;

        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;

        public static event SIPMonitorLogDelegate SIPMonitorLogEvent;

        static SIPDNSManager()
        {
            SIPMonitorLogEvent += (e) => { };
        }

        public static SIPEndPoint Resolve(string host)
        {
            return Resolve(SIPURI.ParseSIPURIRelaxed(host), true);
        }

        public static SIPEndPoint Resolve(SIPURI sipURI, bool synchronous)
        {
            //logger.Debug("SIPDNSManager attempting to resolve " + sipURI.ToString() + ".");

            SIPDNSLookupResult lookupResult = ResolveSIPService(sipURI, synchronous);
            if (lookupResult.LookupError != null)
            {
                logger.Warn("SIPDNSManager experienced a lookup error of " + lookupResult.LookupError + " on " + sipURI.ToParameterlessString() + ", returning null.");
                return null;
            }
            else if (lookupResult.EndPointResults != null && lookupResult.EndPointResults.Count > 0)
            {
                return lookupResult.EndPointResults[0].LookupEndPoint;
            }
            else
            {
                logger.Warn("SIPDNSManager was empty for a lookup on " + sipURI.ToParameterlessString() + ", returning null.");
                return null;
            }
        }

        private static SIPDNSLookupResult ResolveSIPService(string host) {
            try {
                return ResolveSIPService(SIPURI.ParseSIPURIRelaxed(host), true);
            }
            catch (Exception excp) {
                logger.Error("Exception ResolveSIPService (" + host + "). " + excp.Message);
                throw;
            }
        }

        private static SIPDNSLookupResult ResolveSIPService(SIPURI sipURI, bool synchronous)
        {
            try
            {
                if (sipURI == null) {
                    throw new ArgumentNullException("sipURI", "Cannot resolve SIP service on a null URI.");
                }

                string host = sipURI.Host;
                int port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
                bool explicitPort = false;

                if (sipURI.Host.IndexOf(':') != -1)
                {
                    host = sipURI.Host.Split(':')[0];
                    Int32.TryParse(sipURI.Host.Split(':')[1], out port);
                    explicitPort = true;
                }

                if (Regex.Match(host, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$").Success)
                {
                    // Target is an IP address, no DNS lookup required.
                    IPAddress hostIP = IPAddress.Parse(host);
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(hostIP, port)), 0);
                    SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                    result.AddLookupResult(sipLookupEndPoint);
                    return result;
                }
                else if (explicitPort)
                {
                    // Target is a hostname with an explicit port, DNS lookup for A or AAAA record.
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS explicit port lookup requested for " + sipURI.ToString() + ".", null));
                    SIPDNSLookupResult sipLookupResult = new SIPDNSLookupResult(sipURI);
                    DNSARecordLookup(host, port, ref sipLookupResult);
                    return sipLookupResult;
                }
                else
                {
                    // Target is a hostname with no explicit port, use the whole NAPTR->SRV->A lookup procedure.
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS full lookup requested for " + sipURI.ToString() + ".", null));
                    SIPDNSLookupResult sipLookupResult = new SIPDNSLookupResult(sipURI);
                    DNSNAPTRRecordLookup(host, ref sipLookupResult);
                    DNSSRVRecordLookup(sipURI.Scheme, sipURI.Protocol, host, ref sipLookupResult);
                    SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                    int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                    DNSARecordLookup(nextSRVRecord, host, lookupPort, ref sipLookupResult);
                    return sipLookupResult;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ResolveSIPService. (" + sipURI.ToString() + ")" + excp.Message);
                return new SIPDNSLookupResult(sipURI, excp.Message);
            }
        }

        private static void DNSARecordLookup(string host, int port, ref SIPDNSLookupResult lookupResult)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record lookup initiated for " + host + ".", null));

            DNSResponse aRecordResponse = DNSManager.Lookup(host, DNSQType.A, DNS_LOOKUP_TIMEOUT, null);
            if (aRecordResponse.Error != null)
            {
                lookupResult.LookupError = aRecordResponse.Error;
            }
            else if (aRecordResponse.RecordsA == null || aRecordResponse.RecordsA.Length == 0)
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no A records found for " + host + ".", null));
                lookupResult.LookupError = "No A records found for " + host + ".";
            }
            else
            {
                SIPURI sipURI = lookupResult.URI;
                foreach (RecordA aRecord in aRecordResponse.RecordsA)
                {
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                    lookupResult.AddLookupResult(sipLookupEndPoint);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                }
            }
        }

        private static void DNSARecordLookup(SIPDNSServiceResult nextSRVRecord, string host, int port, ref SIPDNSLookupResult lookupResult)
        {
            if (nextSRVRecord != null && nextSRVRecord.Data != null)
            {
                DNSARecordLookup(nextSRVRecord.Data, port, ref lookupResult);
                nextSRVRecord.ResolvedAt = DateTime.Now;
            }
            else
            {
                DNSARecordLookup(host, port, ref lookupResult);
            }
        }

        private static void DNSNAPTRRecordLookup(string host, ref SIPDNSLookupResult lookupResult)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS NAPTR record lookup initiated for " + host + ".", null));

            // Target is a hostname with no explicit port, DNS lookup for NAPTR records.
            DNSResponse naptrRecordResponse = DNSManager.Lookup(host, DNSQType.NAPTR, DNS_LOOKUP_TIMEOUT, null);
            if (naptrRecordResponse.Error == null && naptrRecordResponse.RecordNAPTR != null && naptrRecordResponse.RecordNAPTR.Length > 0) {
                foreach (RecordNAPTR naptrRecord in naptrRecordResponse.RecordNAPTR) {
                    lookupResult.AddNAPTRResult(naptrRecord);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS NAPTR record found for " + host + ", result " + naptrRecord.Service + " " + naptrRecord.Replacement + ".", null));
                }
            }
            else {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no NAPTR records found for " + host + ".", null));
            }
        }

        private static void DNSSRVRecordLookup(SIPSchemesEnum scheme, SIPProtocolsEnum protocol, string host, ref SIPDNSLookupResult lookupResult)
        {
            SIPServicesEnum reqdNAPTRService = SIPServicesEnum.none;
            if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.udp) {
                reqdNAPTRService = SIPServicesEnum.sipudp;
            }
            else if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.tcp) {
                reqdNAPTRService = SIPServicesEnum.siptcp;
            }
            else if (scheme == SIPSchemesEnum.sips && protocol == SIPProtocolsEnum.tcp) {
                reqdNAPTRService = SIPServicesEnum.sipstcp;
            }
            else if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.tls) {
                reqdNAPTRService = SIPServicesEnum.siptls;
            }

            // If there are NAPTR records available see if there is a matching one for the SIP scheme and protocol required.
            SIPDNSServiceResult naptrService = null;
            if (lookupResult.SIPNAPTRResults != null && lookupResult.SIPNAPTRResults.Count > 0)
            {
                if (reqdNAPTRService != SIPServicesEnum.none && lookupResult.SIPNAPTRResults.ContainsKey(reqdNAPTRService))
                {
                    naptrService = lookupResult.SIPNAPTRResults[reqdNAPTRService];
                }
            }

            // Construct the SRV target to lookup depending on whether an NAPTR record was available or not.
            string srvLookup = null;
            if (naptrService != null) {
                srvLookup = naptrService.Data;
            }
            else {
                srvLookup = "_" + scheme.ToString() + "._" + protocol.ToString() + "." + host;
            }

            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record lookup initiated for " + srvLookup + ".", null));

            DNSResponse srvRecordResponse = DNSManager.Lookup(srvLookup, DNSQType.SRV, DNS_LOOKUP_TIMEOUT, null);
            if (srvRecordResponse.Error == null && srvRecordResponse.RecordSRV != null && srvRecordResponse.RecordSRV.Length > 0) {
                foreach (RecordSRV srvRecord in srvRecordResponse.RecordSRV) {
                    lookupResult.AddSRVResult(reqdNAPTRService, srvRecord);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record found for " + srvLookup + ", result " + srvRecord.Target + " " + srvRecord.Port + ".", null));
                }
            }
            else {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no SRV records found for " + srvLookup + ".", null));
            }
        }

        #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPDNSManagerUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
                log4net.Config.BasicConfigurator.Configure();
			}

			[TestFixtureTearDown]
			public void Dispose()
			{
                DNSManager.Stop();
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void IPAddresTargetTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("10.0.0.100");
                SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

                Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

                Assert.IsTrue(lookupSIPEndPoint.SIPProtocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
                Assert.IsTrue(lookupSIPEndPoint.SocketEndPoint.ToString() == "10.0.0.100:5060", "The resolved socket was not correct.");
            }

            [Test]
            public void IPAddresAndSIPSTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("sips:10.0.0.100");
                SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

                Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

                Assert.IsTrue(lookupSIPEndPoint.SIPProtocol == SIPProtocolsEnum.tls, "The resolved protocol was not correct.");
                Assert.IsTrue(lookupSIPEndPoint.SocketEndPoint.ToString() == "10.0.0.100:5061", "The resolved socket was not correct.");
            }

            [Test]
            public void HostWithExplicitPortTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("sip.blueface.ie:5060");
                SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

                Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

                Assert.IsTrue(lookupSIPEndPoint.SIPProtocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
                Assert.IsTrue(lookupSIPEndPoint.SocketEndPoint.ToString() == "194.213.29.100:5060", "The resolved socket was not correct.");
            }

            [Test]
            public void HostWithNoExplicitPortTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPEndPoint lookupSIPEndPoint = SIPDNSManager.Resolve("sip.blueface.ie");

                Console.WriteLine("Resolved SIP end point " + lookupSIPEndPoint);

                Assert.IsTrue(lookupSIPEndPoint.SIPProtocol == SIPProtocolsEnum.udp, "The resolved protocol was not correct.");
                Assert.IsTrue(lookupSIPEndPoint.SocketEndPoint.ToString() == "194.213.29.100:5060", "The resolved socket was not correct.");
            }


            [Test]
            public void HostWithExplicitPortAndMultipleIPsTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("callcentric.com:5060");
                SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

                Assert.IsTrue(lookupResult.EndPointResults.Count > 0, "The number of lookup results returned was incorrect.");
            }

            [Test]
            public void HostWithNAPTRRecordTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("columbia.edu");

                Assert.IsTrue(lookupResult.SIPNAPTRResults != null && lookupResult.SIPNAPTRResults.Count > 0, "The number of NAPTR results returned was incorrect.");
                Assert.IsTrue(lookupResult.SIPSRVResults != null && lookupResult.SIPSRVResults.Count > 0, "The number of SRV results returned was incorrect.");
            }

            [Test]
            public void HostWithNoNAPTRAndSRVTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("callcentric.com");
                SIPEndPoint lookupSIPEndPoint = lookupResult.EndPointResults[0].LookupEndPoint;

                Assert.IsTrue(lookupResult.EndPointResults.Count > 0, "The number of lookup results returned was incorrect.");
            }

            [Test]
            public void TLSSRVTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService("sip:snom.com;transport=tls");
                
                Console.WriteLine("result=" + lookupResult.SIPSRVResults[0].Data + ".");

                Assert.IsTrue(lookupResult.SIPSRVResults != null && lookupResult.SIPSRVResults.Count > 0, "The number of SRV results returned was incorrect.");
                Assert.IsTrue(lookupResult.SIPSRVResults[0].SIPService == SIPServicesEnum.siptls, "The SIP Service returned for the lookup was incorrect.");
                Assert.IsTrue(lookupResult.SIPSRVResults[0].Data == "sip.snom.com.", "The target returned for the lookup was incorrect.");
            }
        }

        #endif

        #endregion
    }
}
