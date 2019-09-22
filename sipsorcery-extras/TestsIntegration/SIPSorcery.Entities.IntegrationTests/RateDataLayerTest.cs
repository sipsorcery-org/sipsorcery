using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Entities;

namespace SIPSorcery.Entities.IntegrationTests
{
    [TestClass]
    //[Ignore] // Integration only tests, require DB.
    public class RateDataLayerTest
    {
        [TestMethod, TestCategory("Integration")]
        public void GetRateTestMethod()
        {
            string id = null;

            try
            {
                RateDataLayer rateDataLayer = new RateDataLayer();
                var rate = new Rate() {Description = "test", Owner = "aaron", Rate1 = 0.1M, Prefix = "012" };
                rateDataLayer.Add(rate);

                id = rate.ID;
                var retrievedRate = rateDataLayer.Get("aaron", rate.ID);

                Assert.IsNotNull(retrievedRate);
            }
            finally
            {
                if (id != null)
                {
                    TestHelper.ExecuteQuery("delete from rate where id = '" + id + "'");
                }
            }
        }
    }
}
