using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys
{
    internal static class SocketExtensions
    {
        public static int SendTo(this Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEP)
        {
#if NET6_0_OR_GREATER
            return socket.SendTo(buffer.Span, socketFlags, remoteEP);
#else
            if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
            {
                return socket.SendTo(segment.Array, segment.Offset, buffer.Length, socketFlags, remoteEP);
            }
            else
            {
                var tempBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    return socket.SendTo(tempBuffer, 0, buffer.Length, socketFlags, remoteEP);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(tempBuffer);
                }
            }
#endif
        }

        public static int ReceiveFrom(this Socket socket, Memory<byte> buffer, SocketFlags socketFlags, ref EndPoint remoteEP)
        {
#if NET6_0_OR_GREATER
            return socket.ReceiveFrom(buffer.Span, socketFlags, ref remoteEP);
#else
            if (MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
            {
                return socket.ReceiveFrom(segment.Array, segment.Offset, buffer.Length, socketFlags, ref remoteEP);
            }
            else
            {
                var tempBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    var bytesReceived = socket.ReceiveFrom(tempBuffer, 0, buffer.Length, socketFlags, ref remoteEP);
                    tempBuffer.AsMemory(0, bytesReceived).CopyTo(buffer);
                    return bytesReceived;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(tempBuffer);
                }
            }
#endif
        }

#if !NET6_0_OR_GREATER
        public static ValueTask<SocketReceiveFromResult> ReceiveFromAsync(this Socket socket, Memory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint)
        {
            var tcs = new TaskCompletionSource<SocketReceiveFromResult>();

            socket.ReceiveFromAsync(
                buffer,
                null,
                socketFlags,
                remoteEndPoint,
                (sender, e) => tcs.TrySetResult(new SocketReceiveFromResult
                {
                    ReceivedBytes = e.BytesTransferred,
                    RemoteEndPoint = e.RemoteEndPoint,
                }));

            return new ValueTask<SocketReceiveFromResult> (tcs.Task);
        }
#endif
    }
}
