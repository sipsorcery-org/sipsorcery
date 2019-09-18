using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Transactions;
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
    }
}
