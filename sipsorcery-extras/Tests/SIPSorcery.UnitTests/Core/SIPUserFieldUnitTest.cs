using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPUserFieldUnitTest
    {
        public SIPUserFieldUnitTest()
        {    }

        private TestContext testContextInstance;

        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

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
