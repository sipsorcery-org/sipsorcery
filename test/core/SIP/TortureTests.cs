//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    /// <summary>
    /// Torture tests from RFC4475 https://tools.ietf.org/html/rfc4475
    /// Tests must be extracted from the base64 blob at the bottom of the RFC:
    /// $ cat torture.b64 | base64 -d > torture.tar.gz  
    /// $ tar zxvf torture.tar.gz
    /// Which gives the dat files needed.
    /// Cutting and pasting is no good as things like white space getting interpreted as end of line screws up
    /// intent of the tests.
    /// </summary>
    [Trait("Category", "unit")]
    public class SIPTortureTests
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// Torture test 3.1.1.1. with file wsinv.dat.
        /// </summary>
        [Fact(Skip = "Bit trickier to pass than anticipated.")]
        public void ShortTorturousInvite()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Assert.True(File.Exists("wsinv.dat"), "The wsinv.dat torture test input file was missing.");

            string raw = File.ReadAllText("wsinv.dat");

            logger.LogDebug(raw);

            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(Encoding.UTF8.GetBytes(raw), null, null);
            SIPRequest inviteReq = SIPRequest.ParseSIPRequest(raw);

            Assert.NotNull(sipMessageBuffer);
            Assert.NotNull(inviteReq);

            logger.LogDebug("-----------------------------------------");
        }
    }
}
