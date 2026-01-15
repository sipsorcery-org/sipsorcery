using System;
using System.Net;
using System.Net.Sockets;

namespace SIPSorcery.Sys;

static class SocketExtensions
{
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
