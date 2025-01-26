//-----------------------------------------------------------------------------
// Filename: BooleanStringConverter.cs
//
// Description: Converter for deserialsing strings to booleans. This converter
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

using System.Text.Json.Serialization;
using System.Text.Json;
using System;

namespace demo;

public class BooleanAsStringConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (bool.TryParse(stringValue, out bool boolValue))
            {
                return boolValue;
            }
            throw new JsonException($"Unable to convert \"{stringValue}\" to boolean.");
        }
        else if (reader.TokenType == JsonTokenType.True)
        {
            return true;
        }
        else if (reader.TokenType == JsonTokenType.False)
        {
            return false;
        }
        throw new JsonException($"Unexpected token parsing boolean. Expected String, True, or False, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
         writer.WriteBooleanValue(value);
    }
}
