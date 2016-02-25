// ============================================================================
// FileName: MSSQLAssetPersistor.cs
//
// Description:
// An asset persistor that use Linq-to-SQL for Microsoft's SQL database. 
//
// Author(s):
// Aaron Clauson
//
// History:
// 17 Nov 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Persistence
{
    public class MSSQLAssetPersistor<T> : SIPAssetPersistor<T> where T : class, ISIPAsset, new()
    {
        public override event SIPAssetDelegate<T> Added;
        public override event SIPAssetDelegate<T> Updated;
        public override event SIPAssetDelegate<T> Deleted;
        public override event SIPAssetsModifiedDelegate Modified;

        public MSSQLAssetPersistor(DbProviderFactory factory, string dbConnStr)
        {
            m_dbProviderFactory = factory;
            m_dbConnectionStr = dbConnStr;
            m_objectMapper = new ObjectMapper<T>();
        }

        public override T Add(T asset)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection))
                    {
                        dataContext.GetTable<T>().InsertOnSubmit(asset);
                        dataContext.SubmitChanges();
                    }
                }

                if (Added != null)
                {
                    Added(asset);
                }

                return asset;
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor Add (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Update(T asset)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection))
                    {
                        dataContext.GetTable<T>().Attach(asset, true);
                        dataContext.SubmitChanges();
                    }
                }

                if (Updated != null)
                {
                    Updated(asset);
                }

                return asset;
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor Update (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void UpdateProperty(Guid id, string propertyName, object value)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    connection.ConnectionString = m_dbConnectionStr;
                    connection.Open();

                    IDbCommand updateCommand = connection.CreateCommand();

                    MetaDataMember member = m_objectMapper.GetMember(propertyName);
                    string parameterName = "@1";
                    DbParameter dbParameter = base.GetParameter(m_dbProviderFactory, member, value, parameterName);
                    updateCommand.Parameters.Add(dbParameter);

                    updateCommand.CommandText = "update " + m_objectMapper.TableName + " set " + propertyName + " = " + parameterName + " where id = '" + id + "'";
                    updateCommand.ExecuteNonQuery();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor UpdateProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void IncrementProperty(Guid id, string propertyName)
        {
            base.Increment(id, propertyName);
        }

        public override void DecrementProperty(Guid id, string propertyName)
        {
            base.Decrement(id, propertyName);
        }

        public override void Delete(T asset)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    connection.ConnectionString = m_dbConnectionStr;
                    connection.Open();

                    IDbCommand command = connection.CreateCommand();
                    command.CommandText = "delete from " + m_objectMapper.TableName + " where id = '" + asset.Id + "'";

                    command.ExecuteNonQuery();
                }

                if (Deleted != null)
                {
                    Deleted(asset);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor Delete (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void Delete(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection))
                    {
                        dataContext.GetTable<T>().DeleteAllOnSubmit(from asset in dataContext.GetTable<T>().Where(whereClause) select asset);
                        dataContext.SubmitChanges();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor Delete (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Get(Guid id)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection) { ObjectTrackingEnabled = false })
                    {
                        //return (from asset in dataContext.GetTable<T>() where asset.Id == id select asset).FirstOrDefault();
                        return (from asset in dataContext.GetTable<T>() where asset.Id.Equals(id) select asset).FirstOrDefault();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor Get (id) (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override object GetProperty(Guid id, string propertyName)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    connection.ConnectionString = m_dbConnectionStr;
                    connection.Open();

                    IDbCommand command = connection.CreateCommand();
                    command.CommandText = "select " + propertyName + " from " + m_objectMapper.TableName + " where id = '" + id + "'";

                    return command.ExecuteScalar();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor GetProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override int Count(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection) { ObjectTrackingEnabled = false })
                    {
                        if (whereClause != null)
                        {
                            return (from asset in dataContext.GetTable<T>().Where(whereClause) select asset).Count();
                        }
                        else
                        {
                            return (from asset in dataContext.GetTable<T>() select asset).Count();
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MSSQLAssetPersistor Count (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Get(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection) { ObjectTrackingEnabled = false })
                    {
                        IQueryable<T> getList = null;
                        if (whereClause != null)
                        {
                            getList = from asset in dataContext.GetTable<T>().Where(whereClause) select asset;
                        }
                        else
                        {
                            getList = from asset in dataContext.GetTable<T>() select asset;
                        }
                        return getList.FirstOrDefault();
                    }
                }
            }
            catch (Exception excp)
            {
                string whereClauseStr = (whereClause != null) ? whereClause.ToString() + ". " : null;
                logger.Error("Exception MSSQLAssetPersistor Get (where) (for " + typeof(T).Name + "). " + whereClauseStr + excp.Message);
                throw;
            }
        }

        public override List<T> Get(Expression<Func<T, bool>> whereClause, string orderByField, int offset, int count)
        {
            try
            {
                using (IDbConnection connection = m_dbProviderFactory.CreateConnection())
                {
                    using (DataContext dataContext = new DataContext(connection) { ObjectTrackingEnabled = false })
                    {
                        IQueryable<T> getList = null;
                        if (whereClause != null)
                        {
                            getList = from asset in dataContext.GetTable<T>().Where(whereClause) select asset;
                        }
                        else
                        {
                            getList = from asset in dataContext.GetTable<T>() select asset;
                        }

                        if (!orderByField.IsNullOrBlank())
                        {
                            getList = getList.OrderBy(orderByField);
                        }

                        if (offset != 0)
                        {
                            getList = getList.Skip(offset);
                        }

                        if (count != Int32.MaxValue)
                        {
                            getList = getList.Take(count);
                        }

                        return getList.ToList() ?? new List<T>();
                    }
                }
            }
            catch (Exception excp)
            {
                string whereClauseStr = (whereClause != null) ? whereClause.ToString() + ". " : null;
                logger.Error("Exception MSSQLAssetPersistor Get (list) (for " + typeof(T).Name + "). " + whereClauseStr + excp.Message);
                throw;
            }
        }
    }

    #region Unit testing.

