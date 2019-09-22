using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPReplacesParameterUnitTest
    {
        public SIPReplacesParameterUnitTest()
        { }

        [TestMethod]
        public void ParamsInUserPortionURITest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            var replaces = SIPReplacesParameter.Parse(SIPEscape.SIPURIParameterUnescape("a48484fb-ac6e00aa%4010.0.0.12%3Bfrom-tag%3D11e7a0c7ec2ab74eo0%3Bto-tag%3D1313732478"));

            Assert.AreEqual("a48484fb-ac6e00aa@10.0.0.12", replaces.CallID);
            Assert.AreEqual("1313732478", replaces.ToTag);
            Assert.AreEqual("11e7a0c7ec2ab74eo0", replaces.FromTag);

            Console.WriteLine("-----------------------------------------");
        }
    }
}
