using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// The application's result for a previously received <see cref="GeminiFunctionCall"/>, sent
/// back to Gemini in a <see cref="GeminiToolResponseMessage"/>.
/// </summary>
public class GeminiFunctionResponse
{
    /// <summary>
    /// Must match the <see cref="GeminiFunctionCall.Id"/> of the call being answered.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The function's result as a JSON object.
    /// </summary>
    [JsonPropertyName("response")]
    public JsonElement? Response { get; set; }
}
