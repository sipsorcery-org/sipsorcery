using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class RTCPReceiverReport
    {
        /// <summary>
        /// Create a new RTCP Receiver Report from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the serialised receiver report.</param>
        [Obsolete("Use RTCPReceiverReport(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPReceiverReport(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }
    }
}
