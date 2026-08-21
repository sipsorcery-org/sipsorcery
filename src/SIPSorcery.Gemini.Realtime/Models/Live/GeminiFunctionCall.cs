using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// A function call requested by the model, delivered via <see cref="GeminiServerEventToolCall"/>.
/// </summary>
public class GeminiFunctionCall
{
    /// <summary>
    /// Identifier for this call. Echo it back in the matching <see cref="GeminiFunctionResponse"/>.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Arguments for the call as a JSON object, shaped by the corresponding
    /// <see cref="GeminiFunctionDeclaration.Parameters"/> schema.
    /// </summary>
    [JsonPropertyName("args")]
    public JsonElement? Args { get; set; }
}
