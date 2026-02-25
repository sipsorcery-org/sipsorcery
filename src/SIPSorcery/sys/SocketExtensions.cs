using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SIPSorcery;

internal static class SocketExtensions
{
    extension(Socket socket)
    {
        public void SendTo(EndPoint remoteEP, ReadOnlyMemory<byte> buffer, IDisposable? owner)
        {
            using var disposableOwner = owner;

#if NET6_0_OR_GREATER
            socket.SendTo(buffer.Span, SocketFlags.None, remoteEP);
#else
            if (buffer.IsEmpty)
            {
                socket.SendTo(Array.Empty<byte>(), 0, 0, SocketFlags.None, remoteEP);
            }
            else
            {
                if (MemoryMarshal.TryGetArray(buffer, out var arraySegment))
                {
                    socket.SendTo(arraySegment.Array!, arraySegment.Offset, arraySegment.Count, SocketFlags.None, remoteEP);
                }
                else
                {
                    var rentedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                    try
                    {
                        buffer.Span.CopyTo(rentedBuffer);
                        socket.SendTo(rentedBuffer, 0, buffer.Length, SocketFlags.None, remoteEP);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                    }
                }
            }
#endif
        }
    }
}
