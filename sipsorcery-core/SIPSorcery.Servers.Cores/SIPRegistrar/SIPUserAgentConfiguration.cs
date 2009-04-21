// ============================================================================
// FileName: SIPUserAgentConfiguration.cs
//
// Description:
// This class describes the settings for SIP user agents for use by the SIP Registrar. This allows
// the registrar to apply different behaviour for different user agents. Some user agents only work
// with a specific expiry time, others will only recognise a response if the Contact header is returned
// exactly as sent rather than as a list of all current contacts as the standard mandates.
//
// Author(s):
// Aaron Clauson
//
// History:
// 07 Sep 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPUserAgentConfiguration
    {
        private static ILog logger = AppState.logger;

        public int MaxAllowedExpiryTime = 3600;     // Overrules the default max expiry time the Registrar is using and allows specific user agents to have expirys up to this value.
        public bool ContactListSupported = true;    // If false means the user agent wants only the Contact header it supplied returned in the Ok response.
        public string UserAgentRegex = null;        // The regular expression string being used to match the user agent.

        public SIPUserAgentConfiguration(int maxExpiry, bool listSupported, string userAgentRegex)
        {
            MaxAllowedExpiryTime = maxExpiry;
            ContactListSupported = listSupported;
            UserAgentRegex = userAgentRegex;
        }

        public static Dictionary<string, SIPUserAgentConfiguration> ParseSIPUserAgentConfigurations(XmlNode userAgentConifgNode)
        {
            try
            {
                Dictionary<string, SIPUserAgentConfiguration> userAgentConfigs = new Dictionary<string, SIPUserAgentConfiguration>();

                if (userAgentConifgNode != null && userAgentConifgNode.ChildNodes.Count != 0)
                {
                    foreach (XmlNode userAgentNode in userAgentConifgNode.SelectNodes("useragent"))
                    {
                        if (userAgentNode.InnerText != null && userAgentNode.InnerText.Trim().Length > 0)
                        {
                            int expiry = Convert.ToInt32(userAgentNode.Attributes.GetNamedItem("expiry").Value);
                            bool contactListSupported = (userAgentNode.Attributes.GetNamedItem("contactlists") != null) ? Convert.ToBoolean(userAgentNode.Attributes.GetNamedItem("contactlists").Value) : true;
                            SIPUserAgentConfiguration userAgentConfig = new SIPUserAgentConfiguration(expiry, contactListSupported, userAgentNode.InnerText);

                            if (userAgentConfig.UserAgentRegex != null && userAgentConfig.UserAgentRegex.Trim().Length > 0 && !userAgentConfigs.ContainsKey(userAgentConfig.UserAgentRegex))
                            {
                                logger.Debug("Added useragent config, useragent=" + userAgentConfig.UserAgentRegex + ", expiry=" + userAgentConfig.MaxAllowedExpiryTime + "s, contact lists=" + userAgentConfig.ContactListSupported + ".");
                                userAgentConfigs.Add(userAgentConfig.UserAgentRegex, userAgentConfig);
                            }
                        }
                    }
                }

                return userAgentConfigs;
            }
            catch (Exception excp)
            {
                logger.Error("Exception ParseSIPUserAgentConfigurations. " + excp.Message);
                return null;
            }
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
		public class SIPRegistrarRecordUnitTest
		{

            [TestFixtureSetUp]
            public void Init()
            { }
                
            [TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}

            [Test]
            public void GetExpiryUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string registrarExpiryNode = 
                    "<registrarexpirys>" +
                    " <useragents>" +
                    "  <useragent expiry='3600'>fring</useragent>" +
                    "  <useragent expiry='113'>*</useragent>" +
                    " </useragents>" +
                    "</registrarexpirys>";
                XmlDocument regExpiryDom = new XmlDocument();
                regExpiryDom.LoadXml(registrarExpiryNode);

                UserAgentMaxExpiryNodes = regExpiryDom.DocumentElement;

                int fringExpiry = GetExpiry(3600, "fring");

                Assert.IsTrue(fringExpiry == 3600, "The expiry value for the fring ua was incorrect.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void GetCiscoExpiryUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string registrarExpiryNode =
                    "<registrarexpirys>" +
                    " <useragents>" +
                    "  <useragent expiry='3600'>fring</useragent>" +
                    "  <useragent expiry='300'>Cisco-CP7960G/8.0</useragent>" +
                    "  <useragent expiry='113'>*</useragent>" +
                    " </useragents>" +
                    "</registrarexpirys>";
                XmlDocument regExpiryDom = new XmlDocument();
                regExpiryDom.LoadXml(registrarExpiryNode);

                UserAgentMaxExpiryNodes = regExpiryDom.DocumentElement;

                int ciscoExpiry = GetExpiry(500, "Cisco-CP7960G/8.0");

                Assert.IsTrue(ciscoExpiry == 300, "The expiry value for the Cisco ua was incorrect, " + ciscoExpiry + ".");

                Console.WriteLine("---------------------------------");
            }


            [Test]
            public void GetDefaultExpiryUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string registrarExpiryNode =
                    "<registrarexpirys>" +
                    " <useragents>" +
                    "  <useragent expiry='3600'>fring</useragent>" +
                    "  <useragent expiry='113'>*</useragent>" +
                    " </useragents>" +
                    "</registrarexpirys>";
                XmlDocument regExpiryDom = new XmlDocument();
                regExpiryDom.LoadXml(registrarExpiryNode);

                UserAgentMaxExpiryNodes = regExpiryDom.DocumentElement;

                int fringExpiry = GetExpiry(DEFAULT_EXPIRY_SECONDS, "cisco");

                Assert.IsTrue(fringExpiry == DEFAULT_EXPIRY_SECONDS, "The expiry value for the unknown ua was incorrect.");

                Console.WriteLine("---------------------------------");
            }
        }

        #endif

        #endregion
    }
}
