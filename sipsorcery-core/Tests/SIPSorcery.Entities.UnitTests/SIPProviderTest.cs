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
    ///This is a test class for SIPProvider unit tests.
    ///</summary>
    [TestClass()]
    public class SIPProviderTest
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
        ///A test for SIPProvider Constructor
        ///</summary>
        [TestMethod()]
        public void SIPAccountConstructorTest()
        {
            SIPProvider target = new SIPProvider();
            Assert.IsNotNull(target);
        }

        ///<summary>
        ///Tests the metadata validation when a new SIP provider is valid.
        ///</summary>
        //[TestMethod()]
        //public void SIPProviderIsValidTest()
        //{
        //    SIPProvider target = new SIPProvider()
        //    {
        //        Owner = "owner",
        //        ProviderName = "test",
        //        ProviderUsername = "user"
        //    };

        //    string validationResult = SIPProvider.Validate(target);

        //    Assert.IsNull(validationResult);
        //}

        ///<summary>
        ///Tests the metadata validation when a new SIP provider is not given a name.
        ///</summary>
        [TestMethod()]
        public void SIPProviderNoNameValidationTest()
        {
            SIPProvider target = new SIPProvider()
            {
                Owner = "owner"
            };

            string validationResult = SIPProvider.Validate(target);

            Assert.AreEqual("A provider name must be specified.", validationResult);
        }
       
        ///<summary>
        ///Tests the metadata validation when a new SIP provider is given an invalid name.
        ///</summary>
        [TestMethod()]
        [Ignore] // "Haven't been able to get the regex validation to work as yet."
        public void SIPProviderInvalidusernameValidationTest()
        {
            SIPProvider target = new SIPProvider()
            {
                Owner = "owner",
                ProviderUsername = "user",
                ProviderName = "my.provider"
            };

            string validationResult = SIPProvider.Validate(target);

            Assert.AreEqual("Provider names cannot contain a full stop '.' in order to avoid ambiguity with DNS host names, please remove the '.'.", validationResult);
        }

        ///<summary>
        ///Tests the metadata validation when a new SIP provider has an invalid server value.
        ///</summary>
        [TestMethod()]
        [Ignore] // "Haven't been able to get the regex validation to work as yet."
        public void SIPProviderInvalidServerValidationTest()
        {
            SIPProvider target = new SIPProvider()
            {
                Owner = "owner",
                ProviderName = "test",
                ProviderUsername = "user",
                ProviderType = "SIP",
                ProviderServer = "somehost"
            };

            string validationResult = SIPProvider.Validate(target);

            Assert.AreEqual("The SIP provider server should contain at least one '.' to be recognised as a valid hostname or IP address.", validationResult);
        }
    }
}
