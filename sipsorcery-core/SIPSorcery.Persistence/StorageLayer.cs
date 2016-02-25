// ============================================================================
// FileName: StorageLayer.cs
//
// Description:
// Provides common functionality to access different database and file storage 
// layers.
//
// Author(s):
// Aaron Clauson
//
// History:
// 24 Dec 2004	Aaron Clauson	Created.
// 19 Mar 2006  Aaron Clauson	Added StoreLargeObject.
// 08 Sep 2006  Aaron Clauson	Modified to work with non-pooled connections as Npgsql currently has big problems in its connection
//								pooling mechanism.
// ?            Aaron Clauson   Isolated the problem in Npgsql connection pooling and workaround is to set MinPoolSize=0 in the connection
//                              string. Modified to work with connection pooling again.
// 16 Dec 2007  Aaron Clauson   Added check for dangerous SQL.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;
using MySql.Data.MySqlClient;
using Npgsql;
using NpgsqlTypes;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Persistence
{
	public class StorageLayer
	{
		public const string FILENAME_DATETIME_FORMAT = "ddMMMyy-HHmmss";

		// Constants used by Npgsql to decide whether a connection string is a pooling one or not.
		// To be a non-pooled string Pooling=false and MinPoolsSize and MaxPoolSize must not be present.
		public static readonly string Pooling = "POOLING=false";
		public static readonly string MinPoolSize = "MINPOOLSIZE";
		public static readonly string MaxPoolSize = "MAXPOOLSIZE";
       		
		private ILog logger = AppState.logger;
	
	    //private static string m_errorNotificationAddress = AppState.ErrorNotificationEmail;

		private StorageTypes m_storageType;
		private string m_dbConnStr = null;

		public StorageLayer()
		{}

		public StorageLayer(StorageTypes storageType, string dbConnStr)
		{
			m_storageType = storageType;
			m_dbConnStr = dbConnStr;
		}

        /// <summary>
        /// Checks SQL queries to filter out ones that match an SQL injection attack.
        /// </summary>
        /// <param name="query"></param>
        private void QuerySecurityCheck(string query)
        {
            // Run a security check on query for SQL injection attacks.
            if (query != null && Regex.Match(query, @"'\S;").Success)
            {
                //SIPSorcerySMTP.SendEmail(m_errorNotificationAddress, m_errorNotificationAddress, "Illegal SQL Detected", query);
                throw new ApplicationException("SQL not permitted: " + query);
            }
        }

		public void ExecuteNonQuery(string query)
		{
			ExecuteNonQuery(m_storageType, m_dbConnStr, query);
		}

        public void ExecuteNonQuery(string query, Dictionary<string, object> parameters) {
            ExecuteNonQuery(m_storageType, m_dbConnStr, query, parameters);
        }

		public void ExecuteNonQuery(StorageTypes storageType, string dbConnString, string query)
		{
            QuerySecurityCheck(query);
            
            try
			{
                using (IDbConnection dbConn = GetDbConnection(storageType, dbConnString)) {
                    dbConn.Open();
                    IDbCommand command = GetDbCommand(storageType, dbConn, query);
                    command.ExecuteNonQuery();
                }
			}
			catch(Exception excp)
			{
				logger.Error("Exception ExecuteNonQuery. " + excp.Message);
				throw excp;
			}	
		}

        public void ExecuteNonQuery(StorageTypes storageType, string dbConnString, string query, Dictionary<string, object> parameters) {
            try {
                using (IDbConnection dbConn = GetDbConnection(storageType, dbConnString)) {
                    dbConn.Open();
                    IDbCommand command = GetDbCommand(storageType, dbConn, query);

                    if (parameters != null && parameters.Count > 0) {
                        foreach (KeyValuePair<string, object> parameter in parameters) {
                            command.Parameters.Add(GetDbParameter(storageType, parameter.Key, parameter.Value));
                        }
                    }

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ExecuteNonQuery. " + excp.Message);
                throw excp;
            }
        }		
		
		public object ExecuteScalar(string query)
		{
			return ExecuteScalar(m_storageType, m_dbConnStr, query);
		}

        public object ExecuteScalar(string query, Dictionary<string, object> parameters) {
            return ExecuteScalar(m_storageType, m_dbConnStr, query, parameters);
        }

        public object ExecuteScalar(StorageTypes storageType, string dbConnString, string query) {
            QuerySecurityCheck(query);

            try {
                using (IDbConnection dbConn = GetDbConnection(storageType, dbConnString)) {
                    dbConn.Open();
                    IDbCommand command = GetDbCommand(storageType, dbConn, query);
                    return command.ExecuteScalar();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ExecuteScalar. " + excp.Message);
                throw excp;
            }
        }

        public object ExecuteScalar(StorageTypes storageType, string dbConnString, string query, Dictionary<string, object> parameters) {
            QuerySecurityCheck(query);

            try {
                using (IDbConnection dbConn = GetDbConnection(storageType, dbConnString)) {
                    dbConn.Open();
                    IDbCommand command = GetDbCommand(storageType, dbConn, query);

                    if (parameters != null && parameters.Count > 0) {
                        foreach (KeyValuePair<string, object> parameter in parameters) {
                            command.Parameters.Add(GetDbParameter(storageType, parameter.Key, parameter.Value));
                        }
                    }

                    return command.ExecuteScalar();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ExecuteScalar. " + excp.Message);
                throw excp;
            }
        }	
		
		public DataSet GetDataSet(string query)
		{
			return GetDataSet(m_storageType, m_dbConnStr, query);
		}

		public DataSet GetDataSet(StorageTypes storageType, string dbConnString, string query)
		{
            QuerySecurityCheck(query);

            try {
                using (IDbConnection dbConn = GetDbConnection(storageType, dbConnString)) {
                    dbConn.Open();
                    IDataAdapter adapter = GetDataAdapter(storageType, dbConn, query);

                    DataSet resultSet = new DataSet();
                    adapter.Fill(resultSet);

                    return resultSet;
                }
            }
            catch (Exception excp) {
                logger.Error("Exception GetDataSet. " + excp.Message);
                throw excp;
            }
		}

		//public int StoreLargeObject(Stream largeObjectStream)
		//{
		//	return StoreLargeObject(m_storageType, m_dbConnStr, largeObjectStream);
		//}
		
		//public int StoreLargeObject(StorageTypes storageType, string dbConnString, Stream largeObjectStream)
		//{
		//	try
		//	{
		//		if(storageType == StorageTypes.Postgresql)
		//		{
		//			NpgsqlConnection connPgsql = new NpgsqlConnection(dbConnString);
		//			connPgsql.Open();

		//			NpgsqlTransaction t = connPgsql.BeginTransaction();
				
		//			LargeObjectManager lbm = new LargeObjectManager(connPgsql);

		//			int noid = lbm.Create(LargeObjectManager.READWRITE);
		//			LargeObject lo =  lbm.Open(noid, LargeObjectManager.READWRITE);

		//			long offset = 0;

		//			while(offset < largeObjectStream.Length)
		//			{
		//				long bytesToWrite = (largeObjectStream.Length - offset > 1024) ? 1024 : largeObjectStream.Length - offset;
						
		//				byte[] buf = new byte[bytesToWrite];
		//				largeObjectStream.Read(buf, 0, (int)bytesToWrite);

		//				lo.Write(buf);
		//				offset += bytesToWrite;
		//			}

		//			lo.Close();
		//			t.Commit();
					
		//			// If using the Npgsql pooling close the connection to place it back in the pool.
		//			connPgsql.Close();

		//			return noid;
		//		}
		//		else
		//		{
		//			throw new ApplicationException("Not supported in StorageLayer.StoreLargeObject");
		//		}
		//	}
		//	catch(Exception excp)
		//	{
		//		logger.Error("Exception StoreLargeObject. " + excp.Message);
		//		throw excp;
		//	}
		//}
	
		//public byte[] GetLargeObject(int largeObjectId)
		//{
		//	return GetLargeObject(m_storageType, m_dbConnStr, largeObjectId);
		//}
	
		//public byte[] GetLargeObject(StorageTypes storageType, string dbConnString, int largeObjectId)
		//{
		//	try
		//	{
		//		if(storageType == StorageTypes.Postgresql)
		//		{
		//			NpgsqlConnection connPgsql = new NpgsqlConnection(dbConnString);
		//			connPgsql.Open();

		//			NpgsqlTransaction t = connPgsql.BeginTransaction();

		//			LargeObjectManager lbm = new LargeObjectManager(connPgsql);
		//			LargeObject lo =  lbm.Open(largeObjectId, LargeObjectManager.READWRITE);
        
		//			byte[] buffer = lo.Read(lo.Size());

		//			lo.Close();
		//			t.Commit();

		//			// If using the Npgsql pooling close the connection to place it back in the pool.
		//			connPgsql.Close();

		//			return buffer;
		//		}
		//		else
		//		{
		//			throw new ApplicationException("Not supported in StorageLayer.StoreLargeObject");
		//		}
		//	}
		//	catch(Exception excp)
		//	{
		//		logger.Error("Exception StoreLargeObject. " + excp.Message);
		//		throw excp;
		//	}
		//}

        public void StoreByteA(string query, byte[] buffer)
        {
            QuerySecurityCheck(query);
            
            try
            {
                if (m_storageType == StorageTypes.Postgresql)
                {
                    NpgsqlConnection connPgsql = null;

                    try
                    {
                        connPgsql = new NpgsqlConnection(m_dbConnStr);
                        connPgsql.Open();
                        NpgsqlCommand command = new NpgsqlCommand(query, connPgsql);
                        
                        NpgsqlParameter param = new NpgsqlParameter(":bytesData", DbType.Binary);
                        param.Value = buffer;

                        command.Parameters.Add(param);
                        command.ExecuteNonQuery();
                    }
                    catch (Exception npgsqlExcp)
                    {
                        throw new ApplicationException("Exception StoreByteA (Npgsql Connection Pooling). Original exception type " + npgsqlExcp.GetType() + ". " + npgsqlExcp.Message);
                    }
                    finally
                    {
                        // If using the Npgsql pooling close the connection to place it back in the pool.
                        connPgsql.Close();
                    }
                }
                else
                {
                    throw new ApplicationException("StorageType " + m_storageType + " not currently supported in ExecuteScalar");
                }
            }
            catch (Exception excp)
            {
                //logger.Error("Exception ExecuteScalar. " + excp.Message);
                throw excp;
            }
        }

		/// <summary>
		/// Used to determine whethe Npgsql will treat a connection string as pooling or not.
		/// Npgsql assumes a connection string is pooling by default.
		/// </summary>
		public bool IsPooledConnectionString(string dbConnStr)
		{
			if(dbConnStr != null)
			{
				if(Regex.Match(dbConnStr, MinPoolSize, RegexOptions.IgnoreCase).Success)
				{
					return true;
				}
				else if(Regex.Match(dbConnStr, MaxPoolSize, RegexOptions.IgnoreCase).Success)
				{
					return true;
				}
				else if(Regex.Match(dbConnStr, Pooling , RegexOptions.IgnoreCase).Success)
				{
					return false;
				}
			}

			return true;
		}
	
		/// <summary>
		/// 
		/// </summary>
		/// <param name="column">Needs to be a bit data type from Postgresql.</param>
		/// <returns></returns>
		public bool ConvertToBool(object column)
		{
			//Npgsql, this works now in Npgsql version 0.99.3.
			//return Convert.ToBoolean(column);

			if(column.ToString() == "0")
			{
				return false;
			}
			else if(column.ToString() == "1")
			{
				return true;
			}
			else
			{
				throw new ApplicationException("Couldn't convert a database boolean type in StorageLayer.");
			}
		}

        public static bool ConvertToBoolean(object column)
        {
            //Npgsql, this works now in Npgsql version 0.99.3.
            //return Convert.ToBoolean(column);

            if(column == null || column.ToString() == null)
            {
                return false;
            }
            else if (column.ToString() == "0" || column.ToString() == "f" ||  column.ToString() == "F" || Regex.Match(column.ToString(), "false", RegexOptions.IgnoreCase).Success)
            {
                return false;
            }
            else if (column.ToString() == "1" | column.ToString() == "t" ||  column.ToString() == "T" || Regex.Match(column.ToString(), "true", RegexOptions.IgnoreCase).Success)
            {
                return true;
            }
            else
            {
                throw new ApplicationException("Couldn't convert a database boolean type in StorageLayer.");
            }
        }

        public static IDbConnection GetDbConnection(StorageTypes storageType, string dbConnStr) {
            if (storageType == StorageTypes.Postgresql) {
                return new NpgsqlConnection(dbConnStr);
            }
            else if (storageType == StorageTypes.MySQL) {
                return new MySqlConnection(dbConnStr);
            }
            else if (storageType == StorageTypes.MSSQL)
            {
                return new SqlConnection(dbConnStr);
            }
            else
            {
                throw new ApplicationException("Storage type " + storageType + " is not supported by GetDbConnection.");
            }
        }

        public static IDbCommand GetDbCommand(StorageTypes storageType, IDbConnection dbConn, string cmdText) {
            if (storageType == StorageTypes.Postgresql) {
                return new NpgsqlCommand(cmdText, (NpgsqlConnection)dbConn);
            }
            else if (storageType == StorageTypes.MySQL) {
                return new MySqlCommand(cmdText, (MySqlConnection)dbConn);
            }
            else if (storageType == StorageTypes.MSSQL)
            {
                return new SqlCommand(cmdText, (SqlConnection)dbConn);
            }
            else
            {
                throw new ApplicationException("Storage type " + storageType + " is not supported by GetDbConnection.");
            }
        }

        private IDataAdapter GetDataAdapter(StorageTypes storageType, IDbConnection dbConn, string cmdText) {
            if (storageType == StorageTypes.Postgresql) {
                return new NpgsqlDataAdapter(cmdText, (NpgsqlConnection)dbConn);
            }
            else if (storageType == StorageTypes.MySQL) {
                return new MySqlDataAdapter(cmdText, (MySqlConnection)dbConn);
            }
            else if (storageType == StorageTypes.MSSQL)
            {
                return new SqlDataAdapter(cmdText, (SqlConnection)dbConn);
            }
            else
            {
                throw new ApplicationException("Storage type " + storageType + " is not supported by GetDbConnection.");
            }
        }

        public static IDataParameter GetDbParameter(StorageTypes storageType, string name, object value) {
            if (storageType == StorageTypes.Postgresql) {
                return new NpgsqlParameter(name, value);
            }
            else if (storageType == StorageTypes.MySQL) {
                return new MySqlParameter(name, value);
            }
            else if (storageType == StorageTypes.MSSQL)
            {
                return new SqlParameter(name, value);
            }
            else
            {
                throw new ApplicationException("Storage type " + storageType + " is not supported by GetDbParameter.");
            }
        }


        #region Unit tests.
		
		#if UNITTEST

		[TestFixture]
		public class StorageLayerUnitTests
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}

			[Test]
			public void PoolingConnectionStringTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

				string poolingConnStr = "Server=127.0.0.1;Port=23001;User Id=user;Password=password;Database=postgres;MaxPoolSize=15;MinPoolSize=1;CommandTimeout=1;Encoding=UNICODE;";

				StorageLayer storageLayer = new StorageLayer();

				Assert.IsTrue(storageLayer.IsPooledConnectionString(poolingConnStr), "Connection string was not correctly recognised as pooling.");
			}

			[Test]
			public void PoolingConnectionStringNoMinOrMaxTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

				string poolingConnStr = "Server=127.0.0.1;Port=23001;User Id=user;Password=password;Database=postgres;CommandTimeout=1;Encoding=UNICODE;";

				StorageLayer storageLayer = new StorageLayer();

				Assert.IsTrue(storageLayer.IsPooledConnectionString(poolingConnStr), "Connection string was not correctly recognised as pooling.");
			}

			[Test]
			public void NonPoolingConnectionStringTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

				string poolingConnStr = "Server=127.0.0.1;Port=23001;User Id=user;Password=password;Database=postgres;Pooling=false;CommandTimeout=1;Encoding=UNICODE;";

				StorageLayer storageLayer = new StorageLayer();

				Assert.IsTrue(!storageLayer.IsPooledConnectionString(poolingConnStr), "Connection string was not correctly recognised as non pooling.");
			}

		}

		#endif

		#endregion
	}
}
