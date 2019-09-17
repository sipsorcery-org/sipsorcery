using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Entities;

namespace SIPSorcery.Entities.IntegrationTests
{
    [TestClass]
    //[Ignore] // Integration only tests, require DB.
    public class CustomerDataLayerTest
    {
        [TestMethod, TestCategory("Integration")]
        public void GetCustomerTest()
        {
            CustomerDataLayer customerDataLayer = new CustomerDataLayer();
            var cust = customerDataLayer.GetForName("aaron");

            Assert.IsNotNull(cust);
        }
    }
}
