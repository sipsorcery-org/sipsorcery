using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public class JsonStringToElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Expecting a JSON string containing JSON content
        if (reader.TokenType == JsonTokenType.String)
        {
            string? jsonString = reader.GetString();
            if (!string.IsNullOrEmpty(jsonString))
            {
                using var doc = JsonDocument.Parse(jsonString);
                return doc.RootElement.Clone();
            }
        }
        // Fallback empty object
        using var emptyDoc = JsonDocument.Parse("{}");
        return emptyDoc.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        // Serialize the JsonElement back to a JSON string
        writer.WriteStringValue(value.GetRawText());
    }
}