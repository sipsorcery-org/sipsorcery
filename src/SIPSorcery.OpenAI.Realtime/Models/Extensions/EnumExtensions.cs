using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public static class EnumExtensions
{
    public static string ToEnumString(this Enum enumValue)
    {
        var member = enumValue.GetType().GetMember(enumValue.ToString());
        if (member.Length > 0)
        {
            var attr = member[0].GetCustomAttribute<EnumMemberAttribute>();
            if (attr != null)
            {
                return attr.Value ?? enumValue.ToString();
            }
        }
        return enumValue.ToString();
    }
}