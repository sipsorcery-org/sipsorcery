using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Tool definition for function calling.
/// </summary>
public class RealtimeResponseTool
{
    /// <summary>
    /// The tool type (currently only "function").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; } = "function";

    /// <summary>
    /// The name of the function.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Description of the function's purpose and usage guidance.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Parameters for the function, defined as a JSON schema object.
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }
}