#if UNITTEST

    [TestFixture]
    public class SQLAssetPersistorUnitTest {

        [Table(Name="table")]
        private class MockSIPAsset : ISIPAsset {

            private Guid m_id;
            public Guid Id {
                get { return m_id; }
                set { m_id = value; }
            }

            public DataTable GetTable() {
                throw new NotImplementedException();
            }

            public void Load(DataRow row) {
                throw new NotImplementedException();
            }

            public Dictionary<Guid, object> Load(System.Xml.XmlDocument dom) {
                throw new NotImplementedException();
            }

            public string ToXML() {
                throw new NotImplementedException();
            }

            public string ToXMLNoParent() {
                throw new NotImplementedException();
            }

            public string GetXMLElementName() {
                throw new NotImplementedException();
            }

            public string GetXMLDocumentElementName() {
                throw new NotImplementedException();
            }
        }
     
        [TestFixtureSetUp]
        public void Init() { }

        [TestFixtureTearDown]
        public void Dispose() { }

        [Test]
        public void SampleTest() {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
        }

        /*[Test]
        public void BuildSingleParameterSelectQueryUnitTest() {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            SimpleDBAssetPersistor<MockSIPAsset> persistor = new SimpleDBAssetPersistor<MockSIPAsset>(null, null);
            string selectQuery = persistor.BuildSelectQuery("select * from table where inserted < ?1", new SqlParameter("1", DateTime.Now));
            Console.WriteLine(selectQuery);
        }

        [Test]
        public void BuildMultipleParameterSelectQueryUnitTest() {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            SimpleDBAssetPersistor<MockSIPAsset> persistor = new SimpleDBAssetPersistor<MockSIPAsset>(null, null);
            SqlParameter[] parameters = new SqlParameter[2];
            parameters[0] = new SqlParameter("1", DateTime.Now);
            parameters[1] = new SqlParameter("2", "test");
            string selectQuery = persistor.BuildSelectQuery("select * from table where inserted < ?1 and name = ?2", parameters);
            Console.WriteLine(selectQuery);
        }*/
    }

#endif

    #endregion
}
