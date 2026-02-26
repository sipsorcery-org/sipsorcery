using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// <b>WebRTC Only:</b> Emitted when the output audio buffer has been completely drained on the server,
/// and no more audio is forthcoming. This event follows the `response.done` event and
/// signifies the end of audio streaming for the current response.
/// </summary>
public class RealtimeServerEventOutputAudioBufferStopped : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "output_audio_buffer.stopped".
    /// </summary>
    public const string TypeName = "output_audio_buffer.stopped";

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
