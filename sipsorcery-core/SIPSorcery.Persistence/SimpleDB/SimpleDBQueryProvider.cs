using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Linq.Mapping;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Persistence {

    public class SimpleDBQueryProvider : QueryProvider {

        private static ILog logger = AppState.logger;

        private AmazonSimpleDB m_service;
        private string m_domainName;
        private SetterDelegate m_setter;

        public string OrderBy;
        public int Offset;
        public int Count = Int32.MaxValue;

        public SimpleDBQueryProvider(AmazonSimpleDB service, string domainName, SetterDelegate setter) {
            m_service = service;
            m_domainName = domainName;
            m_setter = setter;
        }

        public override string GetQueryText(Expression expression) {
            return this.Translate(expression);
        }

        public override object Execute(Expression expression) {

            try {
                Type elementType = TypeSystem.GetElementType(expression.Type);
                string methodName = ((MethodCallExpression)expression).Method.Name;
                bool isIQueryable = expression.Type.FullName.StartsWith("System.Linq.IQueryable");
                string queryString = String.Format(this.Translate(expression), m_domainName);

                if (!OrderBy.IsNullOrBlank()) {
                    string orderByField = (OrderBy.IndexOf(' ') != -1) ? OrderBy.Substring(0, OrderBy.IndexOf(' ')) : OrderBy;
                    // SimpleDB queries with an order clause must have the order field included as a predicate.
                    // If the select query does not contain the a predicate with the order field add it here.
                    if (!queryString.Contains(orderByField)) {
                        queryString += " and " + orderByField + " like '%'";
                    }
                    queryString += " order by " + OrderBy;
                }

                if (Count != Int32.MaxValue) {
                    queryString += " limit " + Count;
                }

                //logger.Debug(queryString);

                if (!queryString.IsNullOrBlank()) {
                    //logger.Debug("SimpleDB select: " + queryString + ".");
                    SelectRequest request = new SelectRequest();
                    request.SelectExpression = queryString;
                    SelectResponse response = m_service.Select(request);
                    if (response.IsSetSelectResult()) {

                        if (elementType == typeof(Int32)) {
                            return Convert.ToInt32(response.SelectResult.Item[0].Attribute[0].Value);
                        }
                        else {
                            object result = Activator.CreateInstance(
                            typeof(SimpleDBObjectReader<>).MakeGenericType(elementType),
                            BindingFlags.Instance | BindingFlags.Public, null,
                            new object[] { response.SelectResult, m_setter },
                            null);

                            if (isIQueryable) {
                                return result;
                            }
                            else {
                                IEnumerator enumerator = ((IEnumerable)result).GetEnumerator();
                                if (enumerator.MoveNext()) {
                                    return enumerator.Current;
                                }
                                else {
                                    return null;
                                }
                            }
                        }
                    }
                    throw new ApplicationException("No results for SimpleDB query.");
                }
                else {
                    throw new ApplicationException("The expression translation by the SimpleDBQueryProvider resulted in an empty select string.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SimpleDBQueryProvider Execute. " + expression.ToString() + ". " + excp.Message);
                throw;
            }
        }

        private string Translate(Expression expression) {
            expression = Evaluator.PartialEval(expression);
            return new SimpleDBExpressionVisitor().Translate(expression);
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
        public class SimpleDBQueryProviderUnitTest {

            [Table(Name = "table")]
            private class MockSIPAsset : ISIPAsset {

                private Guid m_id;
                [Column(Storage = "m_id", Name = "id", DbType = "character varying(36)", IsPrimaryKey = true, CanBeNull = false)]
                public Guid Id {
                    get { return m_id; }
                    set { m_id = value; }
                }

                private string m_username;
                public string Username {
                    get { return m_username; }
                    set { m_username = value; }
                }

                private string m_adminId;
                public string AdminId {
                    get { return m_adminId; }
                    set { m_adminId = value; }
                }

                private bool m_expired;
                [Column(Name = "expired", DbType = "boolean", CanBeNull = false)]
                public bool Expired {
                    get { return m_expired; }
                    set { m_expired = value; }
                }

                private DateTime m_inserted;
                public DateTime Inserted {
                    get { return m_inserted; }
                    set { m_inserted = value; } 
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
            public void Init() {
                log4net.Config.BasicConfigurator.Configure();
            }

            [TestFixtureTearDown]
            public void Dispose() { }

            [Test]
            public void SampleTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            }

            /// <summary>
            /// Check that the query text is generated correctly for a select based on an object's id.
            /// </summary>
            [Test]
            public void SimpleSelectOnIdTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                Guid testGuid = Guid.NewGuid();
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Id == testGuid;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "itemName() = '" + testGuid + "'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly for a select containing a DateTime.
            /// </summary>
            [Test]
            public void SimpleSelectOnDateTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                DateTime checkDate = DateTime.Now;
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Inserted >= checkDate;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly for a select containing an "and" operator.
            /// </summary>
            [Test]
            public void SelectWithAndOperatorTest() {

                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                Guid id = Guid.NewGuid();
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Id == id && asset.Username == "abcd";
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "itemName() = '" + id + "' and username = 'abcd'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a locally scoped variable.
            /// </summary>
            [Test]
            public void SelectWithLocallyScopedVariableOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                string username = "efgh";
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == username;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "username = '" + username + "'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a locally scoped embedded variable.
            /// </summary>
            [Test]
            public void SelectWithLocallyScopedEmbeddedVariableOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Inserted < DateTime.UtcNow;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(Regex.Match(querytext, @"inserted < '\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z'").Success, "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a boolean not operator
            /// </summary>
            [Test]
            public void SelectWithNotOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => !asset.Expired;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext + ".");

                Assert.IsTrue(querytext == "not (expired = 'True')", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a boolean not operator.
            /// </summary>
            [Test]
            public void SelectWithBooleanTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Expired;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext + ".");

                Assert.IsTrue(querytext == "expired = 'True'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a boolean not operator
            /// and an And clause.
            /// </summary>
            [Test]
            public void SelectWithNotCombinedWithAndOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                DateTime checkDate = DateTime.Now;
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => !asset.Expired && asset.Inserted >= checkDate;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext + ".");

                Assert.IsTrue(querytext == "not (expired = 'True') and inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a member variable.
            /// </summary>
            [Test]
            public void SelectWithMemberVariableOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                var myObj = new { Name = "xyz" }; 
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == myObj.Name;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "username = '" + myObj.Name + "'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a member variable.
            /// </summary>
            [Test]
            public void SelectWithNotEqualMemberVariableOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
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
            [Test]
            public void SelectWithMemberVariableCombinedWithAndClauseOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SimpleDBQueryProvider queryProvider = new SimpleDBQueryProvider(null, null, null);
                var myObj = new { Name = "xyz" };
                DateTime checkDate = DateTime.Now;
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == myObj.Name && !asset.Expired && asset.Inserted >= checkDate;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "username = '" + myObj.Name + "' and not (expired = 'True') and inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
            }
        }
        
        #endif

        #endregion
    }
}
