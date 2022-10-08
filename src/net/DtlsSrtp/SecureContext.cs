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
}