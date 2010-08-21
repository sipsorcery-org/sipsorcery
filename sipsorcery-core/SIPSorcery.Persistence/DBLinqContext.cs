// ============================================================================
// FileName: DBLinqContext.cs
//
// Description:
// Class to create DBLinq data contexts.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Aor 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using DbLinq.Data.Linq;
using DbLinq.Data.Linq.Mapping;
using DbLinq.Vendor;
using MySql.Data.MySqlClient;
using Npgsql;

namespace SIPSorcery.Persistence {

    public class DBLinqContext {

        private static System.Data.Linq.Mapping.MappingSource m_mappingSource = new AttributeMappingSource();
        //private static TextWriter dbLinqLogWriter = new StreamWriter(@"C:\Temp\sipsorcery\dblinq.log", true, Encoding.ASCII);

        public static DataContext CreateDBLinqDataContext(StorageTypes storageType, string connectionString) {
            DataContext dataContext = null;
            //DbProviderFactory factory = DbProviderFactories.GetFactory(providerName);
            //new MySql.Data.MySqlClient.MySqlClientFactory();
            //DbProviderFactory factory = Npgsql.NpgsqlFactory.Instance;
            
            switch (storageType) {
                case StorageTypes.DBLinqMySQL:
                    IDbConnection mySqlConn = new MySqlConnection(connectionString);
                    dataContext = new DataContext(mySqlConn, m_mappingSource, new DbLinq.MySql.MySqlVendor());
                    break;
                case StorageTypes.DBLinqPostgresql:
                    IDbConnection npgsqlConn = new NpgsqlConnection(connectionString);
                    dataContext = new DataContext(npgsqlConn, m_mappingSource, new DbLinq.PostgreSql.PgsqlVendor());
                    break;
                default:
                    throw new NotSupportedException("Database type " + storageType + " is not supported by CreateDBLinqDataContext.");
            }

            //dataContext.QueryCacheEnabled = true;
            //dataContext.Log = Console.Out;
            //dataContext.Log = dbLinqLogWriter;
            dataContext.ObjectTrackingEnabled = false;
            return dataContext;
        }
    }
}
