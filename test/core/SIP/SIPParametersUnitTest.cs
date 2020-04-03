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

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPParametersUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPParametersUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void RouteParamExtractTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string routeParam = ";lr;server=hippo";
            SIPParameters serverParam = new SIPParameters(routeParam, ';');
            string serverParamValue = serverParam.Get("server");

            logger.LogDebug("Parameter string=" + serverParam.ToString() + ".");
            logger.LogDebug("The server parameter is=" + serverParamValue + ".");

            Assert.True(serverParamValue == "hippo", "The server parameter was not correctly extracted.");
        }

        [Fact]
        public void QuotedStringParamExtractTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string methodsParam = ";methods=\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"";
            SIPParameters serverParam = new SIPParameters(methodsParam, ';');
            string methodsParamValue = serverParam.Get("methods");

            logger.LogDebug("Parameter string=" + serverParam.ToString() + ".");
            logger.LogDebug("The methods parameter is=" + methodsParamValue + ".");

            Assert.True(methodsParamValue == "\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"", "The method parameter was not correctly extracted.");
        }

        [Fact]
        public void UserFieldWithNamesExtractTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\"";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            logger.LogDebug("KeyValuePair count=" + keyValuePairs.Length + ".");
            logger.LogDebug("First KetValuePair=" + keyValuePairs[0] + ".");

            Assert.True(keyValuePairs.Length == 1, "An incorrect number of key value pairs was extracted");
        }

        [Fact]
        public void MultipleUserFieldWithNamesExtractTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" , \"Jane Doe\" <sip:jabe@doe.com>";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            logger.LogDebug("KeyValuePair count=" + keyValuePairs.Length + ".");
            logger.LogDebug("First KetValuePair=" + keyValuePairs[0] + ".");
            logger.LogDebug("Second KetValuePair=" + keyValuePairs[1] + ".");

            Assert.True(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
        }

        [Fact]
        public void MultipleUserFieldWithNamesExtraWhitespaceExtractTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string userField = "  \"Joe Bloggs\"   <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" \t,   \"Jane Doe\" <sip:jabe@doe.com>";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            logger.LogDebug("KeyValuePair count=" + keyValuePairs.Length + ".");
            logger.LogDebug("First KetValuePair=" + keyValuePairs[0] + ".");
            logger.LogDebug("Second KetValuePair=" + keyValuePairs[1] + ".");

            Assert.True(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
        }

        [Fact]
        public void GetHashCodeDiffOrderEqualityUnittest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

            string testParamStr2 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

            Assert.Equal(testParam1, testParam2);
        }

        [Fact]
        public void GetHashCodeDiffOrderEqualityReorderedUnittest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

            string testParamStr2 = "ftag=12345;lr;server=hippo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

            Assert.Equal(testParam1, testParam2);
        }

        [Fact]
        public void CheckEqualWithDiffCaseEqualityUnittest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";LR;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            logger.LogDebug("Parameter 1:" + testParam1.ToString());

            string testParamStr2 = "ftag=12345;lr;server=hippo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');
            logger.LogDebug("Parameter 2:" + testParam2.ToString());

            Assert.Equal(testParam1, testParam2);
        }

        [Fact]
        public void GetHashCodeDiffValueCaseEqualityUnittest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";LR;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            logger.LogDebug("Parameter 1:" + testParam1.ToString());

            string testParamStr2 = "ftag=12345;lr;server=HiPPo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');
            logger.LogDebug("Parameter 2:" + testParam2.ToString());

            Assert.True(testParam1.GetHashCode() != testParam2.GetHashCode(), "The parameters had different hashcode values.");
        }

        [Fact]
        public void EmptyValueParametersUnittest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";emptykey;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            logger.LogDebug("Parameter 1:" + testParam1.ToString());

            Assert.True(testParam1.Has("emptykey"), "The empty parameter \"emptykey\" was not correctly extracted from the parameter string.");
            Assert.True(Regex.Match(testParam1.ToString(), "emptykey").Success, "The emptykey name was not in the output parameter string.");
        }
    }
}
