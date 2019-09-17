using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Linq.Mapping;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Persistence;

namespace SIPSorcery.Persistence.UnitTests
{
    [TestClass]
    public class SQLAssetPersistorUnitTest
    {
        [Table(Name = "table")]
        private class MockSIPAsset : ISIPAsset
        {

            private Guid m_id;
            public Guid Id
            {
                get { return m_id; }
                set { m_id = value; }
            }

            public DataTable GetTable()
            {
                throw new NotImplementedException();
            }

            public void Load(DataRow row)
            {
                throw new NotImplementedException();
            }

            public Dictionary<Guid, object> Load(System.Xml.XmlDocument dom)
            {
                throw new NotImplementedException();
            }

            public string ToXML()
            {
                throw new NotImplementedException();
            }

            public string ToXMLNoParent()
            {
                throw new NotImplementedException();
            }

            public string GetXMLElementName()
            {
                throw new NotImplementedException();
            }

            public string GetXMLDocumentElementName()
            {
                throw new NotImplementedException();
            }
        }

        //[TestMethod]
        //public void BuildSingleParameterSelectQueryUnitTest()
        //{
        //    Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    SQLAssetPersistor<MockSIPAsset> persistor = new SQLAssetPersistor<MockSIPAsset>(null, null);
        //    string selectQuery = persistor.("select * from table where inserted < ?1", new SqlParameter("1", DateTime.Now));
        //    Console.WriteLine(selectQuery);
        //}

        //[TestMethod]
        //public void BuildMultipleParameterSelectQueryUnitTest()
        //{
        //    Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    SimpleDBAssetPersistor<MockSIPAsset> persistor = new SimpleDBAssetPersistor<MockSIPAsset>(null, null);
        //    SqlParameter[] parameters = new SqlParameter[2];
        //    parameters[0] = new SqlParameter("1", DateTime.Now);
        //    parameters[1] = new SqlParameter("2", "test");
        //    string selectQuery = persistor.BuildSelectQuery("select * from table where inserted < ?1 and name = ?2", parameters);
        //    Console.WriteLine(selectQuery);
        //}
    }
}
