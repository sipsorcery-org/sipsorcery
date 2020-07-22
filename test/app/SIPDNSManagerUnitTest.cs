//-----------------------------------------------------------------------------
// Filename: SIPDNSManagerUnitTest.cs
//
// Description: Unit tests for the SIP specific DNS lookup logic.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "dns")]
    public class SIPDNSManagerUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPDNSManagerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that an IP address can be resolved when the resolution can only be done via a SRV record.
        /// </summary>
        [Fact]
        public void ResolveHostFromServiceTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            //var result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed("sipsorcery.com"), false);
            var result = SIPDns.Resolve(SIPURI.ParseSIPURIRelaxed("sipsorcery.com"), false).Result;

            Assert.NotNull(result);

            logger.LogDebug($"resolved to SIP end point {result}.");
        }

        /// <summary>
        /// Tests that an attempt to lookup the a hostname that's not fully qualified works correctly.
        /// </summary>
        [Fact]
        public void LookupLocalHostnameTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string hostname = System.Net.Dns.GetHostName();

            if (hostname.EndsWith(SIPDns.MDNS_TLD))
            {
                // TODO: Look into why DNS calls on macos cannot resolve domains ending in ".local"
                // RFC6762 domains.
                logger.LogWarning("Skipping unit test LookupLocalHostnameTest due to RFC6762 domain.");
            }
            else
            {
                //var result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed(hostname), false);
                var result = SIPDns.Resolve(SIPURI.ParseSIPURIRelaxed(hostname)).Result;

                Assert.NotNull(result);

                logger.LogDebug($"resolved to SIP end point {result}.");
            }
        }

        [Fact]
        public void ResolveSIPServiceTest()
        {
            try
            {
                logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
                logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

                //SIPDNSManager.UseNAPTRLookups = true;

                //var result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp"), false);
                var result = SIPDns.Resolve(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp")).Result;

                Assert.NotNull(result);
                logger.LogDebug($"resolved to SIP end point {result}.");
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
        public void ResolveSIPServiceFromCacheTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPURI lookupURI = SIPURI.ParseSIPURIRelaxed("sip:tel.t-online.de");
            //var result = SIPDNSManager.ResolveSIPService(lookupURI, false);
            var result = SIPDns.Resolve(lookupURI).Result;
            Assert.NotNull(result);

            //SIPEndPoint resultEP = result.GetSIPEndPoint();
            Assert.NotNull(result);
            logger.LogDebug($"resolved to SIP end point {result}.");
            //Assert.NotEmpty(result.SIPSRVResults);
            //Assert.NotEmpty(result.EndPointResults);

            // Do the same look up again immediately to check the result when it comes from the in-memory cache.
            var resultCache = SIPDns.Resolve(lookupURI).Result;
            Assert.NotNull(resultCache);
            logger.LogDebug($"cache resolved to SIP end point {resultCache}.");
        }

        [Fact]
        public async Task ResolveSIPServiceAsyncTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            //var result = await SIPDNSManager.ResolveAsync(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp"));
            var result = await SIPDns.Resolve(SIPURI.ParseSIPURIRelaxed("sip:reg.sip-trunk.telekom.de;transport=tcp"));

            //SIPEndPoint resultEP = result.GetSIPEndPoint();

            Assert.NotNull(result);

            logger.LogDebug($"resolved to SIP end point {result}.");
        }

        /// <summary>
        /// Tests that the correct end point is resolved for a known sips URI.
        /// </summary>
        [Fact]
        public void ResolveHostFromSecureSIPURITest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var result = SIPDns.Resolve(new SIPURI(null, "sipsorcery.com", null, SIPSchemesEnum.sips, SIPProtocolsEnum.tls), false).Result;

            Assert.NotNull(result);
            Assert.Equal("67.222.131.147", result.Address.ToString());
            Assert.Equal(5061, result.Port);
            Assert.Equal(SIPProtocolsEnum.tls, result.Protocol);

            logger.LogDebug($"resolved to SIP end point {result}.");
        }
    }
}
