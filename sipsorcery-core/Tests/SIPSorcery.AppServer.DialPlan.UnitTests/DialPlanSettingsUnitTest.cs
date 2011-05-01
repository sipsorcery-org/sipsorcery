using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.AppServer.DialPlan;
using SIPSorcery.Entities;

namespace SIPSorcery.AppServer.DialPlan.UnitTests
{
    [TestClass]
    public class DialPlanSettingsUnitTest
    {
        public DialPlanSettingsUnitTest()
        {  }

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
        public void ExtractENUMServersTest()
        {
            SIPDialplanOption options = new SIPDialplanOption()
            {
                ENUMServers = "e164.org\r\n\r\ne164.info\r\n\r\ne164.arpa\r\n\r\ne164.televolution.net\r\n\r\nenum.org\r\n\r\n"
            };

            DialPlanSettings settings = new DialPlanSettings(null, null, null, options);

            List<string> enumServersList = settings.GetENUMServers();

            Assert.AreEqual(5, enumServersList.Count, "The number of ENUM servers extracted from the list was incorrect.");
            Assert.AreEqual("e164.org", enumServersList[0], "The ENUM server at index 0 was not extracted correctly.");
            Assert.AreEqual("enum.org", enumServersList[4], "The ENUM server at index 4 was not extracted correctly.");
        }

        [TestMethod]
        public void ExtractExcludedPrefixesTest()
        {
            SIPDialplanOption options = new SIPDialplanOption()
            {
                ExcludedPrefixes = " 1 (900 | 809)\r\n\r\n 1 \\d\\d\\d 555 1212\r\n\r\n44 (9 | 55 | 70 | 84 | 87)"
            };

            DialPlanSettings settings = new DialPlanSettings(null, null, null, options);

            List<string> excludedPrefixesList = settings.GetExcludedPrefixes();

            Assert.AreEqual(3, excludedPrefixesList.Count, "The number of excluded prefixes extracted from the list was incorrect.");
            Assert.AreEqual(" 1 (900 | 809)", excludedPrefixesList[0], "The excluded prefixes at index 0 was not extracted correctly.");
            Assert.AreEqual(" 1 \\d\\d\\d 555 1212", excludedPrefixesList[1], "The excluded prefixes at index 1 was not extracted correctly.");
            Assert.AreEqual("44 (9 | 55 | 70 | 84 | 87)", excludedPrefixesList[2], "The excluded prefixes at index 2 was not extracted correctly.");
        }

        [TestMethod]
        public void ExtractTimezoneOffsetTest()
        {
            SIPDialplanOption options = new SIPDialplanOption()
            {
                Timezone = "(UTC+10:00) Hobart"
            };

            DialPlanSettings settings = new DialPlanSettings(null, null, null, options);

            int timezoneOffset = settings.GetTimezoneOffset();

            Assert.IsTrue(timezoneOffset >= 600 && timezoneOffset <= 660, "The timezone offset extracted was incorrect.");
        }

        [TestMethod]
        public void ExtractAllowedCountriesTest()
        {
            SIPDialplanOption options = new SIPDialplanOption()
            {
                AllowedCountryCodes = "1 33 36 37[0-2] 380 39 41 420 44 49 61 7 86 883 886 90 972 998"
            };

            DialPlanSettings settings = new DialPlanSettings(null, null, null, options);

            List<string> allowedCountriesList = settings.GetAllowedCountries();

            Assert.AreEqual(18, allowedCountriesList.Count, "The number of allowed countries extracted from the list was incorrect.");
            Assert.AreEqual("1", allowedCountriesList[0], "The allowed countries at index 0 was not extracted correctly.");
            Assert.AreEqual("37[0-2]", allowedCountriesList[3], "The allowed countries at index 3 was not extracted correctly.");
            Assert.AreEqual("998", allowedCountriesList[17], "The allowed countries at index 7 was not extracted correctly.");
        }
    }
}
