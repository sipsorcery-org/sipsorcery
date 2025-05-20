using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

[JsonSerializable(typeof(RTCIceCandidateInit))]
[JsonSerializable(typeof(RTCSessionDescriptionInit))]
[JsonSerializable(typeof(SctpTransportCookie))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<RTCSdpType>)])]
internal partial class SipSorceryJsonSerializerContext : JsonSerializerContext
{
}
