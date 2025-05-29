//-----------------------------------------------------------------------------
// Filename: JSONWriter.cs
//
// Description: A very simple JSON serialiser. Intended for cases where a fully
// fledged JSON serialiser is not supported, for example issues were encountered
// with the Unity game runtime with Newtonsoft and System.Text implementations.
//
// Based on https://github.com/zanders3/json/blob/master/src/JSONWriter.cs.
//
// History:
// 05 Oct 2020	Aaron Clauson	Imported.
//
// License: 
// MIT, see https://github.com/zanders3/json/blob/master/LICENSE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using SIPSorcery.Sys;

namespace TinyJson
{
    //Really simple JSON writer
    //- Outputs JSON structures from an object
    //- Really simple API (new List<int> { 1, 2, 3 }).ToJson() == "[1,2,3]"
    //- Will only output public fields and property getters on objects
    public static class JSONWriter
    {
        public static string ToJson(this object item)
        {
            var builder = new ValueStringBuilder();

            try
            {
                AppendValue(ref builder, item);

                return builder.ToString();
            }
            finally
            {
                builder.Dispose();
            }
        }

        static void AppendValue(ref ValueStringBuilder stringBuilder, object item)
        {
            if (item == null)
            {
                stringBuilder.Append("null");
                return;
            }

            var type = item.GetType();
            var typeCode = Type.GetTypeCode(type);

            switch (typeCode)
            {
                case TypeCode.String:
                case TypeCode.Char:
                    {
                        stringBuilder.Append('"');
                        var str = item.ToString();
                        for (var i = 0; i < str.Length; ++i)
                        {
                            if (str[i] < ' ' || str[i] == '"' || str[i] == '\\')
                            {
                                stringBuilder.Append('\\');
                                var j = "\"\\\n\r\t\b\f".IndexOf(str[i]);
                                if (j >= 0)
                                {
                                    stringBuilder.Append("\"\\nrtbf"[j]);
                                }
                                else
                                {
                                    stringBuilder.Append("u");
                                    stringBuilder.Append((uint)str[i], "X4");
                                }
                            }
                            else
                            {
                                stringBuilder.Append(str[i]);
                            }
                        }
                        stringBuilder.Append('"');
                        return;
                    }

                case TypeCode.Boolean:
                    {
                        stringBuilder.Append((bool)item ? "true" : "false");
                        return;
                    }

                case TypeCode.Single:
                    {
                        stringBuilder.Append((float)item, provider: System.Globalization.CultureInfo.InvariantCulture);
                        return;
                    }

                case TypeCode.Double:
                    {
                        stringBuilder.Append((double)item, provider: System.Globalization.CultureInfo.InvariantCulture);
                        return;
                    }

                case TypeCode.Decimal:
                    {
                        stringBuilder.Append((decimal)item, provider: System.Globalization.CultureInfo.InvariantCulture);
                        return;
                    }

                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    {
                        stringBuilder.Append(item.ToString());
                        return;
                    }

                case TypeCode.DBNull:
                case TypeCode.Empty:
                    {
                        stringBuilder.Append("null");
                        return;
                    }
            }

            if (type.IsEnum)
            {
                stringBuilder.Append('"');
                stringBuilder.Append(item.ToString());
                stringBuilder.Append('"');
            }
            else if (item is IList list)
            {
                stringBuilder.Append('[');
                var isFirst = true;
                for (var i = 0; i < list.Count; i++)
                {
                    if (!isFirst)
                    {
                        stringBuilder.Append(',');
                    }
                    else
                    {
                        isFirst = false;
                    }
                    AppendValue(ref stringBuilder, list[i]);
                }
                stringBuilder.Append(']');
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var keyType = type.GetGenericArguments()[0];
                if (keyType != typeof(string))
                {
                    stringBuilder.Append("{}");
                    return;
                }

                var dict = item as IDictionary;
                stringBuilder.Append('{');
                var isFirst = true;
                foreach (var key in dict.Keys)
                {
                    if (!isFirst)
                    {
                        stringBuilder.Append(',');
                    }
                    else
                    {
                        isFirst = false;
                    }
                    stringBuilder.Append('\"');
                    stringBuilder.Append((string)key);
                    stringBuilder.Append("\":");
                    AppendValue(ref stringBuilder, dict[key]);
                }
                stringBuilder.Append('}');
            }
            else
            {
                stringBuilder.Append('{');
                var isFirst = true;

                var fieldInfos = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                foreach (var field in fieldInfos)
                {
                    if (field.IsDefined(typeof(IgnoreDataMemberAttribute), true))
                    {
                        continue;
                    }

                    var value = field.GetValue(item);
                    if (value != null)
                    {
                        if (!isFirst)
                        {
                            stringBuilder.Append(',');
                        }
                        else
                        {
                            isFirst = false;
                        }
                        stringBuilder.Append('\"');
                        stringBuilder.Append(GetMemberName(field));
                        stringBuilder.Append("\":");
                        AppendValue(ref stringBuilder, value);
                    }
                }

                var propertyInfos = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
                foreach (var prop in propertyInfos)
                {
                    if (!prop.CanRead || prop.IsDefined(typeof(IgnoreDataMemberAttribute), true))
                    {
                        continue;
                    }

                    var value = prop.GetValue(item, null);
                    if (value != null)
                    {
                        if (!isFirst)
                        {
                            stringBuilder.Append(',');
                        }
                        else
                        {
                            isFirst = false;
                        }
                        stringBuilder.Append('\"');
                        stringBuilder.Append(GetMemberName(prop));
                        stringBuilder.Append("\":");
                        AppendValue(ref stringBuilder, value);
                    }
                }

                stringBuilder.Append('}');
            }
        }

        static string GetMemberName(MemberInfo member)
        {
            if (Attribute.GetCustomAttribute(member, typeof(DataMemberAttribute), true) is DataMemberAttribute attr &&
                !string.IsNullOrEmpty(attr.Name))
            {
                return attr.Name;
            }

            return member.Name;
        }
    }
}
