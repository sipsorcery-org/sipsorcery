using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class SctpTransport
    {
        [Obsolete("Use Send(string, Memory<byte>, IDisposable?) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public virtual void Send(string associationID, byte[] buffer, int offset, int length)
            => Send(associationID, buffer.AsMemory(offset, length), null);

        /// <summary>
        /// This is the main method to send user data via SCTP.
        /// </summary>
        /// <param name="associationID">Local handle to the SCTP association.</param>
        /// <param name="buffer">The buffer holding the data to send.</param>
        /// <param name="length">The number of bytes from the buffer to send.</param>
        /// <param name="contextID">Optional. A 32-bit integer that will be carried in the
        /// sending failure notification to the application if the transportation of
        /// this user message fails.</param>
        /// <param name="streamID">Optional. To indicate which stream to send the data on. If not
        /// specified, stream 0 will be used.</param>
        /// <param name="lifeTime">Optional. specifies the life time of the user data. The user
        /// data will not be sent by SCTP after the life time expires.This
        /// parameter can be used to avoid efforts to transmit stale user
        /// messages.</param>
        /// <returns></returns>
        [Obsolete("Use Send(string, ReadOnlyMemory<byte>, IDisposable?, int, int, int) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public string Send(string associationID, byte[] buffer, int length, int contextID, int streamID, int lifeTime)
            => Send(associationID, buffer.AsMemory(0, length), null, contextID, streamID, lifeTime);
    }
}
