using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Represents the log probability of a token used in audio transcription.
/// Includes the token, its associated log probability, and the underlying byte representation.
/// </summary>
public class RealtimeLogProbProperties
{
    /// <summary>
    /// The token that was used to generate the log probability.
    /// </summary>
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    /// <summary>
    /// The log probability value of the token.
    /// </summary>
    [JsonPropertyName("logprob")]
    public required double LogProb { get; set; }

    /// <summary>
    /// The byte sequence that was used to generate the log probability.
    /// </summary>
    [JsonPropertyName("bytes")]
    public required List<int> Bytes { get; set; }
}