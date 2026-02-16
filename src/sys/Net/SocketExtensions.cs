// Adapted from lostmsu's fix for #1494.
// See https://github.com/sipsorcery-org/sipsorcery/issues/1494

using System;
using System.Net;
using System.Net.Sockets;

namespace SIPSorcery.Sys
{
    static class SocketExtensions
    {
        /// <summary>
        /// Completes a pending <see cref="Socket.EndReceiveFrom"/> on a socket that
        /// is already closed. Per the .NET APM contract, <c>End*</c> must always be
        /// called for every <c>Begin*</c>. Skipping the call leaves the underlying
        /// <see cref="IAsyncResult"/> task uncompleted, which surfaces as an
        /// <c>UnobservedTaskException</c> that can crash the process.
        /// </summary>
        public static void EndReceiveFromClosed(this Socket socket, IAsyncResult asyncResult, ref EndPoint ep)
        {
            try
            {
                socket.EndReceiveFrom(asyncResult, ref ep);
            }
            catch (ObjectDisposedException)
            {
                // Socket has been closed, ignore.
            }
            catch (SocketException abort) when (abort.SocketErrorCode == SocketError.OperationAborted)
            {
                // Socket has been closed, ignore.
            }
        }
    }
}
