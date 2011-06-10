using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;

namespace SIPSorcery.Entities.UnitTests
{
    /// <summary>
    ///This is a test class for SIPEntitiesDomainServiceTest and is intended
    ///to contain all SIPEntitiesDomainServiceTest Unit Tests
    ///</summary>
    [TestClass()]
    public class SIPEntitiesDomainServiceTest
    {
        private static string m_testDatabaseFilename = "SIPEntitiesDomainServiceTest.mdb";
        private static string m_testDatabaseSchema = "create table test(id varchar(36) not null);";

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

        //Use ClassInitialize to run code before running the first test in the class
        [ClassInitialize()]
        public static void MyClassInitialize(TestContext testContext)
        {
            string connectionString = string.Format(@"Server=.\SQLEXPRESS; Integrated Security=true;AttachDbFileName={0};", m_testDatabaseFilename);
            //CreateDatabase();
        }
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}

        private static void CreateDatabase()
        {
            var databaseName = Path.GetFileNameWithoutExtension(m_testDatabaseFilename);

            using (var connection = new SqlConnection("Data Source=.\\sqlexpress;Initial Catalog=tempdb;Integrated Security=true;User Instance=True;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "CREATE DATABASE " + databaseName +
                        " ON PRIMARY (NAME=" + databaseName +
                        ", FILENAME='" + m_testDatabaseFilename + "')";
                    command.ExecuteNonQuery();

                    command.CommandText =
                        "EXEC sp_detach_db '" + databaseName + "', 'true'";
                    command.ExecuteNonQuery();

                    // After we've created the database, initialize it with any
                    // schema we've been given
                    if (!string.IsNullOrEmpty(m_testDatabaseSchema))
                    {
                        command.CommandText = m_testDatabaseSchema;
                    }
                }
            }
        }

