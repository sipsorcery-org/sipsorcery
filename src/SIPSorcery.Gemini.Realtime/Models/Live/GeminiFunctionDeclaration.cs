using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Describes a local function the model may call, analogous to an OpenAI Realtime tool.
/// </summary>
public class GeminiFunctionDeclaration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// JSON Schema object describing the function's parameters.
    /// </summary>
    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }
}
