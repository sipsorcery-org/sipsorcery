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
    public class SIPAccountTest
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
        ///A test for SIPAccount Constructor
        ///</summary>
        [TestMethod()]
        public void SIPAccountConstructorTest()
        {
            SIPAccount target = new SIPAccount();
            Assert.IsNotNull(target);
        }

        ///<summary>
        ///Tests the metadata validation when a new SIP account is not given a username.
        ///</summary>
        [TestMethod()]
        public void SIPAccountNoUsernameValidationTest()
        {
            SIPAccount target = new SIPAccount()
            {
                Owner = "owner",
                SIPDomain = "somedomain",
                SIPPassword = "password"
            };

            string validationResult = SIPAccount.Validate(target);

            Assert.AreEqual("A username must be specified for the SIP account.", validationResult);
        }

        ///<summary>
        ///Tests the metadata validation when a new SIP account is not given an invalid username.
        ///</summary>
        //[TestMethod()]
        //public void SIPAccountInvalidUsernameValidationTest()
        //{
        //    SIPAccount target = new SIPAccount()
        //    {
        //        ID = Guid.NewGuid().ToString(),
        //        Owner = "owner",
        //        SIPUsername = "$$$$$$$$$",
        //        SIPDomain = "somedomain",
        //        SIPPassword = "password"
        //    };

        //    string validationResult = SIPAccount.Validate(target);

        //    Assert.AreEqual("The username contained an illegal character. Only alpha-numeric characters and .-_ are allowed.", validationResult);
        //}

        ///<summary>
        ///Tests the metadata validation when a new SIP account is given a prohibited username.
        ///</summary>
    //    [TestMethod()]
    //    public void SIPAccountProhibitedUsernameValidationTest()
    //    {
    //        SIPAccount target = new SIPAccount()
    //        {
    //            Owner = "owner",
    //            SIPUsername = "dispatcher",
    //            SIPDomain = "sipsorcery.com",
    //            SIPPassword = "password"
    //        };

    //        string validationResult = SIPAccount.Validate(target);

    //        Assert.AreEqual("The username you have requested is not permitted.", validationResult);
    //    }
    }
}
