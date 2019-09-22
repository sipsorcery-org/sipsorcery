using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Persistence
{
    public class EntityTypeConversionTable
    {
        private static Dictionary<string, DbType> m_conversionTable = new Dictionary<string, DbType>()
        {
            {"int", DbType.Int32},
            {"bit", DbType.Boolean},
            {"varchar", DbType.StringFixedLength},
            {"datetimeoffset", DbType.DateTimeOffset},
            {"datetime", DbType.DateTime},
            {"decimal", DbType.Decimal},
        };

        public static DbType LookupDbType(string entityType)
        {
            if(entityType.IsNullOrBlank())
            {
                throw new ArgumentNullException("entityType", "The entityType to lookup cannot be empty.");
            }

            if (entityType.Trim().StartsWith("varchar"))
            {
                return m_conversionTable["varchar"];
            }
            else
            {
                return m_conversionTable[entityType];
            }
        }
    }
}
