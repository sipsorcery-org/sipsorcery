using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public class RealtimeUnknown : RealtimeEventBase
{
    public const string TypeName = "realtime.unknown";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    public string? OriginalType { get; set; }

    public string? OriginalJson { get; set; }
}