using System;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPParametersUnitTest
    {
        [TestMethod]
        public void RouteParamExtractTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string routeParam = ";lr;server=hippo";
            SIPParameters serverParam = new SIPParameters(routeParam, ';');
            string serverParamValue = serverParam.Get("server");

            Console.WriteLine("Parameter string=" + serverParam.ToString() + ".");
            Console.WriteLine("The server parameter is=" + serverParamValue + ".");

            Assert.IsTrue(serverParamValue == "hippo", "The server parameter was not correctly extracted.");
        }

        [TestMethod]
        public void QuotedStringParamExtractTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string methodsParam = ";methods=\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"";
            SIPParameters serverParam = new SIPParameters(methodsParam, ';');
            string methodsParamValue = serverParam.Get("methods");

            Console.WriteLine("Parameter string=" + serverParam.ToString() + ".");
            Console.WriteLine("The methods parameter is=" + methodsParamValue + ".");

            Assert.IsTrue(methodsParamValue == "\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"", "The method parameter was not correctly extracted.");
        }

        [TestMethod]
        public void UserFieldWithNamesExtractTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\"";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            Console.WriteLine("KeyValuePair count=" + keyValuePairs.Length + ".");
            Console.WriteLine("First KetValuePair=" + keyValuePairs[0] + ".");

            Assert.IsTrue(keyValuePairs.Length == 1, "An incorrect number of key value pairs was extracted");
        }

        [TestMethod]
        public void MultipleUserFieldWithNamesExtractTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" , \"Jane Doe\" <sip:jabe@doe.com>";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            Console.WriteLine("KeyValuePair count=" + keyValuePairs.Length + ".");
            Console.WriteLine("First KetValuePair=" + keyValuePairs[0] + ".");
            Console.WriteLine("Second KetValuePair=" + keyValuePairs[1] + ".");

            Assert.IsTrue(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
        }

        [TestMethod]
        public void MultipleUserFieldWithNamesExtraWhitespaceExtractTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string userField = "  \"Joe Bloggs\"   <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" \t,   \"Jane Doe\" <sip:jabe@doe.com>";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            Console.WriteLine("KeyValuePair count=" + keyValuePairs.Length + ".");
            Console.WriteLine("First KetValuePair=" + keyValuePairs[0] + ".");
            Console.WriteLine("Second KetValuePair=" + keyValuePairs[1] + ".");

            Assert.IsTrue(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
        }

        [TestMethod]
        public void GetHashCodeEqualityUnittest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

            string testParamStr2 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

            Assert.IsTrue(testParam1.GetHashCode() == testParam2.GetHashCode(), "The parameters had different hashcode values.");
        }

        [TestMethod]
        public void GetHashCodeDiffOrderEqualityUnittest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

            string testParamStr2 = "ftag=12345;lr;server=hippo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

            Assert.IsTrue(testParam1.GetHashCode() == testParam2.GetHashCode(), "The parameters had different hashcode values.");
        }

        [TestMethod]
        public void GetHashCodeDiffCaseEqualityUnittest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";LR;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            Console.WriteLine("Parameter 1:" + testParam1.ToString());

            string testParamStr2 = "ftag=12345;lr;server=hippo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');
            Console.WriteLine("Parameter 2:" + testParam2.ToString());

            Assert.IsTrue(testParam1.GetHashCode() == testParam2.GetHashCode(), "The parameters had different hashcode values.");
        }

        [TestMethod]
        public void GetHashCodeDiffValueCaseEqualityUnittest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";LR;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            Console.WriteLine("Parameter 1:" + testParam1.ToString());

            string testParamStr2 = "ftag=12345;lr;server=HiPPo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');
            Console.WriteLine("Parameter 2:" + testParam2.ToString());

            Assert.IsTrue(testParam1.GetHashCode() != testParam2.GetHashCode(), "The parameters had different hashcode values.");
        }

        [TestMethod]
        public void EmptyValueParametersUnittest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testParamStr1 = ";emptykey;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            Console.WriteLine("Parameter 1:" + testParam1.ToString());

            Assert.IsTrue(testParam1.Has("emptykey"), "The empty parameter \"emptykey\" was not correctly extracted from the paramter string.");
            Assert.IsTrue(Regex.Match(testParam1.ToString(), "emptykey").Success, "The emptykey name was not in the output parameter string.");
        }
    }
}
