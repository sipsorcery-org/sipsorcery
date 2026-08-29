using System.Text.Json.Serialization;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

[JsonSerializable(typeof(RTCIceCandidateInit))]
public partial class SipSorceryJsonSerializerContext : JsonSerializerContext
{
}
