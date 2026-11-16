using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// <b>WebRTC Only:</b> Emitted when the server begins streaming audio to the client.
///
/// This event is sent after an audio content part has been added to the response.
/// It allows clients to track the beginning of audio playback.
/// </summary>
public class RealtimeServerEventOutputAudioBufferStarted : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "output_audio_buffer.started".
    /// </summary>
    public const string TypeName = "output_audio_buffer.started";

    /// <summary>
    /// Overrides the event type property.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The unique ID of the response that produced the audio.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseID { get; set; }
}
