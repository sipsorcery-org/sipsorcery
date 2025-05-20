using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents an RTCP compound packet consisting of 1 or more
    /// RTCP packets combined together in a single buffer. According to RFC3550 RTCP 
    /// transmissions should always have at least 2 RTCP packets (a sender/receiver report
    /// and an SDES report). This implementation does not enforce that constraint for
    /// received reports but does for sends.
    /// </summary>
    partial class RTCPCompoundPacket
    {
        /// <summary>
        /// Creates a new RTCP compound packet from a serialised buffer.
        /// </summary>
        /// <param name="packet">The serialised RTCP compound packet to parse.</param>
        [Obsolete("Use RTCPCompoundPacket(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPCompoundPacket(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }

        /// <summary>
        /// Creates a new RTCP compound packet from a serialised buffer.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="rtcpCompoundPacket"></param>
        /// <param name="consumed"></param>
        /// <returns>The amount read from the packet</returns>
        [Obsolete("Use TryParse(ReadOnlySpan<byte>, RTCPCompoundPacket, int) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static bool TryParse(
            byte[] packet,
            RTCPCompoundPacket rtcpCompoundPacket,
            out int consumed)
        {
            return TryParse(packet.AsSpan(), rtcpCompoundPacket, out consumed);
        }
    }
}
