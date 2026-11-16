
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public class RealtimeEventBaseConverter : JsonConverter<RealtimeEventBase>
{
    public override RealtimeEventBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
            {
                return new RealtimeUnknown
                {
                    OriginalType = null,
                    OriginalJson = root.GetRawText()
                };
            }

            string typeValue = typeProp.GetString() ?? string.Empty;

            if (RealtimeEventTypes.TypeMap.TryGetValue(typeValue, out Type? eventType))
            {
                return (RealtimeEventBase?)JsonSerializer.Deserialize(
                    doc.RootElement.GetRawText(),
                    eventType,
                    options);
            }

            return new RealtimeUnknown
            {
                EventID = doc.RootElement.TryGetProperty("event_id", out var eventIdProp)
                    ? eventIdProp.GetString()
                    : null,
                OriginalType = typeValue,
                OriginalJson = doc.RootElement.GetRawText()
            };
        }
    }

    public override void Write(Utf8JsonWriter writer, RealtimeEventBase value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
