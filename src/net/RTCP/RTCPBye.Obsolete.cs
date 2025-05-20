using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// RTCP Goodbye packet as defined in RFC3550. The BYE packet indicates 
    /// that one or more sources are no longer active.
    /// </summary>
    partial class RTCPBye
    {
        /// <summary>
        /// Create a new RTCP Goodbye packet from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the Goodbye packet.</param>
        [Obsolete("Use RTCPBye(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPBye(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }

        /// <summary>
        /// Gets the raw bytes for the Goodbye packet.
        /// </summary>
        /// <returns>A byte array.</returns>
        [Obsolete("Use WriteBytes(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public byte[] GetBytes()
        {
            var buffer = new byte[GetPacketSize()];

            WriteBytesCore(buffer);

            return buffer;
        }
    }
}
