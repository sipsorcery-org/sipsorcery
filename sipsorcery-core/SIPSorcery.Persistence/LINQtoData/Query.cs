using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace SIPSorcery.Persistence {

    public class Query<T> : IQueryable<T>, IQueryable, IEnumerable<T>, IEnumerable, IOrderedQueryable<T>, IOrderedQueryable {

        QueryProvider provider;

        Expression expression;
        Expression IQueryable.Expression {
            get { return this.expression; }
        }

        Type IQueryable.ElementType {
            get { return typeof(T); }
        }

        IQueryProvider IQueryable.Provider {
            get { return this.provider; }
        }

        public Query(QueryProvider provider) {
            if (provider == null) {
                throw new ArgumentNullException("provider");
            }

            this.provider = provider;
            this.expression = Expression.Constant(this);
        }

        public Query(QueryProvider provider, Expression expression) {
            if (provider == null) {
                throw new ArgumentNullException("provider");
            }

            if (expression == null) {
                throw new ArgumentNullException("expression");
            }

            if (!typeof(IQueryable<T>).IsAssignableFrom(expression.Type)) {
                throw new ArgumentOutOfRangeException("expression");
            }

            this.provider = provider;
            this.expression = expression;
        }

        public IEnumerator<T> GetEnumerator() {
            return ((IEnumerable<T>)this.provider.Execute(this.expression)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable)this.provider.Execute(this.expression)).GetEnumerator();
        }

        public override string ToString() {
            return this.provider.GetQueryText(this.expression);
        }
    }
}
