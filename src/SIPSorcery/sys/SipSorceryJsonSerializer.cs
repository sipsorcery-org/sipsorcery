using System;
using System.Buffers;
using System.Text.Json;

namespace SIPSorcery.Sys;

internal static class SipSorceryJsonSerializer
{
    private static readonly JsonWriterOptions jsonWriterOptions = new()
    {
        Indented = false,
    };

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, SipSorceryJsonSerializerContext.Default.Options);

    public static void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer, jsonWriterOptions);
        JsonSerializer.Serialize<T>(jsonWriter, value, SipSorceryJsonSerializerContext.Default.Options);
    }

    public static T Deserialize<T>(ReadOnlySpan<char> json)
        => JsonSerializer.Deserialize<T>(json, SipSorceryJsonSerializerContext.Default.Options);
}
