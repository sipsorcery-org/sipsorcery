using System;

namespace SIPSorcery.Net;

public static class SDPExtensions
{
    extension(SDP source)
    {
        public static SDP? ParseSDPDescription(string sdpDescription) => SDP.ParseSDPDescription(sdpDescription.AsSpan());
    }
}
