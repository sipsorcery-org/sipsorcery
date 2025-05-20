using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class SctpPacket
    {
        /// <summary>
        /// Parses an SCTP packet from a serialised buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="offset">The position in the buffer of the packet.</param>
        /// <param name="length">The length of the serialised packet in the buffer.</param>
        [Obsolete("Use Parse(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpPacket Parse(byte[] buffer, int offset, int length)
        {
            return Parse(buffer.AsSpan(offset, length));
        }

        /// <summary>
        /// Verifies whether the checksum for a serialised SCTP packet is valid.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="posn">The start position in the buffer.</param>
        /// <param name="length">The length of the packet in the buffer.</param>
        /// <returns>True if the checksum was valid, false if not.</returns>
        [Obsolete("Use VerifyChecksum(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static bool VerifyChecksum(byte[] buffer, int posn, int length)
        {
            return VerifyChecksum(buffer.AsSpan(posn, length));
        }

        /// <summary>
        /// Gets the verification tag from a serialised SCTP packet. This allows
        /// a pre-flight check to be carried out before de-serialising the whole buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="posn">The start position in the buffer.</param>
        /// <param name="length">The length of the packet in the buffer.</param>
        /// <returns>The verification tag for the serialised SCTP packet.</returns>
        [Obsolete("Use GetVerificationTag(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static uint GetVerificationTag(byte[] buffer, int posn, int length)
        {
            return GetVerificationTag(buffer.AsSpan(posn, length));
        }

        /// <summary>
        /// Performs verification checks on a serialised SCTP packet.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="posn">The start position in the buffer.</param>
        /// <param name="length">The length of the packet in the buffer.</param>
        /// <param name="requiredTag">The required verification tag for the serialised
        /// packet. This should match the verification tag supplied by the remote party.</param>
        /// <returns>True if the packet is valid, false if not.</returns>
        [Obsolete("Use IsValid(ReadOnlySpan<byte>, uint) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static bool IsValid(byte[] buffer, int posn, int length, uint requiredTag)
        {
            return IsValid(buffer.AsSpan(posn, length), requiredTag);
        }
    }
}
