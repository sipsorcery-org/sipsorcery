using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

internal static partial class SipSorceryJsonSerializer
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SipSorceryJsonSerializerContext.Default.Options);

    public static T Deserialize<T>(ReadOnlySpan<char> json) => JsonSerializer.Deserialize<T>(json, SipSorceryJsonSerializerContext.Default.Options);

    [JsonSerializable(typeof(RTCIceCandidateInit))]
    private partial class SipSorceryJsonSerializerContext : JsonSerializerContext
    {
    }
}