        /// <summary>
        ///A test for SIPEntitiesDomainService Constructor
        ///</summary>
        [TestMethod()]
        public void SIPEntitiesDomainServiceConstructorTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService();
        }

        /// <summary>
        ///A test for IsAlive
        ///</summary>
        [TestMethod()]
        public void IsAliveTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService();
            Assert.AreEqual(true, target.IsAlive());
        }

        /// <summary>
        ///A test for ChangePassword
        ///</summary>
        [TestMethod()]
        public void ChangePasswordTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            string oldPassword = string.Empty; // TODO: Initialize to an appropriate value
            string newPassword = string.Empty; // TODO: Initialize to an appropriate value
            target.ChangePassword(oldPassword, newPassword);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteCustomer
        ///</summary>
        [TestMethod()]
        public void DeleteCustomerTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            Customer customer = null; // TODO: Initialize to an appropriate value
            target.DeleteCustomer(customer);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPAccount
        ///</summary>
        [TestMethod()]
        public void DeleteSIPAccountTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPAccount sipAccount = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPAccount(sipAccount);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPDialplan
        ///</summary>
        [TestMethod()]
        public void DeleteSIPDialplanTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialPlan sipDialplan = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPDialplan(sipDialplan);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPDialplanLookup
        ///</summary>
        [TestMethod()]
        public void DeleteSIPDialplanLookupTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanLookup sipDialplanLookup = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPDialplanLookup(sipDialplanLookup);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPDialplanOption
        ///</summary>
        [TestMethod()]
        public void DeleteSIPDialplanOptionTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanOption sipDialplanOption = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPDialplanOption(sipDialplanOption);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPDialplanProvider
        ///</summary>
        [TestMethod()]
        public void DeleteSIPDialplanProviderTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanProvider sipDialplanProvider = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPDialplanProvider(sipDialplanProvider);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPDialplanRoute
        ///</summary>
        [TestMethod()]
        public void DeleteSIPDialplanRouteTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanRoute sipDialplanRoute = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPDialplanRoute(sipDialplanRoute);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for DeleteSIPProvider
        ///</summary>
        [TestMethod()]
        public void DeleteSIPProviderTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPProvider sipProvider = null; // TODO: Initialize to an appropriate value
            target.DeleteSIPProvider(sipProvider);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for GetCDRs
        ///</summary>
        [TestMethod()]
        public void GetCDRsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<CDR> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<CDR> actual;
            actual = target.GetCDRs();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetCustomer
        ///</summary>
        [TestMethod()]
        public void GetCustomerTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            Customer expected = null; // TODO: Initialize to an appropriate value
            Customer actual;
            actual = target.GetCustomer();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPAccounts
        ///</summary>
        [TestMethod()]
        public void GetSIPAccountsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPAccount> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPAccount> actual;
            actual = target.GetSIPAccounts();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDialogues
        ///</summary>
        [TestMethod()]
        public void GetSIPDialoguesTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDialogue> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDialogue> actual;
            actual = target.GetSIPDialogues();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDialplanLookups
        ///</summary>
        [TestMethod()]
        public void GetSIPDialplanLookupsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanLookup> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanLookup> actual;
            actual = target.GetSIPDialplanLookups();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDialplanOptions
        ///</summary>
        [TestMethod()]
        public void GetSIPDialplanOptionsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanOption> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanOption> actual;
            actual = target.GetSIPDialplanOptions();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDialplanProviders
        ///</summary>
        [TestMethod()]
        public void GetSIPDialplanProvidersTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanProvider> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanProvider> actual;
            actual = target.GetSIPDialplanProviders();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDialplanRoutes
        ///</summary>
        [TestMethod()]
        public void GetSIPDialplanRoutesTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanRoute> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDialplanRoute> actual;
            actual = target.GetSIPDialplanRoutes();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDialplans
        ///</summary>
        [TestMethod()]
        public void GetSIPDialplansTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDialPlan> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDialPlan> actual;
            actual = target.GetSIPDialplans();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPDomains
        ///</summary>
        [TestMethod()]
        public void GetSIPDomainsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPDomain> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPDomain> actual;
            actual = target.GetSIPDomains();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPProviderBindings
        ///</summary>
        [TestMethod()]
        public void GetSIPProviderBindingsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPProviderBinding> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPProviderBinding> actual;
            actual = target.GetSIPProviderBindings();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPProviders
        ///</summary>
        [TestMethod()]
        public void GetSIPProvidersTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPProvider> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPProvider> actual;
            actual = target.GetSIPProviders();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetSIPRegistrarBindings
        ///</summary>
        [TestMethod()]
        public void GetSIPRegistrarBindingsTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            IQueryable<SIPRegistrarBinding> expected = null; // TODO: Initialize to an appropriate value
            IQueryable<SIPRegistrarBinding> actual;
            actual = target.GetSIPRegistrarBindings();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetTimeZoneOffsetMinutes
        ///</summary>
        [TestMethod()]
        public void GetTimeZoneOffsetMinutesTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            int expected = 0; // TODO: Initialize to an appropriate value
            int actual;
            actual = target.GetTimeZoneOffsetMinutes();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for GetUser
        ///</summary>
        [TestMethod()]
        public void GetUserTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            User expected = null; // TODO: Initialize to an appropriate value
            User actual;
            actual = target.GetUser();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for InsertCustomer
        ///</summary>
        [TestMethod()]
        public void InsertCustomerTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            Customer customer = null; // TODO: Initialize to an appropriate value
            target.InsertCustomer(customer);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPAccount
        ///</summary>
        [TestMethod()]
        public void InsertSIPAccountTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPAccount sipAccount = null; // TODO: Initialize to an appropriate value
            target.InsertSIPAccount(sipAccount);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPDialplan
        ///</summary>
        [TestMethod()]
        public void InsertSIPDialplanTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialPlan sipDialplan = null; // TODO: Initialize to an appropriate value
            target.InsertSIPDialplan(sipDialplan);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPDialplanLookup
        ///</summary>
        [TestMethod()]
        public void InsertSIPDialplanLookupTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanLookup sipDialplanLookup = null; // TODO: Initialize to an appropriate value
            target.InsertSIPDialplanLookup(sipDialplanLookup);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPDialplanOption
        ///</summary>
        [TestMethod()]
        public void InsertSIPDialplanOptionTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanOption sipDialplanOption = null; // TODO: Initialize to an appropriate value
            target.InsertSIPDialplanOption(sipDialplanOption);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPDialplanProvider
        ///</summary>
        [TestMethod()]
        public void InsertSIPDialplanProviderTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanProvider sipDialplanProvider = null; // TODO: Initialize to an appropriate value
            target.InsertSIPDialplanProvider(sipDialplanProvider);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPDialplanRoute
        ///</summary>
        [TestMethod()]
        public void InsertSIPDialplanRouteTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanRoute sipDialplanRoute = null; // TODO: Initialize to an appropriate value
            target.InsertSIPDialplanRoute(sipDialplanRoute);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for InsertSIPProvider
        ///</summary>
        [TestMethod()]
        public void InsertSIPProviderTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPProvider sipProvider = null; // TODO: Initialize to an appropriate value
            target.InsertSIPProvider(sipProvider);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for Login
        ///</summary>
        [TestMethod()]
        public void LoginTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            string username = string.Empty; // TODO: Initialize to an appropriate value
            string password = string.Empty; // TODO: Initialize to an appropriate value
            bool isPersistent = false; // TODO: Initialize to an appropriate value
            string customData = string.Empty; // TODO: Initialize to an appropriate value
            User expected = null; // TODO: Initialize to an appropriate value
            User actual;
            actual = target.Login(username, password, isPersistent, customData);
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for Logout
        ///</summary>
        [TestMethod()]
        public void LogoutTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            User expected = null; // TODO: Initialize to an appropriate value
            User actual;
            actual = target.Logout();
            Assert.AreEqual(expected, actual);
            Assert.Inconclusive("Verify the correctness of this test method.");
        }

        /// <summary>
        ///A test for UpdateCustomer
        ///</summary>
        [TestMethod()]
        public void UpdateCustomerTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            Customer currentCustomer = null; // TODO: Initialize to an appropriate value
            target.UpdateCustomer(currentCustomer);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPAccount
        ///</summary>
        [TestMethod()]
        public void UpdateSIPAccountTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPAccount currentSIPAccount = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPAccount(currentSIPAccount);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPDialplan
        ///</summary>
        [TestMethod()]
        public void UpdateSIPDialplanTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialPlan currentSIPDialplan = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPDialplan(currentSIPDialplan);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPDialplanLookup
        ///</summary>
        [TestMethod()]
        public void UpdateSIPDialplanLookupTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanLookup currentSIPDialplanLookup = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPDialplanLookup(currentSIPDialplanLookup);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPDialplanOption
        ///</summary>
        [TestMethod()]
        public void UpdateSIPDialplanOptionTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanOption currentSIPDialplanOption = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPDialplanOption(currentSIPDialplanOption);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPDialplanProvider
        ///</summary>
        [TestMethod()]
        public void UpdateSIPDialplanProviderTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanProvider currentSIPDialplanProvider = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPDialplanProvider(currentSIPDialplanProvider);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPDialplanRoute
        ///</summary>
        [TestMethod()]
        public void UpdateSIPDialplanRouteTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPDialplanRoute currentSIPDialplanRoute = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPDialplanRoute(currentSIPDialplanRoute);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateSIPProvider
        ///</summary>
        [TestMethod()]
        public void UpdateSIPProviderTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            SIPProvider currentSIPProvider = null; // TODO: Initialize to an appropriate value
            target.UpdateSIPProvider(currentSIPProvider);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }

        /// <summary>
        ///A test for UpdateUser
        ///</summary>
        [TestMethod()]
        public void UpdateUserTest()
        {
            SIPEntitiesDomainService target = new SIPEntitiesDomainService(); // TODO: Initialize to an appropriate value
            User user = null; // TODO: Initialize to an appropriate value
            target.UpdateUser(user);
            Assert.Inconclusive("A method that does not return a value cannot be verified.");
        }
    }
}
