using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// RTCP Header as defined in RFC3550.
    /// </summary>
    partial class RTCPHeader
    {
        [Obsolete("Use ParseFeedbackType(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static RTCPFeedbackTypesEnum ParseFeedbackType(byte[] packet)
            => ParseFeedbackType(new ReadOnlySpan<byte>(packet));

        /// <summary>
        /// Extract and load the RTCP header from an RTCP packet.
        /// </summary>
        /// <param name="packet"></param>
        [Obsolete("Use RTCPHeader(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPHeader(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }
    }
}
