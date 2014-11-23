//-----------------------------------------------------------------------------
// Filename: SIPParameters.cs
//
// Description: SIP parameters as used in Contact, To, From and Via SIP headers.
//
// History:
// 06 May 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Represents a series of name value pairs that are optionally included in SIP URIs and also as an additional
    /// optional setting on some SIP Headers (Contact, To, From, Via).
    /// This class also treats the header value of a SIP URI as a special case of a SIP parameter. The difference between
    /// a paramter and a SIP URI header is the start and delimiter characters used.
    ///
    /// SIP URI with parameters:
    /// sip:1234@sip.com;key1=value1;key2=value2
    /// 
    /// SIP URI with headers:
    /// sip:1234@sip.com?key1=value1&key2=value2
    /// 
    /// SIP URI with parameters and headers (paramters always come first):
    /// sip:1234@sip.com;key1=value1;key2=value2?key1=value1&key2=value2
    /// </summary>
    /// <bnf>
    /// generic-param  =  token [ EQUAL gen-value ]
    /// gen-value      =  token / host / quoted-string
    /// </bnf>
    [DataContract]
	public class SIPParameters
	{
        private const char TAG_NAME_VALUE_SEPERATOR = '=';
        private const char QUOTE = '"';
        private const char BACK_SLASH = '\\';
        private const char DEFAULT_PARAMETER_DELIMITER = ';';

        private static ILog logger = AssemblyState.logger;

        [DataMember]
        public char TagDelimiter = DEFAULT_PARAMETER_DELIMITER;

        //[IgnoreDataMember]
        [DataMember]
        public Dictionary<string, string> m_dictionary = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);

        [IgnoreDataMember]
        public int Count
        {
            get { return (m_dictionary != null) ? m_dictionary.Count : 0;  }
        }
        
        /// <summary>
        /// Parses the name value pairs from a SIP parameter or header string.
        /// </summary>
        public SIPParameters(string sipString, char delimiter)
        {
            Initialise(sipString, delimiter);
        }

        private void Initialise(string sipString, char delimiter)
        {
            TagDelimiter = delimiter;

            string[] keyValuePairs = GetKeyValuePairsFromQuoted(sipString, delimiter);

            if (keyValuePairs != null && keyValuePairs.Length > 0)
            {
                foreach (string keyValuePair in keyValuePairs)
                {
                    AddKeyValuePair(keyValuePair, m_dictionary);
                }
            }   
        }

        public static string[] GetKeyValuePairsFromQuoted(string quotedString, char delimiter)
        {
            try
            {
                List<string> keyValuePairList = new List<string>();

                if (quotedString == null || quotedString.Trim().Length == 0)
                {
                    return null;
                }
                else if(quotedString.IndexOf(delimiter) == -1)
                {
                    //return quotedString.Split(delimiter);
                    return new string[] {quotedString};
                }
                else
                {
                    int startParameterPosn = 0;
                    int inParameterPosn = 0;
                    bool inQuotedStr = false;

                    while (inParameterPosn != -1 && inParameterPosn < quotedString.Length)
                    {
                        inParameterPosn = quotedString.IndexOfAny(new char[] { delimiter, QUOTE }, inParameterPosn);

                        // Determine if the delimiter position represents the end of the parameter or is in a quoted string.
                        if (inParameterPosn != -1)
                        {
                            if (inParameterPosn <= startParameterPosn && quotedString[inParameterPosn] == delimiter)
                            {
                                // Initial or doubled up Parameter delimiter character, ignore and move on.
                                inQuotedStr = false;
                                inParameterPosn++;
                                startParameterPosn = inParameterPosn;
                            }
                            else if (quotedString[inParameterPosn] == QUOTE)
                            {
                                if (inQuotedStr && inParameterPosn > 0 && quotedString[inParameterPosn - 1] != BACK_SLASH)
                                {
                                    // If in a quoted string and this quote has not been escaped close the quoted string.
                                    inQuotedStr = false;
                                }
                                else if (inQuotedStr && inParameterPosn > 0 && quotedString[inParameterPosn - 1] == BACK_SLASH)
                                {
                                    // Do nothing, quote has been escaped in a quoted string.
                                }
                                else if (!inQuotedStr)
                                {
                                    // Start quoted string.
                                    inQuotedStr = true;
                                }

                                inParameterPosn++;
                            }
                            else
                            {
                                if (!inQuotedStr)
                                {
                                    // Parameter delimiter found and not in quoted string therefore this is a parameter separator.
                                    string keyValuePair = quotedString.Substring(startParameterPosn, inParameterPosn - startParameterPosn);

                                    keyValuePairList.Add(keyValuePair);

                                    inParameterPosn++;
                                    startParameterPosn = inParameterPosn;
                                }
                                else
                                {
                                    // Do nothing, separator character is within a quoted string.
                                    inParameterPosn++;
                                }
                            }
                        }
                    }

                    // Add the last parameter.
                    if (startParameterPosn < quotedString.Length)
                    {
                        // Parameter delimiter found and not in quoted string therefore this is a parameter separator.
                        keyValuePairList.Add(quotedString.Substring(startParameterPosn));
                    }
                }

                return keyValuePairList.ToArray();
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetKeyValuePairsFromQuoted. " + excp.Message);
                throw excp;
            }
        }

        private void AddKeyValuePair(string keyValuePair, Dictionary<string, string> dictionary)
        {
            if (keyValuePair != null && keyValuePair.Trim().Length > 0)
            {
                int seperatorPosn = keyValuePair.IndexOf(TAG_NAME_VALUE_SEPERATOR);
                if (seperatorPosn != -1)
                {
                    string keyName = keyValuePair.Substring(0, seperatorPosn).Trim();

                    // If this is not the parameter that is being removed put it back on.
                    if (!dictionary.ContainsKey(keyName))
                    {
                        dictionary.Add(keyName, keyValuePair.Substring(seperatorPosn + 1).Trim());
                    }
                }
                else
                {
                    // Keys with no values are valid in SIP so they get added to the collection with a null value.
                    if (!dictionary.ContainsKey(keyValuePair))
                    {
                        dictionary.Add(keyValuePair, null);
                    }
                }
            }
        }

        public void Set(string name, string value)
        {
            if (m_dictionary.ContainsKey(name))
            {
                m_dictionary[name] = value;
            }
            else
            {
                m_dictionary.Add(name, value);
            }
        }

        public string Get(string name)
        {
            if (m_dictionary != null || m_dictionary.Count == 0)
            {
                if (m_dictionary.ContainsKey(name))
                {
                    return SIPEscape.SIPURIParameterUnescape(m_dictionary[name]);
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        public bool Has(string name)
        {
            if (m_dictionary != null)
            {
                return m_dictionary.ContainsKey(name);
            }
            else
            {
                return false;
            }
        }

        public void Remove(string name)
        {
            if (name != null)
            {
                m_dictionary.Remove(name);
            }
        }

        public void RemoveAll()
        {
            m_dictionary = new Dictionary<string, string>();
        }

        public string[] GetKeys()
        {
            if (m_dictionary == null || m_dictionary.Count == 0)
            {
                return null;
            }
            else
            {
                string[] keys = new string[m_dictionary.Count];
                int index = 0;
                foreach (KeyValuePair<string, string> entry in m_dictionary)
                {
                    keys[index++] = entry.Key as string;
                }

                return keys;
            }
        }

        public new string ToString() {
            string paramStr = null;

            if (m_dictionary != null) {
                foreach (KeyValuePair<string, string> param in m_dictionary) {
                    if (param.Value != null && param.Value.Trim().Length > 0) {
                        paramStr += TagDelimiter + param.Key + TAG_NAME_VALUE_SEPERATOR + SIPEscape.SIPURIParameterEscape(param.Value);
                    }
                    else {
                        paramStr += TagDelimiter + param.Key;
                    }
                }
            }

            return paramStr;
        }

        public override int GetHashCode()
        {
            if (m_dictionary != null && m_dictionary.Count > 0)
            {
                SortedList sortedParams = new SortedList();
                foreach (KeyValuePair<string, string> param in m_dictionary)
                {
                    sortedParams.Add(param.Key.ToLower(), (string)param.Value);
                }

                StringBuilder sortedParamBuilder = new StringBuilder();
                foreach (DictionaryEntry sortedEntry in sortedParams)
                {
                    sortedParamBuilder.Append((string)sortedEntry.Key + (string)sortedEntry.Value);
                }

                return sortedParamBuilder.ToString().GetHashCode();
            }
            else
            {
                return 0;
            }
        }

        public SIPParameters CopyOf()
        {
            SIPParameters copy = new SIPParameters(ToString(), TagDelimiter);
            return copy;
        }
						
		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class SIPParamsUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}
			
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}
			
			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
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

            [Test]
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

            [Test]
            public void UserFieldWithNamesExtractTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\"";
                string[] keyValuePairs = GetKeyValuePairsFromQuoted(userField, ',');

                Console.WriteLine("KeyValuePair count=" + keyValuePairs.Length + ".");
                Console.WriteLine("First KetValuePair=" + keyValuePairs[0] + ".");

                Assert.IsTrue(keyValuePairs.Length == 1, "An incorrect number of key value pairs was extracted");
            }

            [Test]
            public void MultipleUserFieldWithNamesExtractTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string userField = "\"Joe Bloggs\" <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" , \"Jane Doe\" <sip:jabe@doe.com>";
                string[] keyValuePairs = GetKeyValuePairsFromQuoted(userField, ',');

                Console.WriteLine("KeyValuePair count=" + keyValuePairs.Length + ".");
                Console.WriteLine("First KetValuePair=" + keyValuePairs[0] + ".");
                Console.WriteLine("Second KetValuePair=" + keyValuePairs[1] + ".");

                Assert.IsTrue(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
            }

            [Test]
            public void MultipleUserFieldWithNamesExtraWhitespaceExtractTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string userField = "  \"Joe Bloggs\"   <sip:joe@bloggs.com>;allow=\"options, invite, cancel\" \t,   \"Jane Doe\" <sip:jabe@doe.com>";
                string[] keyValuePairs = GetKeyValuePairsFromQuoted(userField, ',');

                Console.WriteLine("KeyValuePair count=" + keyValuePairs.Length + ".");
                Console.WriteLine("First KetValuePair=" + keyValuePairs[0] + ".");
                Console.WriteLine("Second KetValuePair=" + keyValuePairs[1] + ".");

                Assert.IsTrue(keyValuePairs.Length == 2, "An incorrect number of key value pairs was extracted");
            }

            [Test]
            public void GetHashCodeEqualityUnittest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testParamStr1 = ";lr;server=hippo;ftag=12345";
                SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

                string testParamStr2 = ";lr;server=hippo;ftag=12345";
                SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

                Assert.IsTrue(testParam1.GetHashCode() == testParam2.GetHashCode(), "The parameters had different hashcode values.");
            }

            [Test]
            public void GetHashCodeDiffOrderEqualityUnittest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testParamStr1 = ";lr;server=hippo;ftag=12345";
                SIPParameters testParam1 = new SIPParameters(testParamStr1, ';');

                string testParamStr2 = "ftag=12345;lr;server=hippo;";
                SIPParameters testParam2 = new SIPParameters(testParamStr2, ';');

                Assert.IsTrue(testParam1.GetHashCode() == testParam2.GetHashCode(), "The parameters had different hashcode values.");
            }

            [Test]
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

            [Test]
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

            [Test]
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

		#endif

		#endregion
	}
}
