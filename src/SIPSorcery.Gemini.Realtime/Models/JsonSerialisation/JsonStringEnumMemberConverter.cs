using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Converts between strings and enums, accepting either the enum member name or the value of its
/// [EnumMember] attribute. Only needed for System.Text.Json; Newtonsoft supports this out of the box.
/// </summary>
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

            // Deliberately not reading the value back for the message: Utf8JsonReader.GetString()
            // throws on anything that isn't a string or null, which would replace this diagnostic
            // with a confusing InvalidOperationException from inside the converter.
            throw new JsonException(
                reader.TokenType == JsonTokenType.String
                    ? $"Unable to convert \"{reader.GetString()}\" to enum \"{typeof(T)}\"."
                    : $"Unable to convert a JSON {reader.TokenType} token to enum \"{typeof(T)}\".");
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
