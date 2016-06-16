using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Entities;

namespace SIPSorcery.Entities.IntegrationTests
{
    [TestClass]
    public class CustomerDataLayerTest
    {
        [TestMethod]
        public void GetCustomerTest()
        {
            CustomerDataLayer customerDataLayer = new CustomerDataLayer();
            var cust = customerDataLayer.GetForName("aaron");

            Assert.IsNotNull(cust);
        }
    }
}
