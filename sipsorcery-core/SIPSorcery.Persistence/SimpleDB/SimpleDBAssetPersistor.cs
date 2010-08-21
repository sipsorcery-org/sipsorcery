// ============================================================================
// FileName: SimpleDBAssetPersistor.cs
//
// Description:
// An asset persistor for Amazon's SimpleDB data store. 
//
// Author(s):
// Aaron Clauson
//
// History:
// 24 Sep 2009	Aaron Clauson	Created.
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Linq.Mapping;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Persistence
{
    public class SimpleDBAssetPersistor<T> : SIPAssetPersistor<T> where T : class, ISIPAsset, new()
    {
        private AmazonSimpleDBClient m_simpleDBClient;

        public override event SIPAssetDelegate<T> Added;
        public override event SIPAssetDelegate<T> Updated;
        public override event SIPAssetDelegate<T> Deleted;
        public override event SIPAssetsModifiedDelegate Modified;

        public SimpleDBAssetPersistor(string dbConnStr)
        {
            string awsKeyID = Regex.Match(dbConnStr, "AWSKeyID=(?<keyid>.+?)(;|$)").Result("${keyid}");
            string awsSecretKey = Regex.Match(dbConnStr, "AWSSecretKey=(?<secretkey>.+?)(;|$)").Result("${secretkey}");
            m_simpleDBClient = new AmazonSimpleDBClient(awsKeyID, awsSecretKey);
            m_objectMapper = new ObjectMapper<T>();
        }

        public SimpleDBAssetPersistor(string awsAccessKeyId, string awsSecretAccessKey)
        {
            m_simpleDBClient = new AmazonSimpleDBClient(awsAccessKeyId, awsSecretAccessKey);
            m_objectMapper = new ObjectMapper<T>();
        }

        private T Upsert(T asset)
        {
            PutAttributesRequest request = new PutAttributesRequest();
            request.DomainName = m_objectMapper.TableName;
            request.ItemName = asset.Id.ToString();
            request.Attribute = new List<ReplaceableAttribute>();
            Dictionary<MetaDataMember, object> allPropertyValues = m_objectMapper.GetAllValues(asset);

            foreach (KeyValuePair<MetaDataMember, object> propertyValue in allPropertyValues)
            {
                if (!propertyValue.Key.IsPrimaryKey)
                {
                    if (propertyValue.Value != null)
                    {
                        request.Attribute.Add(new ReplaceableAttribute().WithReplace(true).WithName(propertyValue.Key.MappedName.ToLower()).WithValue(GetValue(propertyValue.Value)));
                        /*if (propertyValue.Key.Type == typeof(DateTime) || propertyValue.Key.Type == typeof(Nullable<DateTime>)) {
                            //logger.Debug("Upsert adding attribute name=" + propertyValue.Key.Name.ToLower() + ", value=" + ((DateTime)propertyValue.Value).ToString("o") + ".");
                            request.Attribute.Add(new ReplaceableAttribute().WithReplace(true).WithName(propertyValue.Key.MappedName.ToLower()).WithValue(((DateTime)propertyValue.Value).ToString("o")));
                        }
                        else {
                            //logger.Debug("Upsert adding attribute name=" + propertyValue.Key.Name + ", value=" + propertyValue.Value.ToString() + ".");
                            request.Attribute.Add(new ReplaceableAttribute().WithReplace(true).WithName(propertyValue.Key.MappedName.ToLower()).WithValue(propertyValue.Value.ToString()));
                        }*/
                    }
                    //else {
                    //     request.Attribute.Add(new ReplaceableAttribute().WithReplace(true).WithName(propertyValue.Key.Name).WithValue(null));
                    // }
                }
            }

            PutAttributesResponse response = m_simpleDBClient.PutAttributes(request);

            if (response.IsSetResponseMetadata())
            {
                ResponseMetadata responseMetadata = response.ResponseMetadata;
                //logger.Debug("Upsert response for " + request.DomainName + ", id=" + request.ItemName + ", attributes=" + request.Attribute.Count + ": " + responseMetadata.RequestId);
            }

            return asset;
        }

        public override T Add(T asset)
        {
            try
            {
                T addedAsset = Upsert(asset);

                if (Added != null)
                {
                    Added(addedAsset);
                }

                return addedAsset;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor Add (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Update(T asset)
        {
            try
            {
                T updatedAsset = Upsert(asset);

                if (Updated != null)
                {
                    Updated(updatedAsset);
                }

                return updatedAsset;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor Update (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void UpdateProperty(Guid id, string propertyName, object value)
        {
            try
            {
                PutAttributesRequest request = new PutAttributesRequest();
                request.DomainName = m_objectMapper.TableName;
                request.ItemName = id.ToString();
                request.Attribute = new List<ReplaceableAttribute>();
                request.Attribute.Add(new ReplaceableAttribute().WithReplace(true).WithName(propertyName.ToLower()).WithValue(GetValue(value)));

                PutAttributesResponse response = m_simpleDBClient.PutAttributes(request);

                if (response.IsSetResponseMetadata())
                {
                    ResponseMetadata responseMetadata = response.ResponseMetadata;
                    logger.Debug("UpdateProperty response: " + responseMetadata.RequestId);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor UpdateProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void Delete(T asset)
        {
            try
            {
                DeleteAttributesRequest deleteRequest = new DeleteAttributesRequest();
                deleteRequest.DomainName = m_objectMapper.TableName;
                deleteRequest.ItemName = asset.Id.ToString();

                DeleteAttributesResponse response = m_simpleDBClient.DeleteAttributes(deleteRequest);

                if (response.IsSetResponseMetadata())
                {
                    ResponseMetadata responseMetadata = response.ResponseMetadata;
                    logger.Debug("Delete response: " + responseMetadata.RequestId);
                }

                if (Deleted != null)
                {
                    Deleted(asset);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor Delete (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override void Delete(Expression<Func<T, bool>> where)
        {
            try
            {
                List<T> items = Get(where, null, 0, Int32.MaxValue);

                if (items != null && items.Count > 0)
                {
                    foreach (T item in items)
                    {
                        DeleteAttributesRequest deleteRequest = new DeleteAttributesRequest();
                        deleteRequest.DomainName = m_objectMapper.TableName;
                        deleteRequest.ItemName = item.Id.ToString();

                        DeleteAttributesResponse response = m_simpleDBClient.DeleteAttributes(deleteRequest);

                        if (response.IsSetResponseMetadata())
                        {
                            ResponseMetadata responseMetadata = response.ResponseMetadata;
                            logger.Debug("Delete response: " + responseMetadata.RequestId);
                        }

                        if (Deleted != null)
                        {
                            Deleted(item);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor Delete (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Get(Guid id)
        {
            try
            {
                SelectRequest request = new SelectRequest();
                request.SelectExpression = "select * from " + m_objectMapper.TableName + " where itemName() = '" + id + "'";
                SelectResponse response = m_simpleDBClient.Select(request);
                if (response.IsSetSelectResult() && response.SelectResult.Item.Count == 1)
                {
                    SimpleDBObjectReader<T> objectReader = new SimpleDBObjectReader<T>(response.SelectResult, m_objectMapper.SetValue);
                    return objectReader.First();
                }
                else if (response.IsSetSelectResult() && response.SelectResult.Item.Count == 1)
                {
                    throw new ApplicationException("Multiple rows were returned for Get with id=" + id + ".");
                }
                else
                {
                    return default(T);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor Get(for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override object GetProperty(Guid id, string propertyName)
        {
            try
            {
                SelectRequest request = new SelectRequest();
                request.SelectExpression = "select " + propertyName.ToLower() + " from " + m_objectMapper.TableName + " where itemName() = '" + id.ToString() + "'";
                SelectResponse response = m_simpleDBClient.Select(request);
                if (response.IsSetSelectResult())
                {
                    return response.SelectResult.Item[0].Attribute[0].Value;
                }
                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor GetProperty (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override int Count(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                SimpleDBQueryProvider simpleDBQueryProvider = new SimpleDBQueryProvider(m_simpleDBClient, m_objectMapper.TableName, m_objectMapper.SetValue);
                Query<T> assets = new Query<T>(simpleDBQueryProvider);
                if (whereClause != null)
                {
                    return assets.Where(whereClause).Count();
                }
                else
                {
                    return assets.Count();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SimpleDBAssetPersistor Count (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        public override T Get(Expression<Func<T, bool>> whereClause)
        {
            try
            {
                SimpleDBQueryProvider simpleDBQueryProvider = new SimpleDBQueryProvider(m_simpleDBClient, m_objectMapper.TableName, m_objectMapper.SetValue);
                Query<T> assets = new Query<T>(simpleDBQueryProvider);
                IQueryable<T> getList = null;
                if (whereClause != null)
                {
                    getList = from asset in assets.Where(whereClause) select asset;
                }
                else
                {
                    getList = from asset in assets select asset;
                }
                return getList.FirstOrDefault();
            }
            catch (Exception excp)
            {
                string whereClauseStr = (whereClause != null) ? whereClause.ToString() + ". " : null;
                logger.Error("Exception SimpleDBAssetPersistor Get (where) (for " + typeof(T).Name + "). " + whereClauseStr + excp.Message);
                throw;
            }
        }

        public override List<T> Get(Expression<Func<T, bool>> whereClause, string orderByField, int offset, int count)
        {
            try
            {
                SimpleDBQueryProvider simpleDBQueryProvider = new SimpleDBQueryProvider(m_simpleDBClient, m_objectMapper.TableName, m_objectMapper.SetValue);
                Query<T> assetList = new Query<T>(simpleDBQueryProvider);
                //IQueryable<T> getList = from asset in assetList.Where(whereClause) orderby orderByField select asset;
                IQueryable<T> getList = null;
                if (whereClause != null)
                {
                    getList = from asset in assetList.Where(whereClause) select asset;
                }
                else
                {
                    getList = from asset in assetList select asset;
                }

                if (!orderByField.IsNullOrBlank())
                {
                    simpleDBQueryProvider.OrderBy = orderByField;
                }

                //if (offset != 0) {
                //   simpleDBQueryProvider.Offset = offset;
                //}

                if (count != Int32.MaxValue)
                {
                    simpleDBQueryProvider.Count = count;
                }

                return getList.ToList() ?? new List<T>();
            }
            catch (Exception excp)
            {
                string whereClauseStr = (whereClause != null) ? whereClause.ToString() + ". " : null;
                logger.Error("Exception SimpleDBAssetPersistor Get (list) (for " + typeof(T).Name + "). " + whereClauseStr + excp.Message);
                throw;
            }
        }

        private string GetValue(object propertyValue)
        {
            if (propertyValue == null)
            {
                return null;
            }
            else
            {
                if (propertyValue.GetType() == typeof(DateTime) || propertyValue.GetType() == typeof(Nullable<DateTime>))
                {
                    return ((DateTime)propertyValue).ToString("o");
                }
                else
                {
                    return propertyValue.ToString();
                }
            }
        }

        /*public override T GetFromDirectQuery(string sqlQuery, params IDbDataParameter[] sqlParameters) {
            return GetSimpleDBObject(BuildSelectQuery(sqlQuery, sqlParameters));
        }

        public override List<T> GetListFromDirectQuery(string sqlQuery, params IDbDataParameter[] sqlParameters) {
            try {
                SelectRequest request = new SelectRequest();
                request.SelectExpression = BuildSelectQuery(sqlQuery, sqlParameters);
                SelectResponse response = m_simpleDBClient.Select(request);
                if (response.IsSetSelectResult() && response.SelectResult.Item.Count > 0) {
                    SimpleDBObjectReader<T> objectReader = new SimpleDBObjectReader<T>(response.SelectResult, m_objectMapper.SetValue);
                    return objectReader.ToList();
                }
                else {
                    return new List<T>();
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SimpleDBAssetPersistor GetListFromDirectQuery (for " + typeof(T).Name + "). " + excp.Message);
                throw;
            }
        }

        internal string BuildSelectQuery(string sqlQuery, params IDbDataParameter[] sqlParameters) {
            string queryText = sqlQuery;
            foreach (IDbDataParameter parameter in sqlParameters) {
                if (parameter.DbType == DbType.DateTime) {
                    queryText = queryText.Replace("?" + parameter.ParameterName, "'" + ((DateTime)parameter.Value).ToString("o") + "'");
                }
                else {
                    queryText = queryText.Replace("?" + parameter.ParameterName, "'" + parameter.Value.ToString() + "'");
                }
            }
            //logger.Debug(queryText);
            return queryText;
        }*/
    }

    #region Unit testing.

#if UNITTEST

    [TestFixture]
    public class SimpleDBAssetPersistorUnitTest {

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
