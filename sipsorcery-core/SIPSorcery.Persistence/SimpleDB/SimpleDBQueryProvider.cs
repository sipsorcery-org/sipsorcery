using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;
using log4net;

namespace SIPSorcery.Persistence {

    public class SimpleDBQueryProvider : QueryProvider {

        private static ILog logger = AppState.logger;

        private AmazonSimpleDB m_service;
        private string m_domainName;
        private SetterDelegate m_setter;

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
                logger.Debug(queryString);

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
                            typeof(ObjectReader<>).MakeGenericType(elementType),
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
    }
}
