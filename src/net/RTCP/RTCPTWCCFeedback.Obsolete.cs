using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class RTCPTWCCFeedback
    {
        /// <summary>
        /// Constructs a TWCC feedback message from the raw RTCP packet.
        /// </summary>
        /// <param name="packet">The complete RTCP TWCC feedback packet.</param>
        /// <summary>
        /// Parses a TWCC feedback packet from the given byte array.
        /// </summary>
        [Obsolete("Use RTCPSDesReport(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPTWCCFeedback(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }

        [Obsolete("Use ValidatePacket(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        private void ValidatePacket(byte[] packet)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            ValidatePacket(new ReadOnlySpan<byte>(packet));
        }
    }
}
