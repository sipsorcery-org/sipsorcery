//-----------------------------------------------------------------------------
// Filename: SIPDNSManagerUnitTest.cs
//
// Description: Unit tests for the SIP specific DNS lookup logic.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 14 OCt 2019	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "integration")]
    public class SIPDNSManagerUnitTest
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// Tests that an IP address can be reoslved when the resolution can only be done via a SRV record.
        /// </summary>
        [Fact]
        public void ResolveHostFromServiceTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            var result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed("sipsorcery.com"), false);

            SIPEndPoint resultEP = result.GetSIPEndPoint();

            Assert.NotNull(resultEP);

            logger.LogDebug($"resolved to SIP end point {resultEP}");
        }

        /// <summary>
        /// Tests that an attempt to lookup the a hostname that's not fully qualified works correctly.
        /// </summary>
        [Fact]
        public void LookupLocalHostnameTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            string hostname = System.Net.Dns.GetHostName();

            var result = SIPDNSManager.ResolveSIPService(SIPURI.ParseSIPURIRelaxed(hostname), false);

            SIPEndPoint resultEP = result.GetSIPEndPoint();

            Assert.NotNull(resultEP);

            logger.LogDebug($"resolved to SIP end point {resultEP}");
        }
    }
}
