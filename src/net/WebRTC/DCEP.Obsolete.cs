using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial struct DataChannelOpenMessage
    {
        /// <summary>
        /// Parses the an DCEP open message from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse the message from.</param>
        /// <param name="posn">The position in the buffer to start parsing from.</param>
        /// <returns>A new DCEP open message instance.</returns>
        [Obsolete("Use Parse(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static DataChannelOpenMessage Parse(byte[] buffer, int posn)
        {
            return Parse(buffer.AsSpan(posn));
        }
    }
}
