//-----------------------------------------------------------------------------
// Filename: StorageTypes.cs
//
// Description: List of persistence store options.
//
// Author(s):
// Aaron Clauson
//
// History:
// ??	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
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
            if (Enum.TryParse<StorageTypes>(storageType, true, out var result))
            {
                return result;
            }

            logger.LogError("StorageTypesConverter {StorageType} unknown.", storageType);

            return StorageTypes.Unknown;
        }
    }
}
