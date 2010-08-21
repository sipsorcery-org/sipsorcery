using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Data.Linq.Mapping;
using System.Text;

namespace SIPSorcery.Persistence {
 
    internal class SQLExpressionVisitor : ExpressionVisitor {

        private StringBuilder sb;
        private bool m_isLeaf;
        private bool m_whereAdded;

        internal SQLExpressionVisitor() { }

        internal string Translate(Expression expression) {
            this.sb = new StringBuilder();
            this.Visit(expression);
            string query = this.sb.ToString().Trim();
            return query;
        }

        private static Expression StripQuotes(Expression e) {
            while (e.NodeType == ExpressionType.Quote) {
                e = ((UnaryExpression)e).Operand;
            }
            return e;
        }

        protected override Expression VisitMethodCall(MethodCallExpression m) {
            if (m.Method.DeclaringType == typeof(Queryable) &&
                (m.Method.Name == "Where" || m.Method.Name == "Select" || m.Method.Name == "Count" ||
                    m.Method.Name == "FirstOrDefault")) {

                if (m_isLeaf) {
                    if (m.Method.Name == "Where" && !m_whereAdded) {
                        sb.Append(" where ");
                        m_whereAdded = true;
                    }

                    this.Visit(m.Arguments[0]);
                    LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                    this.Visit(lambda.Body);
                    return m;
                }
                else {
                    if (m.Method.Name == "Select") {
                        m_isLeaf = true;
                        sb.Append("select * from {0}");
                        this.Visit(m.Arguments[0]);
                        LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                        this.Visit(lambda.Body);
                        return m;
                    }
                    else if (m.Method.Name == "Where") {
                        m_isLeaf = true;
                        m_whereAdded = true;
                        sb.Append("select * from {0} where ");
                        this.Visit(m.Arguments[0]);
                        LambdaExpression lambda = (LambdaExpression)StripQuotes(m.Arguments[1]);
                        this.Visit(lambda.Body);
                        return m;
                    }
                    else if (m.Method.Name == "Count") {
                        m_isLeaf = true;
                        sb.Append("select count(*) from {0}");
                        this.Visit(m.Arguments[0]);
                        return m;
                    }
                    else if (m.Method.Name == "FirstOrDefault") {
                        m_isLeaf = true;
                        sb.Append("select * from {0}");
                        this.Visit(m.Arguments[0]);
                        sb.Append(" limit 1");
                        return m;
                    }
                }
            }

            throw new NotSupportedException(string.Format("The method '{0}' is not supported by SQL.", m.Method.Name));
        }

        protected override Expression VisitUnary(UnaryExpression u) {
            switch (u.NodeType) {
                case ExpressionType.Not:
                    sb.Append("not (");
                    this.Visit(u.Operand);
                    sb.Append(")");
                    break;
                default:
                    throw new NotSupportedException(string.Format("The unary operator '{0}' is not supported by SQL.", u.NodeType));
            }
            return u;
        }

        protected override Expression VisitBinary(BinaryExpression b) {
            this.Visit(b.Left);
            switch (b.NodeType) {
                case ExpressionType.And | ExpressionType.AndAlso:
                    sb.Append(" and ");
                    break;
                case ExpressionType.Or:
                    sb.Append(" or ");
                    break;
                case ExpressionType.Equal:
                    sb.Append(" = ");
                    break;
                case ExpressionType.NotEqual:
                    sb.Append(" != ");
                    break;
                case ExpressionType.LessThan:
                    sb.Append(" < ");
                    break;
                case ExpressionType.LessThanOrEqual:
                    sb.Append(" <= ");
                    break;
                case ExpressionType.GreaterThan:
                    sb.Append(" > ");
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    sb.Append(" >= ");
                    break;
                default:
                    throw new NotSupportedException(string.Format("The binary operator '{0}' is not supported by SQL.", b.NodeType));
            }

            this.Visit(b.Right);
            return b;
        }

        protected override Expression VisitConstant(ConstantExpression c) {
            IQueryable q = c.Value as IQueryable;
            if (q != null) {
                //throw new ApplicationException("Nested expressions not supported.");
            }
            else if (c.Value == null) {
                sb.Append("null");
            }
            else {
                if (c.Value.GetType() == typeof(Guid)) {
                    sb.Append("'");
                    sb.Append(c.Value);
                    sb.Append("'");
                }
                else {
                    switch (Type.GetTypeCode(c.Value.GetType())) {
                        case TypeCode.DateTime:
                            sb.Append("'");
                            sb.Append(((DateTime)c.Value).ToString("o"));
                            sb.Append("'");
                            break;
                        case TypeCode.Object:
                            if (c.Value is DateTimeOffset)
                            {
                                sb.Append("'");
                                sb.Append(((DateTimeOffset)c.Value).ToString("o"));
                                sb.Append("'");
                            }
                            else
                            {
                                throw new NotSupportedException(string.Format("The constant for '{0}' is not supported by SQL.", c.Value));
                            }
                            break;
                        default:
                            sb.Append("'");
                            sb.Append(c.Value.ToString().Replace("'", "''"));
                            sb.Append("'");
                            break;
                    }
                }
            }
            return c;
        }

        protected override Expression VisitMemberAccess(MemberExpression m) {
            if (m.Expression != null && m.Expression.NodeType == ExpressionType.Parameter) {
                if (GetMemberType(m) == typeof(Boolean)) {
                    sb.Append(m.Member.Name.ToLower() + " = '1'");
                }
                else {
                    sb.Append(m.Member.Name.ToLower());
                }
                return m;
            }
            throw new NotSupportedException(string.Format("The member '{0}' is not supported by SQL.", m.Member.Name));
        }

        private string GetTableName(MethodCallExpression m) {
            IQueryable q = ((ConstantExpression)m.Arguments[0]).Value as IQueryable;
            return GetTableName(q.ElementType);
        }

        private string GetTableName(Type tableType) {
            AttributeMappingSource mappingSource = new AttributeMappingSource();
            MetaModel mapping = mappingSource.GetModel(tableType);
            MetaTable table = mapping.GetTable(tableType);
            return table.TableName;
        }

        private bool IsPrimaryKey(MemberExpression m) {
            AttributeMappingSource mappingSource = new AttributeMappingSource();
            MetaModel mapping = mappingSource.GetModel(m.Member.DeclaringType);
            MetaTable table = mapping.GetTable(m.Member.DeclaringType);
            foreach (MetaDataMember dataMember in table.RowType.PersistentDataMembers) {
                if (dataMember.Name == m.Member.Name) {
                    return dataMember.IsPrimaryKey;
                }
            }
            return false;
        }

        private Type GetMemberType(MemberExpression m) {
            AttributeMappingSource mappingSource = new AttributeMappingSource();
            MetaModel mapping = mappingSource.GetModel(m.Member.DeclaringType);
            MetaTable table = mapping.GetTable(m.Member.DeclaringType);
            foreach (MetaDataMember dataMember in table.RowType.PersistentDataMembers) {
                if (dataMember.Name == m.Member.Name) {
                    return dataMember.Type;
                }
            }
            return null;
        }
    }
}
