using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// RTCP Source Description (SDES) report as defined in RFC3550.
    /// Only the mandatory CNAME item is supported.
    /// </summary>
    partial class RTCPSDesReport
    {
        /// <summary>
        /// Create a new RTCP SDES item from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the SDES report.</param>
        [Obsolete("Use RTCPSDesReport(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPSDesReport(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }
    }
}
