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

using System;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserFieldUnitTest
    {
        [Fact]
        public void ParamsInUserPortionURITest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPUserField userField = SIPUserField.ParseSIPUserField("<sip:C=on;t=DLPAN@10.0.0.1:5060;lr>");

            Assert.True("C=on;t=DLPAN" == userField.URI.User, "SIP user portion parsed incorrectly.");
            Assert.True("10.0.0.1:5060" == userField.URI.Host, "SIP host portion parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
