using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Sys;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Persistence.IntegrationTests
{
    [TestClass]

    public class SIPAccountAssetPersistorTest
    {
        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        private static StorageTypes _serverStorageType;
        private static string _serverStorageConnStr;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            _serverStorageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) : StorageTypes.Unknown;
            _serverStorageConnStr = AppState.GetConfigSetting(m_connStrKey);
        }

        /// <summary>
        /// Tests an attempt to get a non-existent SIP account record.
        /// </summary>
        [TestMethod, TestCategory("Integration")]
        public void GetNonExistentSIPAccountAssetTestMethod()
        {
            var sipAccountPersistor = SIPAssetPersistorFactory<SIPAccountAsset>.CreateSIPAssetPersistor(_serverStorageType, _serverStorageConnStr, null);

            var result = sipAccountPersistor.Get(Guid.Empty);

            Assert.IsNull(result);
        }

        /// <summary>
        /// Tests an attempt to get a SIP account record.
        /// </summary>
        [TestMethod, TestCategory("Integration")]
        public void GetSIPAccountAssetTestMethod()
        {
            try
            {
                TestHelper.ExecuteNonQuery(_serverStorageConnStr, "insert into sipaccounts (id, sipusername, sippassword, owner, sipdomain, inserted) values (uuid(), 'test', 'password', 'aaron', 'sipsorcery.com', now())");

                var sipAccountPersistor = SIPAssetPersistorFactory<SIPAccountAsset>.CreateSIPAssetPersistor(_serverStorageType, _serverStorageConnStr, null);

                SIPAccountAsset result = sipAccountPersistor.Get(x => x.SIPUsername == "test");

                Assert.IsNotNull(result);
            }
            finally
            {
                TestHelper.ExecuteNonQuery(_serverStorageConnStr, "delete from sipaccounts where sipusername = 'test'");
            }
        }

        /// <summary>
        /// Tests using the asset persistor to get a SIPAccount object instead of a SIPAccountAsset object.
        /// </summary>
        [TestMethod, TestCategory("Integration")]
        public void GetSIPAccounFromSIPAccountAssetPersistorTestMethod()
        {
            try
            {
                TestHelper.ExecuteNonQuery(_serverStorageConnStr, "insert into sipaccounts (id, sipusername, sippassword, owner, sipdomain, inserted) values (uuid(), 'test', 'password', 'aaron', 'sipsorcery.com', now())");

                var sipAccountPersistor = SIPAssetPersistorFactory<SIPAccountAsset>.CreateSIPAssetPersistor(_serverStorageType, _serverStorageConnStr, null);

                GetSIPAccountDelegate getSIPAccount = sipAccountPersistor.Get => (x => x.SIPUsername == "test") ;

                //Assert.IsNotNull(result);
            }
            finally
            {
                TestHelper.ExecuteNonQuery(_serverStorageConnStr, "delete from sipaccounts where sipusername = 'test'");
            }
        }
    }
}
