using System.Text.Json.Serialization;
using System.Text.Json;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// The System.Text.Json serialisation options used for every Gemini Live message.
/// </summary>
public class JsonOptions
{
    public static readonly JsonSerializerOptions Default;

    static JsonOptions()
    {
        Default = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            WriteIndented = true,
            Converters =
                {
                    // Allow enum values or member attribute values, e.g. [EnumMember(Value = "xxx")] to be deserialised from strings.
                    new JsonStringEnumMemberConverter(),
                },
            // Property names are set explicitly via [JsonPropertyName] to match Gemini's camelCase wire format.
            PropertyNamingPolicy = null
        };
    }
}
