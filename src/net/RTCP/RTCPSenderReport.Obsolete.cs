using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An RTCP sender report is for use by active RTP senders. 
    /// </summary>
    /// <remarks>
    /// From https://tools.ietf.org/html/rfc3550#section-6.4:
    /// "The only difference between the
    /// sender report(SR) and receiver report(RR) forms, besides the packet
    /// type code, is that the sender report includes a 20-byte sender
    /// information section for use by active senders.The SR is issued if a
    /// site has sent any data packets during the interval since issuing the
    /// last report or the previous one, otherwise the RR is issued."
    /// </remarks>
    partial class RTCPSenderReport
    {
        /// <summary>
        /// Create a new RTCP Sender Report from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the serialised sender report.</param>
        [Obsolete("Use RTCPSenderReport(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPSenderReport(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }
    }
}
