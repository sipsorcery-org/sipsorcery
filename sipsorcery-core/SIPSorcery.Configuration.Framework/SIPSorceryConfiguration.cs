using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;

namespace SIPSorcery.Sys
{
    public class SIPSorceryConfiguration
    {
        public const string PERSISTENCE_STORAGETYPE_KEY = "PersistenceStorageType";
        public const string PERSISTENCE_STORAGECONNSTR_KEY = "PersistenceConnStr";

        public StorageTypes PersistenceStorageType { get; private set; }
        public string PersistenceConnStr { get; private set;}

        public SIPSorceryConfiguration()
        {
            PersistenceStorageType = (ConfigurationManager.AppSettings[PERSISTENCE_STORAGETYPE_KEY] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[PERSISTENCE_STORAGETYPE_KEY]) : StorageTypes.Unknown;
            PersistenceConnStr = ConfigurationManager.AppSettings[PERSISTENCE_STORAGECONNSTR_KEY];
        }

        public string GetAppSetting(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }
    }
}
