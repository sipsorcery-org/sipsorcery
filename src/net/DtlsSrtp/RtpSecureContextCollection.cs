//-----------------------------------------------------------------------------
// Filename: RtpSecureContextCollection.cs
//
// Description: Represents a secure context for Rtp Sessions
//
// Author(s):
// Jean-Philippe Fournier
//
// History:
// 5 January 2022 : Jean-Philippe Fournier, created Montréal, QC, Canada
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.Sys;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    public class SecureContext
    {
        public ProtectRtpPacket ProtectRtpPacket { get; private set; }
        public ProtectRtpPacket ProtectRtcpPacket { get; private set; }

        public ProtectRtpPacket UnprotectRtpPacket { get; private set; }
        public ProtectRtpPacket UnprotectRtcpPacket { get; private set;}

        public SecureContext(ProtectRtpPacket protectRtpPacket, ProtectRtpPacket unprotectRtpPacket, ProtectRtpPacket protectRtcpPacket, ProtectRtpPacket unprotectRtcpPacket)
        {
            ProtectRtpPacket = protectRtpPacket;
            ProtectRtcpPacket = protectRtcpPacket;
            UnprotectRtpPacket = unprotectRtpPacket;
            UnprotectRtcpPacket = unprotectRtcpPacket;
        }
    }

    public class RtpSecureContextCollection
    {
        private readonly ILogger _logger = Log.Logger;

        private readonly ConcurrentDictionary<SDPMediaTypesEnum, SecureContext> m_handlerCollection;

        public RtpSecureContextCollection()
        {
            m_handlerCollection = new ConcurrentDictionary<SDPMediaTypesEnum, SecureContext>();
        }

        public void SetSecureContextForMediaType(SDPMediaTypesEnum mediaType, SecureContext srtpHandler)
        {
            var result = m_handlerCollection.TryAdd(mediaType, srtpHandler);
            if (!result)
            {
                _logger.LogTrace($"Tried adding new SecureContext for media type {mediaType}, but one already existed");
            }
        }

        public SecureContext GetSecureContext(SDPMediaTypesEnum mediaType)
        {
            m_handlerCollection.TryGetValue(mediaType, out var secureContext);
            return secureContext;
        }

        public bool IsSecureContextReady(SDPMediaTypesEnum mediaType)
        {
            m_handlerCollection.TryGetValue(mediaType, out var secureContext);
            return secureContext != null;
        }
    }
}