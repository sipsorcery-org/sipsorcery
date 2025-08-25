using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class RTCPFeedback
    {
        /// <summary>
        /// Create a new RTCP Report from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the serialised feedback report.</param>
        [Obsolete("Use RTCPFeedback(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPFeedback(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }
    }
}
