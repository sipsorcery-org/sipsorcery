using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class RTCSctpTransport
    {
        /// <summary>
        /// This method is called by the SCTP association when it wants to send an SCTP packet
        /// to the remote party.
        /// </summary>
        /// <param name="associationID">Not used for the DTLS transport.</param>
        /// <param name="buffer">The buffer containing the data to send.</param>
        /// <param name="offset">The position in the buffer to send from.</param>
        /// <param name="length">The number of bytes to send.</param>
        [Obsolete("Use Send(string, Memory<byte>, IDisposable?) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public override void Send(string associationID, byte[] buffer, int offset, int length)
            => Send(associationID, buffer.AsMemory(offset, length), null);
    }
}
