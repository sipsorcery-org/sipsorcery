using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

[JsonSerializable(typeof(RTCIceCandidateInit))]
[JsonSerializable(typeof(RTCSessionDescriptionInit))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    AllowTrailingCommas = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<RTCSdpType>)])]
public partial class SipSorceryJsonSerializerContext : JsonSerializerContext
{
}
