using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiTranscription
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Incremental model output — audio/text parts, transcripts and turn/interruption state. This
/// is the most frequent message type on the wire while the model is responding.
/// </summary>
public class GeminiServerEventContent : GeminiServerMessage
{
    public const string JsonKey = "serverContent";

    public GeminiServerEventContent() : base(GeminiServerMessageKind.ServerContent)
    {
    }

    [JsonPropertyName("modelTurn")]
    public GeminiContent? ModelTurn { get; set; }

    /// <summary>
    /// True on the final message of a model turn.
    /// </summary>
    [JsonPropertyName("turnComplete")]
    public bool? TurnComplete { get; set; }

    /// <summary>
    /// True when the model's turn was interrupted by user input (barge-in). Any audio already
    /// queued for playback by the caller should be discarded.
    /// </summary>
    [JsonPropertyName("interrupted")]
    public bool? Interrupted { get; set; }

    [JsonPropertyName("generationComplete")]
    public bool? GenerationComplete { get; set; }

    /// <summary>
    /// Transcript of the caller's spoken audio, present when
    /// <see cref="GeminiSetup.InputAudioTranscription"/> was enabled.
    /// </summary>
    [JsonPropertyName("inputTranscription")]
    public GeminiTranscription? InputTranscription { get; set; }

    /// <summary>
    /// Transcript of the model's spoken audio, present when
    /// <see cref="GeminiSetup.OutputAudioTranscription"/> was enabled.
    /// </summary>
    [JsonPropertyName("outputTranscription")]
    public GeminiTranscription? OutputTranscription { get; set; }
}
