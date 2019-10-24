//-----------------------------------------------------------------------------
// Filename: DNSUnitTest.cs
//
// Description: Unit tests for the DNS lookup classes used in the SIPSorcery library.
// 
// History:
// 14 OCt 2019	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com)
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
    }
}