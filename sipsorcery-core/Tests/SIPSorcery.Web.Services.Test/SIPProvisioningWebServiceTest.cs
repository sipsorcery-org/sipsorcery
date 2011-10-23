using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Web.Services.Test
{
    [TestClass]
    public class SIPProvisioningWebServiceTest
    {
        [TestMethod]
        public void TestParseCDRCountLambdaExpression()
        {
            var whereExpression = DynamicExpression.ParseLambda<SIPCDRAsset, bool>(@"Dst == ""123""");
            Assert.IsNotNull(whereExpression);
        }

        [TestMethod]
        public void TestParseLikeFromLambdaExpression()
        {
            var whereExpression = DynamicExpression.ParseLambda<SIPCDRAsset, bool>(@"FromHeader.Contains(""123"")");
            Assert.IsNotNull(whereExpression);
        }
    }
}
