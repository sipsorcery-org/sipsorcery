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
using SIPSorcery.UnitTests;
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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string routeParam = ";lr;server=hippo";
            SIPParameters serverParam = new SIPParameters(routeParam, ';');
            string serverParamValue = serverParam.Get("server");

            logger.LogDebug("Parameter string={ParameterString}.", serverParam.ToString());
            logger.LogDebug("The server parameter is={serverParamValue}.", serverParamValue);

            Assert.True(serverParamValue == "hippo", "The server parameter was not correctly extracted.");
        }

        [Fact]
        public void QuotedStringParamExtractTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string methodsParam = ";methods=\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"";
            SIPParameters serverParam = new SIPParameters(methodsParam, ';');
            string methodsParamValue = serverParam.Get("methods");

            logger.LogDebug("Parameter string={ParameterString}.", serverParam.ToString());
            logger.LogDebug("The methods parameter is={methodsParamValue}.", methodsParamValue);

            Assert.True(methodsParamValue == "\"INVITE, MESSAGE, INFO, SUBSCRIBE, OPTIONS, BYE, CANCEL, NOTIFY, ACK, REFER\"", "The method parameter was not correctly extracted.");
        }

        [Fact]
        public void UserFieldWithNamesExtractTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\"";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            logger.LogDebug("KeyValuePair count={KeyValuePairCount}.", keyValuePairs.Length);
            logger.LogDebug("First KetValuePair={FirstKeyValuePair}.", keyValuePairs[0]);

            Assert.True(keyValuePairs.Length == 1, "An incorrect number of key value pairs was extracted");
        }

        [Fact]
        public void MultipleUserFieldWithNamesExtractTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" , \"Jane Doe\" <sip:jabe@doe.com>";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            logger.LogDebug("KeyValuePair count={KeyValuePairCount}.", keyValuePairs.Length);
            logger.LogDebug("First KetValuePair={FirstKeyValuePair}.", keyValuePairs[0]);
            logger.LogDebug("Second KetValuePair={SecondKeyValuePair}.", keyValuePairs[1]);

            Assert.True(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
        }

        [Fact]
        public void MultipleUserFieldWithNamesExtraWhitespaceExtractTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string userField = "  \"Joe Bloggs\"   <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" \t,   \"Jane Doe\" <sip:jabe@doe.com>";
            string[] keyValuePairs = SIPParameters.GetKeyValuePairsFromQuoted(userField, ',');

            logger.LogDebug("KeyValuePair count={KeyValuePairCount}.", keyValuePairs.Length);
            logger.LogDebug("First KetValuePair={FirstKeyValuePair}.", keyValuePairs[0]);
            logger.LogDebug("Second KetValuePair={SecondKeyValuePair}.", keyValuePairs[1]);

            Assert.True(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
        }

        [Fact]
        public void GetHashCodeDiffOrderEqualityUnittest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string testParamStr1 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

            string testParamStr2 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

            Assert.Equal(testParam1, testParam2);
        }

        [Fact]
        public void GetHashCodeDiffOrderEqualityReorderedUnittest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string testParamStr1 = ";lr;server=hippo;ftag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

            string testParamStr2 = "ftag=12345;lr;server=hippo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

            Assert.Equal(testParam1, testParam2);
        }

        [Fact]
        public void CheckEqualWithDiffCaseEqualityUnittest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string testParamStr1 = ";LR;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            logger.LogDebug("Parameter 1={Parameter1}.", testParam1.ToString());

            string testParamStr2 = "ftag=12345;lr;server=hippo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');
            logger.LogDebug("Parameter 2={Parameter2}.", testParam2.ToString());

            Assert.Equal(testParam1, testParam2);
        }

        [Fact]
        public void GetHashCodeDiffValueCaseEqualityUnittest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string testParamStr1 = ";LR;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            logger.LogDebug("Parameter 1={Parameter1}.", testParam1.ToString());

            string testParamStr2 = "ftag=12345;lr;server=HiPPo;";
            SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');
            logger.LogDebug("Parameter 2={Parameter2}.", testParam2.ToString());

            Assert.True(testParam1.GetHashCode() != testParam2.GetHashCode(), "The parameters had different hashcode values.");
        }

        [Fact]
        public void EmptyValueParametersUnittest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string testParamStr1 = ";emptykey;Server=hippo;FTag=12345";
            SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');
            logger.LogDebug("Parameter 1={Parameter1}.", testParam1.ToString());

            Assert.True(testParam1.Has("emptykey"), "The empty parameter \"emptykey\" was not correctly extracted from the parameter string.");
            Assert.True(Regex.Match(testParam1.ToString(), "emptykey").Success, "The emptykey name was not in the output parameter string.");
        }

        /// <summary>
        /// Guards the span based Initialise rewrite. A quoted parameter value that contains the
        /// parameter delimiter must not be split at the embedded delimiter.
        /// </summary>
        [Fact]
        public void QuotedValueWithEmbeddedDelimiterTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string paramStr = ";text=\"a;b;c\";lr";
            SIPParameters parameters = new SIPParameters(paramStr, ';');

            logger.LogDebug("Parameter string={ParameterString}.", parameters.ToString());

            Assert.Equal("\"a;b;c\"", parameters.Get("text"));
            Assert.True(parameters.Has("lr"), "The lr flag parameter was not extracted.");
            Assert.Equal(2, parameters.GetKeys().Length);
        }

        /// <summary>
        /// A quoted parameter value that contains the name/value separator ('=') must keep the
        /// embedded '=' as part of the value rather than splitting on it.
        /// </summary>
        [Fact]
        public void QuotedValueWithEmbeddedSeparatorTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string paramStr = ";data=\"key=value\";lr";
            SIPParameters parameters = new SIPParameters(paramStr, ';');

            logger.LogDebug("Parameter string={ParameterString}.", parameters.ToString());

            Assert.Equal("\"key=value\"", parameters.Get("data"));
            Assert.True(parameters.Has("lr"), "The lr flag parameter was not extracted.");
        }

        /// <summary>
        /// A parameter string with a single key and no delimiter must parse to a single flag parameter.
        /// Exercises the no-delimiter early-out branch in the Initialise rewrite.
        /// </summary>
        [Fact]
        public void SingleFlagParameterNoDelimiterTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            SIPParameters parameters = new SIPParameters("lr", ';');

            Assert.True(parameters.Has("lr"), "The lr flag parameter was not extracted.");
            Assert.Null(parameters.Get("lr"));
            Assert.Single(parameters.GetKeys());
        }

        /// <summary>
        /// Empty and whitespace only parameter strings must produce an empty parameter collection
        /// and a null ToString output.
        /// </summary>
        [Fact]
        public void EmptyAndWhitespaceParametersTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            // GetKeys() returns null (not an empty array) when no parameters are present.
            SIPParameters empty = new SIPParameters("", ';');
            Assert.Null(empty.GetKeys());
            Assert.Null(empty.ToString());

            SIPParameters whitespace = new SIPParameters("   ", ';');
            Assert.Null(whitespace.GetKeys());
            Assert.Null(whitespace.ToString());
        }

        /// <summary>
        /// Round-trip: a parameter set serialised via ToString() must re-parse into an equivalent
        /// collection (covers the StringBuilder based ToString rewrite).
        /// </summary>
        [Fact]
        public void ParameterRoundTripTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string paramStr = ";lr;server=hippo;ftag=12345";
            SIPParameters original = new SIPParameters(paramStr, ';');

            string serialised = original.ToString();
            logger.LogDebug("Serialised parameters={Serialised}.", serialised);

            SIPParameters reparsed = new SIPParameters(serialised, ';');

            Assert.Equal(original.GetKeys().Length, reparsed.GetKeys().Length);
            Assert.True(reparsed.Has("lr"), "The lr flag parameter did not survive the round-trip.");
            Assert.Equal("hippo", reparsed.Get("server"));
            Assert.Equal("12345", reparsed.Get("ftag"));
        }
    }
}
