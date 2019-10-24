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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPUserFieldUnitTest
    {
        [TestMethod]
        public void ParamsInUserPortionURITest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPUserField userField = SIPUserField.ParseSIPUserField("<sip:C=on;t=DLPAN@10.0.0.1:5060;lr>");

            Assert.IsTrue("C=on;t=DLPAN" == userField.URI.User, "SIP user portion parsed incorrectly.");
            Assert.IsTrue("10.0.0.1:5060" == userField.URI.Host, "SIP host portion parsed incorrectly.");

            Console.WriteLine("-----------------------------------------");
        }
    }
}
