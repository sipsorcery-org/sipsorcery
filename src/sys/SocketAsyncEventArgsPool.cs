using Microsoft.Extensions.ObjectPool;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SIPSorcery.Sys
{
    internal static class SocketAsyncEventArgsPool
    {
        private static readonly ObjectPool<SipSorcerySocketAsyncEventArgs> pool = ObjectPool.Create(new SipSorcerySocketAsyncEventArgsPooledObjectPolicy());

        private static SipSorcerySocketAsyncEventArgs RentCore(Memory<byte> buffer, IDisposable? memoryOwner, SocketFlags socketFlags, EndPoint remoteEndPoint, EventHandler<SocketAsyncEventArgs>? handler)
        {
            var args = pool.Get();
            args.SetHandler(handler);
#if NETFRAMEWORK || !NETSTANDARD2_1_OR_GREATER || !NETCOREAPP2_1_OR_GREATER
            SetBuffer(args, buffer, ref memoryOwner);
#else
            args.SetBuffer(buffer);
#endif
            args.UserToken = memoryOwner;
            args.SocketFlags = socketFlags;
            args.RemoteEndPoint = remoteEndPoint;
            return args;
        }

        public static SocketAsyncEventArgs Rent(Memory<byte> buffer, IDisposable? memoryOwner, SocketFlags socketFlags, EndPoint remoteEndPoint, EventHandler<SocketAsyncEventArgs>? handler)
            => RentCore(buffer, memoryOwner, socketFlags, remoteEndPoint, handler);

        public static void Return(SocketAsyncEventArgs args) => pool.Return((SipSorcerySocketAsyncEventArgs)args);

        public static void SendToAsync(this Socket socket, Memory<byte> buffer, IDisposable? memoryOwner, SocketFlags socketFlags, IPEndPoint remoteEndPoint, EventHandler<SocketAsyncEventArgs>? handler)
        {
            var args = RentCore(buffer, memoryOwner, socketFlags, remoteEndPoint, handler);
            if (!socket.SendToAsync(args))
            {
                args.HandleCompleted(socket, args);
            }
        }

        public static void ReceiveFromAsync(this Socket socket, Memory<byte> buffer, IDisposable? memoryOwner, SocketFlags socketFlags, EndPoint remoteEndPoint, EventHandler<SocketAsyncEventArgs>? handler)
        {
            var args = RentCore(buffer, memoryOwner, socketFlags, remoteEndPoint, handler);
            if (!socket.ReceiveFromAsync(args))
            {
                args.HandleCompleted(socket, args);
            }
            //remoteEndPoint = (IPEndPoint)args.RemoteEndPoint;
        }

#if NETFRAMEWORK || !NETSTANDARD2_1_OR_GREATER || !NETCOREAPP2_1_OR_GREATER
        private static void SetBuffer(SocketAsyncEventArgs args, Memory<byte> buffer, ref IDisposable? memoryOwner)
        {
            if (!MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
            {
                memoryOwner?.Dispose();
                var newMemoryOwner = MemoryPool<byte>.Shared.Rent(buffer.Length);
                buffer.CopyTo(newMemoryOwner.Memory);
                memoryOwner = newMemoryOwner;
                MemoryMarshal.TryGetArray<byte>(newMemoryOwner.Memory, out segment);
            }

            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
        }
#endif

        /// <summary>
        /// A policy for pooling <see cref="SipSorcerySocketAsyncEventArgs"/> instances.
        /// </summary>
        private class SipSorcerySocketAsyncEventArgsPooledObjectPolicy : PooledObjectPolicy<SipSorcerySocketAsyncEventArgs>
        {
            /// <inheritdoc />
            public override SipSorcerySocketAsyncEventArgs Create()
            {
                return new SipSorcerySocketAsyncEventArgs();
            }

            /// <inheritdoc />
            public override bool Return(SipSorcerySocketAsyncEventArgs obj)
            {
                obj.SetHandler(null);
#if NETFRAMEWORK || !NETSTANDARD2_1_OR_GREATER || !NETCOREAPP2_1_OR_GREATER
                obj.SetBuffer(default, 0, 0);
#else
                obj.SetBuffer(default);
#endif
                obj.SetBuffer(null, 0, 0);

                if (obj.UserToken is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                obj.UserToken = null;

                return true;
            }
        }

        private sealed class SipSorcerySocketAsyncEventArgs : SocketAsyncEventArgs
        {
            private EventHandler<SocketAsyncEventArgs>? _handler;

            /// <summary>Creates an empty <see cref="SipSorcerySocketAsyncEventArgs" /> instance.</summary>
            /// <exception cref="NotSupportedException">The platform is not supported.</exception>
            public SipSorcerySocketAsyncEventArgs() :
#if NET5_0_OR_GREATER
                base(false)
#else
                base()
#endif
            {
                Completed += HandleCompleted;
            }

            public void SetHandler(EventHandler<SocketAsyncEventArgs>? handler)
            {
                _handler = handler;
            }

            public void HandleCompleted(object source, SocketAsyncEventArgs e)
            {
                try
                {
                    _handler?.Invoke(source, e);
                }
                finally
                {
                    Return(this);
                }
            }
        }
    }
}
