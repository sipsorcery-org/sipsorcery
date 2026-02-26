using System.Text.Json;

namespace SIPSorcery.OpenAI.Realtime.Models;

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
