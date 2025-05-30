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
using System.Buffers;
using System.Collections;
using System.Collections.Frozen;
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
        private static readonly FrozenDictionary<char, char> EscapeMap = new Dictionary<char, char>
        {
            ['"'] = '"',
            ['\\'] = '\\',
            ['\n'] = 'n',
            ['\r'] = 'r',
            ['\t'] = 't',
            ['\b'] = 'b',
            ['\f'] = 'f'
        }.ToFrozenDictionary();

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

        static void AppendValue(ref ValueStringBuilder builder, object item)
        {
            if (item == null)
            {
                builder.Append("null");
                return;
            }

            var type = item.GetType();

            if (type.IsEnum)
            {
                builder.Append('"');
                builder.Append(item.ToString());
                builder.Append('"');
                return;
            }

            var typeCode = Type.GetTypeCode(type);

            switch (typeCode)
            {

                case TypeCode.String:
                    {
                        builder.Append('"');
                        var str = ((string)item).AsSpan();
                        for (var i = 0; i < str.Length; i++)
                        {
                            AppendEscapedChar(ref builder, str[i]);
                        }
                        builder.Append('"');
                        return;
                    }

                case TypeCode.Char:
                    {
                        builder.Append('"');
                        AppendEscapedChar(ref builder, (char)item);
                        builder.Append('"');
                        return;

                    }

                case TypeCode.Boolean:
                    {
                        builder.Append((bool)item ? "true" : "false");
                        return;
                    }

                case TypeCode.Single:
                    {
                        builder.Append((float)item, provider: System.Globalization.CultureInfo.InvariantCulture);
                        return;
                    }

                case TypeCode.Double:
                    {
                        builder.Append((double)item, provider: System.Globalization.CultureInfo.InvariantCulture);
                        return;
                    }

                case TypeCode.Decimal:
                    {
                        builder.Append((decimal)item, provider: System.Globalization.CultureInfo.InvariantCulture);
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
                        builder.Append(item.ToString());
                        return;
                    }

                case TypeCode.DBNull:
                case TypeCode.Empty:
                    {
                        builder.Append("null");
                        return;
                    }
            }

            static void AppendEscapedChar(ref ValueStringBuilder builder, char ch)
            {
                if (ch is >= ' ' and not '"' and not '\\')
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append('\\');
                    if (EscapeMap.TryGetValue(ch, out var escapeChar))
                    {
                        builder.Append(escapeChar);
                    }
                    else
                    {
                        builder.Append('u');
                        builder.Append(((uint)ch).ToString("X4"));
                    }
                }
            }

            if (item is IList list)
            {
                builder.Append('[');
                var isFirst = true;
                for (var i = 0; i < list.Count; i++)
                {
                    if (!isFirst)
                    {
                        builder.Append(',');
                    }
                    else
                    {
                        isFirst = false;
                    }
                    AppendValue(ref builder, list[i]);
                }
                builder.Append(']');
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var keyType = type.GetGenericArguments()[0];
                if (keyType != typeof(string))
                {
                    builder.Append("{}");
                    return;
                }

                var dict = item as IDictionary;
                builder.Append('{');
                var isFirst = true;
                foreach (var key in dict.Keys)
                {
                    if (!isFirst)
                    {
                        builder.Append(',');
                    }
                    else
                    {
                        isFirst = false;
                    }
                    builder.Append('\"');
                    builder.Append((string)key);
                    builder.Append("\":");
                    AppendValue(ref builder, dict[key]);
                }
                builder.Append('}');
            }
            else
            {
                builder.Append('{');
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
                            builder.Append(',');
                        }
                        else
                        {
                            isFirst = false;
                        }
                        builder.Append('\"');
                        builder.Append(GetMemberName(field));
                        builder.Append("\":");
                        AppendValue(ref builder, value);
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
                            builder.Append(',');
                        }
                        else
                        {
                            isFirst = false;
                        }
                        builder.Append('\"');
                        builder.Append(GetMemberName(prop));
                        builder.Append("\":");
                        AppendValue(ref builder, value);
                    }
                }

                builder.Append('}');
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
