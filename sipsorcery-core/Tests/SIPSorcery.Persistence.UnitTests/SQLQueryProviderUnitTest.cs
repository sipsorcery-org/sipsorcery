using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Linq.Mapping;
using System.Linq.Expressions;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Persistence;

namespace SIPSorcery.Persistence.UnitTests
{
    [TestClass]
    public class SQLQueryProviderUnitTest
    {

        [Table(Name = "table")]
        private class MockSIPAsset : ISIPAsset
        {
            [Column(Name = "id", DbType = "varchar(36)", IsPrimaryKey = true, CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
            public Guid Id { get; set; }

            public string Username { get; set; }

            public string AdminId { get; set; }

            [Column(Name = "expired", DbType = "boolean", CanBeNull = false)]
            public bool Expired { get; set; }

            [Column(Name = "inserted", DbType = "datetimeoffset", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
            public DateTime Inserted { get; set; }

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

        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
        }

        /// <summary>
        /// Check that the query text is generated correctly for a select based on an object's id.
        /// </summary>
        [TestMethod]
        public void SimpleSelectOnIdTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            Guid id = Guid.NewGuid();
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Id == id;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "id = '" + id + "'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly for a select containing a DateTime.
        /// </summary>
        [TestMethod]
        public void SimpleSelectOnDateTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            DateTime checkDate = DateTime.Now;
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Inserted >= checkDate;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly for a select containing an "and" operator.
        /// </summary>
        [TestMethod]
        public void SelectWithAndOperatorTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.AdminId == "1234" && asset.Username == "abcd";
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "adminid = '1234' and username = 'abcd'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a locally scoped variable.
        /// </summary>
        [TestMethod]
        public void SelectWithLocallyScopedVariableOperatorTest()
        {

            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            string username = "efgh";
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == username;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "username = '" + username + "'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a boolean not operator
        /// </summary>
        [TestMethod]
        public void SelectWithNotOperatorTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => !asset.Expired;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext + ".");

            Assert.IsTrue(querytext == "not (expired = '1')", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a boolean not operator.
        /// </summary>
        [TestMethod]
        public void SelectWithBooleanTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Expired;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext + ".");

            Assert.IsTrue(querytext == "expired = '1'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a boolean not operator
        /// and an And clause.
        /// </summary>
        [TestMethod]
        public void SelectWithNotCombinedWithAndOperatorTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            DateTime checkDate = DateTime.Now;
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => !asset.Expired && asset.Inserted >= checkDate;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext + ".");

            Assert.IsTrue(querytext == "not (expired = '1') and inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a member variable.
        /// </summary>
        [TestMethod]
        public void SelectWithMemberVariableOperatorTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            var myObj = new { Name = "xyz" };
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == myObj.Name;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "username = '" + myObj.Name + "'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a member variable.
        /// </summary>
        [TestMethod]
        public void SelectWithNotEqualMemberVariableOperatorTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            var myObj = new { Name = "xyz" };
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username != myObj.Name;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "username != '" + myObj.Name + "'", "The query text was incorrect.");
        }

        /// <summary>
        /// Check that the query text is generated correctly when the select contains a member variable
        /// combined with an And clause.
        /// </summary>
        [TestMethod]
        public void SelectWithMemberVariableCombinedWithAndClauseOperatorTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
            var myObj = new { Name = "xyz" };
            DateTime checkDate = DateTime.Now;
            Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == myObj.Name && !asset.Expired && asset.Inserted >= checkDate;
            string querytext = queryProvider.GetQueryText(whereClause);
            Console.WriteLine("Query: " + querytext);

            Assert.IsTrue(querytext == "username = '" + myObj.Name + "' and not (expired = '1') and inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
        }
    }
}
