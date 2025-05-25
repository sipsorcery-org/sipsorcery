//-----------------------------------------------------------------------------
// Filename: SIPDnsUnitTest.cs
//
// Description: Integration tests for the SIP specific DNS lookup logic.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 24 Jul 2020  Aaron Clauson   Moved from unit to integration tests.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.IntegrationTests
{
    [Trait("Category", "dns")]
    public class SIPDnsUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPDnsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that an IP address can be resolved when the resolution can only be done via a SRV record.
        /// </summary>
        [Fact]
        public async Task ResolveHostFromServiceTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();

            var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sipsorcery.com"), false, cts.Token);

            Assert.NotNull(result);

            logger.LogDebug("resolved to SIP end point {result}.", result);
        }

        /// <summary>
        /// Tests that an attempt to lookup the a hostname that's not fully qualified works correctly.
        /// </summary>
        [Fact]
        public async Task LookupLocalHostnameTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();

            string hostname = System.Net.Dns.GetHostName();

            if (hostname.EndsWith(SIPDns.MDNS_TLD))
            {
                logger.LogWarning("Skipping unit test LookupLocalHostnameTest due to RFC6762 domain.");
            }
            else
            {
                var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed(hostname), false, cts.Token);

                Assert.NotNull(result);

                logger.LogDebug("resolved to SIP end point {Result}.", result);
            }
        }

        [Fact]
        public async Task ResolveSIPServiceTest()
        {
            try
            {
                logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
                logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

                //SIPDNSManager.UseNAPTRLookups = true;

                CancellationTokenSource cts = new CancellationTokenSource();

                var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp"), false, cts.Token);

                Assert.NotNull(result);
                logger.LogDebug("resolved to SIP end point {Result}.", result);
                //Assert.NotEmpty(result.SIPNAPTRResults);
                //Assert.NotEmpty(result.SIPSRVResults);
                //Assert.NotEmpty(result.EndPointResults);

                //result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed("sip:tel.t-online.de"), false);
                //Assert.NotNull(resultEP);
                //result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed("sips:hpbxsec.deutschland-lan.de:5061;transport=tls"), false);
                //Assert.NotNull(resultEP);
            }
            finally
            {
                //SIPDNSManager.UseNAPTRLookups = false;
            }
        }

        /// <summary>
        /// Does the same resolve twice in a row within a short space of time. This should cause the second lookup
        /// to be supplied from the in-memory cache.
        /// </summary>
        [Fact]
        public async Task ResolveNoSRVFromCacheTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();

            SIPURI lookupURI = SIPURI.ParseSIPURIRelaxed("sip:sip.sipsorcery.com:5060");
            //var result = SIPDNSManager.ResolveSIPService(lookupURI, false);
            var result = await SIPDns.ResolveAsync(lookupURI, false, cts.Token);
            Assert.NotNull(result);

            //SIPEndPoint resultEP = result.GetSIPEndPoint();
            Assert.NotNull(result);
            Assert.NotEqual(SIPEndPoint.Empty, result);
            logger.LogDebug("resolved to SIP end point {Result}.", result);
            //Assert.NotEmpty(result.SIPSRVResults);
            //Assert.NotEmpty(result.EndPointResults);

            // Do the same look up again immediately to check the result when it comes from the in-memory cache.
            var resultCache = SIPDns.ResolveFromCache(lookupURI, false);
            Assert.NotNull(resultCache);
            Assert.NotEqual(SIPEndPoint.Empty, resultCache);
            logger.LogDebug("cache resolved to SIP end point {ResultCache}.", resultCache);
        }

        /// <summary>
        /// Does the same resolve twice in a row within a short space of time. This should cause the second lookup
        /// to be supplied from the in-memory cache.
        /// </summary>
        [Fact]
        public async Task ResolveWithSRVFromCacheTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();

            SIPURI lookupURI = SIPURI.ParseSIPURIRelaxed("sip:tel.t-online.de");
            //var result = SIPDNSManager.ResolveSIPService(lookupURI, false);
            var result = await SIPDns.ResolveAsync(lookupURI, false, cts.Token);
            Assert.NotNull(result);

            //SIPEndPoint resultEP = result.GetSIPEndPoint();
            Assert.NotNull(result);
            Assert.NotEqual(SIPEndPoint.Empty, result);
            logger.LogDebug("resolved to SIP end point {Result}.", result);
            //Assert.NotEmpty(result.SIPSRVResults);
            //Assert.NotEmpty(result.EndPointResults);

            // Do the same look up again immediately to check the result when it comes from the in-memory cache.
            var resultCache = SIPDns.ResolveFromCache(lookupURI, false);
            Assert.NotNull(resultCache);
            Assert.NotEqual(SIPEndPoint.Empty, resultCache);
            logger.LogDebug("cache resolved to SIP end point {ResultCache}.", resultCache);
        }

        [Fact]
        public async Task ResolveSIPServiceAsyncTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();
            //var result = await SIPDNSManager.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp"));
            var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp"), false, cts.Token);

            //SIPEndPoint resultEP = result.GetSIPEndPoint();

            Assert.NotNull(result);

            logger.LogDebug("resolved to SIP end point {Result}.", result);
        }

        /// <summary>
        /// Tests that the correct end point is resolved for a known sips URI.
        /// </summary>
        [Fact]
        public async Task ResolveHostFromSecureSIPURITest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();

            var result = await SIPDns.ResolveAsync(new SIPURI(null, "sipsorcery.com", null, SIPSchemesEnum.sips, SIPProtocolsEnum.tls), false, cts.Token);

            Assert.NotNull(result);
            Assert.Equal("67.222.131.147", result.Address.ToString());
            Assert.Equal(5061, result.Port);
            Assert.Equal(SIPProtocolsEnum.tls, result.Protocol);

            logger.LogDebug("resolved to SIP end point {Result}.", result);
        }

        /// <summary>
        /// Tests that attempting to resolve a non-existent hostname is handled gracefully.
        /// </summary>
        [Fact]
        public async Task ResolveNonExistentServiceTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();
            var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sipsorceryx.com"), false, cts.Token);

            Assert.Equal(SIPEndPoint.Empty, result);
        }

        /// <summary>
        /// Tests that using a non-responding DNS server is handled gracefully.
        /// </summary>
        [Fact]
        public async Task NonRespondingDNSServerTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var originalClient = SIPDns.LookupClient;

            try
            {
                LookupClientOptions clientOptions = new LookupClientOptions(IPAddress.Parse("127.0.0.2"))
                {
                    Retries = 3,
                    Timeout = TimeSpan.FromSeconds(1),
                    UseCache = true,
                    UseTcpFallback = false
                };

                SIPDns.LookupClient = new LookupClient(clientOptions);

                CancellationTokenSource cts = new CancellationTokenSource();
                var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sipsorcery.com"), false, cts.Token);

                Assert.Equal(SIPEndPoint.Empty, result);
            }
            finally
            {
                SIPDns.LookupClient = originalClient;
            }
        }

        /// <summary>
        /// Tests that a lookup that resolves to a CNAME record works correctly.
        /// </summary>
        [Fact]
        public async Task LookupCNAMETest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            CancellationTokenSource cts = new CancellationTokenSource();

            string hostname = "utest.sipsorcery.com";

            var result = await SIPDns.ResolveAsync(SIPURI.ParseSIPURIRelaxed(hostname), false, cts.Token);

            Assert.NotNull(result);

            logger.LogDebug("resolved to SIP end point {Result}.", result);
        }
    }
}
