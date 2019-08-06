using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Entities;

namespace SIPSorcery.Entities.UnitTests
{   
    ///<summary>
    ///This is a test class for SIPAccount unit tests.
    ///</summary>
    [TestClass()]
    public class SimpleWizardRuleTest
    {
        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
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

        ///<summary>
        ///A test for SimpleWizardRule Constructor
        ///</summary>
        [TestMethod()]
        public void SimpleWizardRuleConstructorTest()
        {
            SimpleWizardRule target = new SimpleWizardRule();
            Assert.IsNotNull(target);
        }

        /// <summary>
        /// Tests that the time check returns false when the specified time pattern does not match the day.
        /// </summary>
        [TestMethod]
        public void TimePatternExcludedDayTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "Tu"
            };

            // Sunday.
            var now = new DateTimeOffset(2011, 11, 27, 14, 28, 0, 0, TimeSpan.Zero);

            Assert.IsFalse(target.IsTimeMatch(now, null));
        }

        /// <summary>
        /// Tests that the time check returns true when the specified time pattern does match the day.
        /// </summary>
        [TestMethod]
        public void TimePatternIncludedDayTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "Su"
            };

            // Sunday.
            var now = new DateTimeOffset(2011, 11, 27, 14, 28, 0, 0, TimeSpan.Zero);

            Assert.IsTrue(target.IsTimeMatch(now, null));
        }

        /// <summary>
        /// Tests that the time check returns true when the specified time pattern includes all days.
        /// </summary>
        [TestMethod]
        public void TimePatternAllDaysIncludedDayTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "MTuWThFSaSu;00:00-23:59"
            };

            // Sunday.
            var now = new DateTimeOffset(2011, 11, 27, 14, 28, 0, 0, TimeSpan.Zero);

            Assert.IsTrue(target.IsTimeMatch(now, null));
        }

        /// <summary>
        /// Tests that the time check returns false when the specified time pattern includes all days
        /// except Wednesday.
        /// </summary>
        [TestMethod]
        public void TimePatternAllDaysExceptWednesdayDayTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "MTuThFSaSu;00:00-23:59"
            };

            // Wednesday.
            var now = new DateTimeOffset(2011, 11, 23, 14, 28, 0, 0, TimeSpan.Zero);

            Assert.IsFalse(target.IsTimeMatch(now, null));
        }

        /// <summary>
        /// Tests that the time check returns false when the specified time pattern has a start time
        /// earlier than the test time.
        /// </summary>
        [TestMethod]
        public void TimePatternExcludeByStartTimeTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "MTuWThFSaSu;08:00-23:59"
            };

            var now = new DateTimeOffset(2011, 11, 23, 7, 28, 0, 0, TimeSpan.Zero);

            Assert.IsFalse(target.IsTimeMatch(now, null));
        }

        /// <summary>
        /// Tests that the time check returns false when the specified time pattern has an end time
        /// later than the test time.
        /// </summary>
        [TestMethod]
        public void TimePatternExcludeByEndTimeTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "MTuWThFSaSu;08:00-17:00"
            };

            var now = new DateTimeOffset(2011, 11, 23, 18, 28, 0, 0, TimeSpan.Zero);

            Assert.IsFalse(target.IsTimeMatch(now, null));
        }


        /// <summary>
        /// Tests that the time check returns true when the specified time pattern includes the test time.
        /// </summary>
        [TestMethod]
        public void TimePatternIncludedTimeTest()
        {
            SimpleWizardRule target = new SimpleWizardRule()
            {
                TimePattern = "MTuWThFSaSu;08:00-17:00"
            };

            var now = new DateTimeOffset(2011, 11, 23, 11, 28, 0, 0, TimeSpan.Zero);

            Assert.IsTrue(target.IsTimeMatch(now, null));
        }
    }
}
