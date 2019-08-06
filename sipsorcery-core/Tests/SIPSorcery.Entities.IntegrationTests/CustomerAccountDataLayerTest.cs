using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Entities.IntegrationTests
{
    [TestClass]
    public class CustomerAccountDataLayerTest
    {
        [TestMethod]
        public void TestGetCustomerAccountByNumber()
        {
            string accountNumber = "000111222";

            try
            {
                var customerAccount = new CustomerAccount() {
                    ID = Guid.NewGuid().ToString(), AccountName = accountNumber, Owner = "aaron", RatePlan = 1,
                    AccountCode = "AC" + accountNumber, AccountNumber = accountNumber, Inserted = DateTime.UtcNow.ToString("o") };

                using (var db = new SIPSorceryEntities())
                {
                    db.CustomerAccounts.Add(customerAccount);
                    db.SaveChanges();
                }

                CustomerAccountDataLayer customerAccountDataLayer = new CustomerAccountDataLayer();
                var checkCustomerAccount = customerAccountDataLayer.Get("aaron", accountNumber);

                Assert.IsNotNull(checkCustomerAccount);
                Assert.AreEqual(customerAccount.ID, checkCustomerAccount.ID);
            }
            finally
            {
                TestHelper.ExecuteQuery("delete from customeraccount where accountnumber = '" + accountNumber + "'");
            }
        }

        [TestMethod]
        public void TestGetCustomerAccountByAccountCode()
        {
            string accountNumber = "000111222";
            string accountCode = "AC" + accountNumber;

            try
            {
                var customerAccount = new CustomerAccount()
                {
                    ID = Guid.NewGuid().ToString(),
                    AccountName = accountNumber,
                    Owner = "aaron",
                    RatePlan = 1,
                    AccountCode = accountCode,
                    AccountNumber = accountNumber,
                    Inserted = DateTime.UtcNow.ToString("o")
                };

                using (var db = new SIPSorceryEntities())
                {
                    db.CustomerAccounts.Add(customerAccount);
                    db.SaveChanges();
                }

                CustomerAccountDataLayer customerAccountDataLayer = new CustomerAccountDataLayer();
                var checkCustomerAccount = customerAccountDataLayer.Get("aaron", accountCode);

                Assert.IsNotNull(checkCustomerAccount);
                Assert.AreEqual(customerAccount.ID, checkCustomerAccount.ID);
            }
            finally
            {
                TestHelper.ExecuteQuery("delete from customeraccount where accountnumber = '" + accountNumber + "'");
            }
        }
    }
}
