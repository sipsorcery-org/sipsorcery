using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

[JsonSerializable(typeof(RTCIceCandidateInit))]
[JsonSerializable(typeof(RTCSessionDescriptionInit))]
[JsonSerializable(typeof(SctpTransportCookie))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    AllowTrailingCommas = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<RTCSdpType>)])]
internal sealed partial class SipSorceryJsonSerializerContext : JsonSerializerContext
{
}
