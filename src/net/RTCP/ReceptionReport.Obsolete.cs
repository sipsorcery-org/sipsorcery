using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents a point in time sample for a reception report.
    /// </summary>
    partial class ReceptionReportSample
    {
        [Obsolete("Use ReceptionReportSample(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public ReceptionReportSample(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }

        /// <summary>
        /// Serialises the reception report block to a byte array.
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
