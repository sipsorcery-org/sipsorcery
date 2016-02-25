using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Transactions;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Persistence {

    public class SQLQueryProvider : QueryProvider {

        private static ILog logger = AppState.logger;

        private DbProviderFactory m_dbFactory;
        private string m_dbConnStr;
        private string m_tableName;
        private SetterDelegate m_setter;

        public string OrderBy;
        public int Offset;
        public int Count = Int32.MaxValue;

        public SQLQueryProvider(DbProviderFactory dbFactory, string dbConnStr, string tableName, SetterDelegate setter) {

            m_dbFactory = dbFactory;
            m_dbConnStr = dbConnStr;
            m_tableName = tableName;
            m_setter = setter;
        }

        public override string GetQueryText(Expression expression) {
            return this.Translate(expression);
        }

        public override object Execute(Expression expression) {

            try {
                Type elementType = TypeSystem.GetElementType(expression.Type);
                string methodName = ((MethodCallExpression)expression).Method.Name;
                bool isIQueryable = (expression.Type.FullName.StartsWith("System.Linq.IQueryable") || expression.Type.FullName.StartsWith("System.Linq.IOrderedQueryable"));
                string queryString = String.Format(this.Translate(expression), m_tableName);
    
                if (!OrderBy.IsNullOrBlank()) {
                    queryString += " order by " + OrderBy;
                }

                if (Count != Int32.MaxValue) {
                    queryString += " limit " + Count;
                }

                if (Offset != 0) {
                    queryString += " offset " + Offset;
                }

                //logger.Debug(queryString);

                if (!queryString.IsNullOrBlank()) {

                    using(TransactionScope transactionScope = new TransactionScope(TransactionScopeOption.Suppress))
                    {
                    using (IDbConnection connection = m_dbFactory.CreateConnection()) {
                        connection.ConnectionString = m_dbConnStr;
                        connection.Open();

                        if (elementType == typeof(Int32))
                        {
                            // This is a count.
                            IDbCommand command = connection.CreateCommand();
                            command.CommandText = queryString;
                            return Convert.ToInt32(command.ExecuteScalar());
                        }
                        else
                        {
                            //logger.Debug("SimpleDB select: " + queryString + ".");
                            IDbCommand command = connection.CreateCommand();
                            command.CommandText = queryString;
                            IDbDataAdapter adapter = m_dbFactory.CreateDataAdapter();
                            adapter.SelectCommand = command;
                            DataSet resultSet = new DataSet();
                            adapter.Fill(resultSet);

                            if (resultSet != null && resultSet.Tables[0] != null)
                            {

                                object result = Activator.CreateInstance(
                                typeof(SQLObjectReader<>).MakeGenericType(elementType),
                                BindingFlags.Instance | BindingFlags.Public, null,
                                new object[] { resultSet, m_setter },
                                null);

                                if (isIQueryable)
                                {
                                    return result;
                                }
                                else
                                {
                                    IEnumerator enumerator = ((IEnumerable)result).GetEnumerator();
                                    if (enumerator.MoveNext())
                                    {
                                        return enumerator.Current;
                                    }
                                    else
                                    {
                                        return null;
                                    }
                                }
                            }
                        }
                        }
                        throw new ApplicationException("No results for SQL query.");
                    }
                }
                else {
                    throw new ApplicationException("The expression translation by the SQLQueryProvider resulted in an empty select string.");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SQLQueryProvider Execute. " + expression.ToString() + ". " + excp.Message);
                throw;
            }
        }

        private string Translate(Expression expression) {
            //Stopwatch timer = new Stopwatch();
            //timer.Start();
            expression = Evaluator.PartialEval(expression);
            string translation = new SQLExpressionVisitor().Translate(expression);
            //timer.Stop();
            //logger.Debug("SQL query took " + timer.ElapsedMilliseconds + "ms: " + translation + ".");
            return translation;
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
        public class SQLQueryProviderUnitTest {

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
            [Test]
            public void SimpleSelectOnDateTest() {
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
            [Test]
            public void SelectWithAndOperatorTest() {
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
            [Test]
            public void SelectWithLocallyScopedVariableOperatorTest() {

                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
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

                SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => !asset.Expired;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext + ".");

                Assert.IsTrue(querytext == "not (expired = '1')", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select contains a boolean not operator.
            /// </summary>
            [Test]
            public void SelectWithBooleanTest() {
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
            [Test]
            public void SelectWithNotCombinedWithAndOperatorTest() {
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
            [Test]
            public void SelectWithMemberVariableOperatorTest() {
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
            [Test]
            public void SelectWithNotEqualMemberVariableOperatorTest() {
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
            [Test]
            public void SelectWithMemberVariableCombinedWithAndClauseOperatorTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
                var myObj = new { Name = "xyz" };
                DateTime checkDate = DateTime.Now;
                Expression<Func<MockSIPAsset, bool>> whereClause = (asset) => asset.Username == myObj.Name && !asset.Expired && asset.Inserted >= checkDate;
                string querytext = queryProvider.GetQueryText(whereClause);
                Console.WriteLine("Query: " + querytext);

                Assert.IsTrue(querytext == "username = '" + myObj.Name + "' and not (expired = '1') and inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
            }

            /// <summary>
            /// Check that the query text is generated correctly when the select expression includes an orderby clause.
            /// </summary>
            [Test]
            public void OrderedSelectTest() {

                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                SQLQueryProvider queryProvider = new SQLQueryProvider(null, null, null, null);
                Query<MockSIPAsset> assetList = new Query<MockSIPAsset>(queryProvider);

                DateTime checkDate = DateTime.Now;
                var dummyResult = from asset in assetList orderby asset.Inserted select asset;
                string querytext = queryProvider.GetQueryText(dummyResult.Expression);
                Console.WriteLine("Query: " + querytext);

                //Assert.IsTrue(querytext == "username = '" + myObj.Name + "' and not (expired = '1') and inserted >= '" + checkDate.ToString("o") + "'", "The query text was incorrect.");
            }
        }
        
        #endif

        #endregion
    }
}
