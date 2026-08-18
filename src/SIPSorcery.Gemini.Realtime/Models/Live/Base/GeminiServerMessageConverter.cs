using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Deserialises a Gemini BidiGenerateContent server message. Gemini's wire format wraps each
/// message under a single top-level property whose NAME is the type discriminator, e.g.
/// {"serverContent": {...}} or {"toolCall": {...}} — there is no "type" field inside the
/// payload itself (unlike the OpenAI Realtime API). This converter inspects the root object's
/// property names, looks the first recognised one up in <see cref="GeminiServerMessageTypes.TypeMap"/>,
/// and deserialises the VALUE of that property (not the whole wrapper) into the mapped type.
///
/// <c>usageMetadata</c> is the one exception to "one message per wire message": it sits alongside
/// the message-type union rather than inside it, so Gemini routinely sends it in the same JSON
/// object as a <c>serverContent</c> (or any other) message. It is therefore parsed independently
/// and attached to <see cref="GeminiServerMessage.UsageMetadata"/>, which keeps both halves —
/// picking whichever key happened to come first would silently discard the other.
/// </summary>
public class GeminiServerMessageConverter : JsonConverter<GeminiServerMessage>
{
    public override GeminiServerMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new GeminiUnknownServerMessage { OriginalJson = root.GetRawText() };
            }

            GeminiServerMessage? message = null;
            string? firstKey = null;

            foreach (var property in root.EnumerateObject())
            {
                firstKey ??= property.Name;

                if (message == null && GeminiServerMessageTypes.TypeMap.TryGetValue(property.Name, out Type? messageType))
                {
                    message = (GeminiServerMessage?)JsonSerializer.Deserialize(
                        property.Value.GetRawText(),
                        messageType,
                        options);
                }
            }

            GeminiUsageMetadata? usageMetadata = null;
            if (root.TryGetProperty(GeminiServerEventUsageMetadata.JsonKey, out var usageElement))
            {
                usageMetadata = JsonSerializer.Deserialize<GeminiUsageMetadata>(usageElement.GetRawText(), options);

                // Usage arrived on its own, with no member of the message-type union alongside it.
                message ??= new GeminiServerEventUsageMetadata();
            }

            message ??= new GeminiUnknownServerMessage
            {
                OriginalKey = firstKey,
                OriginalJson = root.GetRawText()
            };

            message.UsageMetadata = usageMetadata;

            return message;
        }
    }

    public override void Write(Utf8JsonWriter writer, GeminiServerMessage value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
