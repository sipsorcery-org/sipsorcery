using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Data.Linq.Mapping;
using System.Reflection;
using System.Text;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;

namespace SIPSorcery.Persistence {

    public delegate void SetterDelegate(object instance, string propertyName, object value);

    public class ObjectReader<T> : IEnumerable<T>, IEnumerable where T : class, ISIPAsset, new() {

        Enumerator enumerator;
        private SelectResult m_selectResult;
        private SetterDelegate m_setter;

        public ObjectReader(SelectResult selectResult) {
            m_selectResult = selectResult;
        }

        public ObjectReader(SelectResult selectResult, SetterDelegate setter) {
            m_selectResult = selectResult;
            m_setter = setter;
            this.enumerator = new Enumerator(selectResult, setter);
        }

        public IEnumerator<T> GetEnumerator() {
            Enumerator e = this.enumerator;

            if (e == null) {
                throw new InvalidOperationException("Cannot enumerate more than once");
            }
            this.enumerator = null;
            return e;
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetEnumerator();
        }

        public T First() {
            T instance = new T();
            instance.Id = m_selectResult.Item[0].Name;
            foreach (Amazon.SimpleDB.Model.Attribute itemAttribute in m_selectResult.Item[0].Attribute) {
                m_setter(instance, itemAttribute.Name, itemAttribute.Value);
            }
            return instance;
        }

        class Enumerator : IEnumerator<T>, IEnumerator, IDisposable {

            SelectResult m_selectResult;
            private SetterDelegate m_setter;
            int m_selectIndex;
            T current;

            internal Enumerator(SelectResult selectResult, SetterDelegate setter) {
                m_selectResult = selectResult;
                m_setter = setter;
            }

            public T Current {
                get { return this.current; }
            }

            object IEnumerator.Current {
                get { return this.current; }
            }

            public bool MoveNext() {

                if (m_selectIndex < m_selectResult.Item.Count) {

                    T instance = new T();
                    instance.Id = m_selectResult.Item[m_selectIndex].Name;
                    foreach (Amazon.SimpleDB.Model.Attribute itemAttribute in m_selectResult.Item[m_selectIndex++].Attribute) {
                        m_setter(instance, itemAttribute.Name, itemAttribute.Value);
                    }

                    this.current = instance;
                    return true;
                }
                return false;
            }

            public void Reset() {  }

            public void Dispose() {  }
        }
    }
}
