//-----------------------------------------------------------------------------
// Filename: DNSUnitTest.cs
//
// Description: Unit tests for the DNS lookup classes used in the SIPSorcery library.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 14 Oct 2019	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Heijden.DNS;
using SIPSorcery.SIP;

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class DNSUnitTest
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// Test that a known A record is resolved.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void LookupARecordMethod()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DNSResponse result = DNSManager.Lookup("www.sipsorcery.com", QType.A, 10, null, false, false);

            logger.LogDebug($"Lookup result {result.RecordsA[0].Address}.");

            Assert.AreEqual(result.RecordsA[0].Address.ToString(), "67.222.131.148");
        }

        /// <summary>
        /// Test that a known AAAA record is resolved.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void LookupAAAARecordMethod()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DNSResponse result = DNSManager.Lookup("www.google.com", QType.AAAA, 10, null, false, false);

            foreach (var aaaaResult in result.RecordsAAAA)
            {
                logger.LogDebug($"AAAA Lookup result {aaaaResult.ToString()}.");
            }

            Assert.IsTrue(result.RecordsAAAA.Length > 0);
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void LookupSrvRecordMethod()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            DNSResponse result = DNSManager.Lookup(SIPDNSConstants.SRV_SIP_UDP_QUERY_PREFIX + "sipsorcery.com", QType.SRV, 10, null, false, false);

            foreach (var srvResult in result.RecordSRV)
            {
                logger.LogDebug($"SRV Lookup result {srvResult.ToString()}.");
            }

            Assert.AreEqual(result.RecordSRV.Length, 1);
            Assert.AreEqual(result.RecordSRV.First().TARGET, "sip.sipsorcery.com.");
        }

        /// <summary>
        /// Test that a non qualified hostname can be looked up.
        /// </summary>
        [TestMethod]
        [TestCategory("Integration")]
        public void LookupCurrentHostNameMethod()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            logger.LogDebug($"Current host name {System.Net.Dns.GetHostName()}");

            var addressList = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList;

            addressList.ToList().ForEach(x => logger.LogDebug(x.ToString()));

            Assert.IsTrue(addressList.Count() > 0, "No address results were returned for a local hostname lookup.");
        }
    }
}