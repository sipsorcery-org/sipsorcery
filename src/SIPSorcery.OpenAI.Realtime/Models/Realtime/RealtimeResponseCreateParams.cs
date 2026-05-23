using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Parameters for creating a new Realtime response. These override the session configuration
/// for this response only and control how the model responds.
/// </summary>
public class RealtimeResponseCreateParams
{
    /// <summary>
    /// The modalities the model can respond with. To disable audio, set this to ["text"].
    /// </summary>
    [JsonPropertyName("modalities")]
    public List<RealtimeModalityEnum>? Modalities { get; set; }

    /// <summary>
    /// System instructions that guide the model’s behavior and response style.
    /// </summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    /// <summary>
    /// Nested audio configuration (GA shape). Populated automatically when
    /// <see cref="Voice"/> is assigned; callers can also build it directly
    /// for finer-grained control.
    /// </summary>
    [JsonPropertyName("audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RealtimeAudio? Audio { get; set; }

    /// <summary>
    /// The voice the model will use to respond. Cannot change once audio
    /// response has begun. Public API retained for backward compatibility:
    /// writes are routed into <see cref="Audio"/>.<see cref="RealtimeAudio.Output"/>.<see cref="RealtimeAudioOutput.Voice"/>
    /// (the GA location) and JSON serialisation skips this flat property.
    /// </summary>
    [JsonIgnore]
    public RealtimeVoicesEnum? Voice
    {
        get => Audio?.Output?.Voice;
        set
        {
            if (value == null)
            {
                if (Audio?.Output != null)
                {
                    Audio.Output.Voice = null;
                }
                return;
            }
            Audio ??= new RealtimeAudio();
            Audio.Output ??= new RealtimeAudioOutput();
            Audio.Output.Voice = value;
        }
    }

    /// <summary>
    /// Audio format to use for responses.
    /// </summary>
    [JsonPropertyName("output_audio_format")]
    public RealtimeAudioFormatEnum? OutputAudioFormat { get; set; }

    /// <summary>
    /// List of tools (e.g. functions) available to the model.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<RealtimeResponseTool>? Tools { get; set; }

    /// <summary>
    /// Tool selection behavior: auto, none, required, or specify a function.
    /// </summary>
    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; set; }

    /// <summary>
    /// Sampling temperature for randomness control (range: 0.6–1.2).
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Maximum number of output tokens. Can be a number or the string "inf".
    /// </summary>
    [JsonPropertyName("max_response_output_tokens")]
    public string? MaxResponseOutputTokens { get; set; }

    /// <summary>
    /// Controls which conversation the response is added to: "auto", "none", or conversation ID.
    /// </summary>
    [JsonPropertyName("conversation")]
    public string? Conversation { get; set; }

    /// <summary>
    /// Metadata associated with the response request.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Metadata? Metadata { get; set; }

    /// <summary>
    /// Optional override input items to establish a custom context instead of using conversation history.
    /// </summary>
    [JsonPropertyName("input")]
    public List<RealtimeConversationItemWithReference>? Input { get; set; }
}
