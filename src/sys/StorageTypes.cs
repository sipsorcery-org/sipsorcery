using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    public enum StorageTypes
    {
        Unknown,
        MSSQL,
        Postgresql,
        MySQL,
        Oracle,
        XML,
        DBLinqMySQL,
        DBLinqPostgresql,
        SimpleDBLinq,
        SQLLinqMySQL,
        SQLLinqPostgresql,
        SQLLinqMSSQL,
        SQLLinqOracle,
    }

    public class StorageTypesConverter
    {
        private static ILogger logger = Log.Logger;

        public static StorageTypes GetStorageType(string storageType)
        {
            try
            {
                return (StorageTypes)Enum.Parse(typeof(StorageTypes), storageType, true);
            }
            catch
            {
                logger.LogError("StorageTypesConverter " + storageType + " unknown.");
                return StorageTypes.Unknown;
            }
        }
    }
}
