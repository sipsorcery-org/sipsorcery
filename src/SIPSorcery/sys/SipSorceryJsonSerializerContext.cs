using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

[JsonSerializable(typeof(RTCIceCandidateInit))]
internal partial class SipSorceryJsonSerializerContext : JsonSerializerContext
{
}
