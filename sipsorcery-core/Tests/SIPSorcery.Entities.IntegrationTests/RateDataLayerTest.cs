using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Entities;

namespace SIPSorcery.Entities.IntegrationTests
{
    [TestClass]
    public class RateDataLayerTest
    {
        [TestMethod]
        public void GetRateTestMethod()
        {
            RateDataLayer rateDataLayer = new RateDataLayer();
            var rate = new Rate() { Description = "test", Owner = "aaron", Rate1 = 0.1M, Prefix = "012" };
            rateDataLayer.Add(rate);

            var retrievedRate = rateDataLayer.Get("aaron", rate.ID);

            Assert.IsNotNull(retrievedRate);
        }
    }
}
