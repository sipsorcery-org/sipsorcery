using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Raw media bytes, base64 encoded, tagged with a MIME type. Used for both realtime audio/video
/// input (e.g. "audio/pcm;rate=16000") and inline audio/image data returned in model output parts.
/// </summary>
public class GeminiBlob
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }
}
