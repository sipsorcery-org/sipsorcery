
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Represents one piece of content in a conversation item (e.g., text, audio, reference).
/// </summary>
public class RealtimeConversationContent
{
    /// <summary>
    /// The type of the content block (input_text, input_audio, item_reference, text).
    /// </summary>
    [JsonPropertyName("type")]
    public required RealtimeConversationContentTypeEnum Type { get; set; }

    /// <summary>
    /// The text content (for input_text or text types).
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// ID of a referenced item (for item_reference content).
    /// </summary>
    [JsonPropertyName("id")]
    public string? ID { get; set; }

    /// <summary>
    /// Base64-encoded audio bytes (for input_audio content).
    /// </summary>
    [JsonPropertyName("audio")]
    public string? Audio { get; set; }

    /// <summary>
    /// Transcript of the audio (for input_audio content).
    /// </summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; set; }
}
