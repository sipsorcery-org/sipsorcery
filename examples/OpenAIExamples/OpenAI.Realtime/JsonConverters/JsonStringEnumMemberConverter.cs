//-----------------------------------------------------------------------------
// Filename: JsonStringEnumMemberConverter.cs
//
// Description: Converter for deserialsing strings to enums. Allows both the enum
// value and the member attribute to be used as the string value. This converter
// is only required for System.Text.Json as Newtonsoft already supports this.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Jun 2024  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// MIT.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

#nullable enable

public class JsonStringEnumMemberConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        Type? underlyingType = Nullable.GetUnderlyingType(typeToConvert);
        return typeToConvert.IsEnum || (underlyingType != null && underlyingType.IsEnum);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type? underlyingType = Nullable.GetUnderlyingType(typeToConvert);

        if (underlyingType != null && underlyingType.IsEnum)
        {
            var converterType = typeof(NullableEnumMemberConverter<>).MakeGenericType(underlyingType);
            var converter = (JsonConverter?)Activator.CreateInstance(converterType);
            if (converter == null)
            {
                throw new InvalidOperationException($"Unable to create converter for {typeToConvert}");
            }
            return converter;
        }
        else if (typeToConvert.IsEnum)
        {
            var converterType = typeof(EnumMemberConverter<>).MakeGenericType(typeToConvert);
            var converter = (JsonConverter?)Activator.CreateInstance(converterType);
            if (converter == null)
            {
                throw new InvalidOperationException($"Unable to create converter for {typeToConvert}");
            }
            return converter;
        }
        throw new InvalidOperationException($"Unable to create converter for {typeToConvert}");
    }

    private class EnumMemberConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var enumText = reader.GetString();

                if (string.IsNullOrEmpty(enumText))
                {
                    throw new JsonException($"Unable to convert empty string to {typeof(T)}.");
                }

                // Check for EnumMember attribute values
                foreach (var field in typeof(T).GetFields())
                {
                    if (field.GetCustomAttribute<EnumMemberAttribute>() is EnumMemberAttribute attribute)
                    {
                        if (string.Equals(attribute.Value, enumText, StringComparison.OrdinalIgnoreCase))
                        {
                            return (T)(field.GetValue(null) ?? default(T));
                        }
                    }
                    else if (string.Equals(field.Name, enumText, StringComparison.OrdinalIgnoreCase))
                    {
                        return (T)(field.GetValue(null) ?? default(T));
                    }
                }

                // If not found, try parsing directly
                if (Enum.TryParse(enumText, true, out T result))
                {
                    return result;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out int intValue))
                {
                    return (T)(object)intValue;
                }
            }

            throw new JsonException($"Unable to convert \"{reader.GetString()}\" to enum \"{typeof(T)}\".");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var field = typeof(T).GetField(value.ToString());
            if (field?.GetCustomAttribute<EnumMemberAttribute>() is EnumMemberAttribute attribute)
            {
                writer.WriteStringValue(attribute.Value);
            }
            else
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }

    private class NullableEnumMemberConverter<T> : JsonConverter<T?> where T : struct, Enum
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var enumText = reader.GetString();

                if (string.IsNullOrEmpty(enumText))
                {
                    return null;
                }

                // Check for EnumMember attribute values
                foreach (var field in typeof(T).GetFields())
                {
                    if (field.GetCustomAttribute<EnumMemberAttribute>() is EnumMemberAttribute attribute)
                    {
                        if (string.Equals(attribute.Value, enumText, StringComparison.OrdinalIgnoreCase))
                        {
                            return (T)(field.GetValue(null) ?? default(T));
                        }
                    }
                    else if (string.Equals(field.Name, enumText, StringComparison.OrdinalIgnoreCase))
                    {
                        return (T)(field.GetValue(null) ?? default(T));
                    }
                }

                // If not found, try parsing directly
                if (Enum.TryParse(enumText, true, out T result))
                {
                    return result;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out int intValue))
                {
                    return (T)(object)intValue;
                }
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                var field = typeof(T).GetField(value.Value.ToString());
                if (field?.GetCustomAttribute<EnumMemberAttribute>() is EnumMemberAttribute attribute)
                {
                    writer.WriteStringValue(attribute.Value);
                }
                else
                {
                    writer.WriteStringValue(value.Value.ToString());
                }
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
