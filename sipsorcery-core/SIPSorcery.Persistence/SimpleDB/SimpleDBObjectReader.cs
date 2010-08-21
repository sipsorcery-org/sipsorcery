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
using SIPSorcery.Sys;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;
using log4net;

namespace SIPSorcery.Persistence {

    public class SimpleDBObjectReader<T> : IEnumerable<T>, IEnumerable where T : class, ISIPAsset, new() {

        private static ILog logger = AppState.logger;

        Enumerator enumerator;
        private SelectResult m_selectResult;
        private SetterDelegate m_setter;

        public SimpleDBObjectReader(SelectResult selectResult) {
            m_selectResult = selectResult;
        }

        public SimpleDBObjectReader(SelectResult selectResult, SetterDelegate setter) {
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
            return Load(m_selectResult.Item[0]);
        }

        public static T Load(Item selectResultItem) {
            try {
                T instance = new T();

                DataTable table = instance.GetTable();
                DataRow row = table.NewRow();
                row["id"] = new Guid(selectResultItem.Name);
                //instance.Id = new Guid(m_selectResult.Item[0].Name);
                foreach (Amazon.SimpleDB.Model.Attribute itemAttribute in selectResultItem.Attribute) {
                    //logger.Debug(" " + itemAttribute.Name + ": " + itemAttribute.Value + ".");
                    row[itemAttribute.Name] = itemAttribute.Value;
                    //m_setter(instance, itemAttribute.Name, itemAttribute.Value);
                }
                instance.Load(row);
                return instance;
            }
            catch (Exception excp) {
                logger.Error("Exception SimpleDBObjectReader Load (" + typeof(T) + "). " + excp.Message);
                throw;
            }
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

                    /*T instance = new T();
                    instance.Id = new Guid(m_selectResult.Item[m_selectIndex].Name);
                    foreach (Amazon.SimpleDB.Model.Attribute itemAttribute in m_selectResult.Item[m_selectIndex].Attribute) {
                        //logger.Debug(" " + itemAttribute.Name + ": " + itemAttribute.Value + ".");
                        m_setter(instance, itemAttribute.Name, itemAttribute.Value);
                    }

                    this.current = instance;*/

                    this.current = Load(m_selectResult.Item[m_selectIndex]);
                    m_selectIndex++;
                    return true;
                }
                return false;
            }

            public void Reset() {  }

            public void Dispose() {  }
        }
    }
}
