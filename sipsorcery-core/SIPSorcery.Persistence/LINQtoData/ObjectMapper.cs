using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace SIPSorcery.Persistence {
    
    public class ObjectMapper<T> {

        public string TableName;
        private Dictionary<MetaDataMember, Func<object, object>> m_getterTableMap;
        private Dictionary<MetaDataMember, Action<object, object>> m_setterTableMap;

        public ObjectMapper() {
            TableName = GetTableName(typeof(T));
            m_getterTableMap = GetGetterTableMap(typeof(T));
            m_setterTableMap = GetSetterTableMap(typeof(T));
        }

        public object GetValue(T instance, string propertyName) {
            foreach (KeyValuePair<MetaDataMember, Func<object, object>> getter in m_getterTableMap) {
                if (getter.Key.Name.ToLower() == propertyName.ToLower()) {
                    return getter.Value(instance);
                }
            }
            return null;
        }

        public Dictionary<MetaDataMember, object> GetAllValues(T instance) {
            Dictionary<MetaDataMember, object> allPropertyValues = new Dictionary<MetaDataMember, object>();
            foreach (KeyValuePair<MetaDataMember, Func<object, object>> getter in m_getterTableMap) {
                allPropertyValues.Add(getter.Key, getter.Value(instance));
            }
            return allPropertyValues;
        }

        public MetaDataMember GetMember(string propertyName) {
            foreach (KeyValuePair<MetaDataMember, Func<object, object>> getter in m_getterTableMap) {
                if (getter.Key.Name.ToLower() == propertyName.ToLower()) {
                    return getter.Key;
                }
            }
            return null;
        }

        public void SetValue(object instance, string propertyName, object value) {
            SetValue((T)instance, propertyName, value);
        }

        public void SetValue(T instance, string propertyName, object value) {
            if (value != null) {
                foreach (KeyValuePair<MetaDataMember, Action<object, object>> setter in m_setterTableMap) {
                    if (setter.Key.MappedName.ToLower() == propertyName.ToLower()) {
                        if (setter.Key.Type == typeof(DateTime) || setter.Key.Type == typeof(Nullable<DateTime>)) {
                            setter.Value(instance, DateTime.Parse(value as string));
                        }
                        else {
                            setter.Value(instance, Convert.ChangeType(value, setter.Key.Type));
                            break;
                        }
                    }
                }
            }
        }

        private string GetTableName(Type tableType) {
            AttributeMappingSource mappingSource = new AttributeMappingSource();
            MetaModel mapping = mappingSource.GetModel(tableType);
            MetaTable table = mapping.GetTable(tableType);
            return table.TableName;
        }

        private Dictionary<MetaDataMember, Func<object, object>> GetGetterTableMap(Type tableType) {
            AttributeMappingSource mappingSource = new AttributeMappingSource();
            MetaModel mapping = mappingSource.GetModel(tableType);
            MetaTable table = mapping.GetTable(tableType);

            Dictionary<MetaDataMember, Func<object, object>> mappedTable = new Dictionary<MetaDataMember, Func<object, object>>();

            foreach (MetaDataMember dataMember in table.RowType.PersistentDataMembers) {
                MemberInfo memberInfo = dataMember.Member;
                Expression<Func<object, object>> getter = null;
                if (memberInfo is FieldInfo) {
                    getter = (Expression<Func<object, object>>)(o => ((FieldInfo)memberInfo).GetValue(o));
                }
                else if (memberInfo is PropertyInfo) {
                    getter = (Expression<Func<object, object>>)(o => ((PropertyInfo)memberInfo).GetGetMethod().Invoke(o, new object[0]));
                }
                else {
                    throw new ApplicationException("GetTableMap could not determine lambda expression for " + memberInfo.GetType() + ".");
                }
                mappedTable.Add(dataMember, getter.Compile());
            }

            return mappedTable;
        }

        private Dictionary<MetaDataMember, Action<object, object>> GetSetterTableMap(Type tableType) {
            AttributeMappingSource mappingSource = new AttributeMappingSource();
            MetaModel mapping = mappingSource.GetModel(tableType);
            MetaTable table = mapping.GetTable(tableType);

            Dictionary<MetaDataMember, Action<object, object>> mappedTable = new Dictionary<MetaDataMember, Action<object, object>>();

            foreach (MetaDataMember dataMember in table.RowType.PersistentDataMembers) {
                MemberInfo memberInfo = dataMember.Member;
                Expression<Action<object, object>> getter = null;
                if (memberInfo is FieldInfo) {
                    getter = (Expression<Action<object, object>>)((o,v) => ((FieldInfo)memberInfo).SetValue(o, v));
                }
                else if (memberInfo is PropertyInfo) {
                    getter = (Expression<Action<object, object>>)((o, v) => ((PropertyInfo)memberInfo).GetSetMethod().Invoke(o, new[] { v }));
                }
                else {
                    throw new ApplicationException("GetTableMap could not determine lambda expression for " + memberInfo.GetType() + ".");
                }
                mappedTable.Add(dataMember, getter.Compile());
            }

            return mappedTable;
        }
    }
}
