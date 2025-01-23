// https://platform.openai.com/docs/api-reference/realtime-server-events/response/function_call_arguments/done

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseFunctionCallArgumentsDone : OpenAIServerEventBase
{
    public const string TypeName = "response.function_call_arguments.done";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response_id")]
    public string? ResponseID { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemID { get; set; }

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallID { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    [JsonConverter(typeof(JsonStringToElementConverter))]
    public JsonElement Arguments { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }

    public string ArgumentsToString()
    {
        string argumentsStr = string.Empty;

        if (Arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in Arguments.EnumerateObject())
            {
                argumentsStr += $"Argument Name: {property.Name}, Value: {property.Value}\n";
            }
        }

        return argumentsStr;
    }
}

public static class JsonElementExtensions
{
    /// <summary>
    /// Attempts to retrieve the value of a named property from a JsonElement object.
    /// Returns the property value as a string if found; otherwise, returns null.
    /// </summary>
    public static string? GetNamedArgumentValue(this JsonElement element, string propertyName)
    {
        // Ensure the JsonElement is an object.
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Try to get the property.
        if (element.TryGetProperty(propertyName, out JsonElement propertyValue))
        {
            // If the property is a JSON string, return its string value.
            if (propertyValue.ValueKind == JsonValueKind.String)
            {
                return propertyValue.GetString();
            }
            // For non-string types, return the raw JSON text.
            return propertyValue.GetRawText();
        }

        // If property not found, return null.
        return null;
    }
}

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
