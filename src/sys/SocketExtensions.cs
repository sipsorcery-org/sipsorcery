using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys
{
    internal static class SocketExtensions
    {
#if NET6_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static int SendTo(this Socket socket, ReadOnlyMemory<byte> buffer, SocketFlags socketFlags, EndPoint remoteEP)
        {
#if NET6_0_OR_GREATER
            return socket.SendTo(buffer.Span, socketFlags, remoteEP);
#else
            if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                return socket.SendTo(segment.Array!, segment.Offset, segment.Count, socketFlags, remoteEP);
            }
            else
            {
                throw new NotSupportedException("Only array-backed memory is supported.");
            }
#endif
        }

#if NET6_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
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

        public static ValueTask<SocketReceiveFromResult> ReceiveFromAsync(
            this Socket socket,
            Memory<byte> buffer,
            SocketFlags socketFlags,
            EndPoint remoteEndPoint,
            CancellationToken cancellationToken = default)
        {
            if (!MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                throw new ArgumentException("The buffer must be array-backed.", nameof(buffer));
            }

            var args = new SocketAsyncEventArgs
            {
                RemoteEndPoint = remoteEndPoint,
                SocketFlags = socketFlags
            };

            args.SetBuffer(segment.Array!, segment.Offset, segment.Count);

            var tcs = new TaskCompletionSource<SocketReceiveFromResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Completed(object? s, SocketAsyncEventArgs e)
            {
                e.Completed -= Completed;
                e.Dispose();

                if (e.SocketError == SocketError.Success)
                {
                    var result = new SocketReceiveFromResult
                    {
                        ReceivedBytes = e.BytesTransferred,
                        RemoteEndPoint = e.RemoteEndPoint!,
                    };

                    tcs.TrySetResult(result);
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
            }

            args.Completed += Completed;

            bool pending;
            try
            {
                pending = socket.ReceiveFromAsync(args);
            }
            catch (Exception ex)
            {
                args.Completed -= Completed;
                args.Dispose();
                return new ValueTask<SocketReceiveFromResult>(Task.FromException<SocketReceiveFromResult>(ex));
            }

            if (!pending)
            {
                args.Completed -= Completed;
                args.Dispose();

                if (args.SocketError == SocketError.Success)
                {
                    var result = new SocketReceiveFromResult
                    {
                        ReceivedBytes = args.BytesTransferred,
                        RemoteEndPoint = args.RemoteEndPoint!,
                    };

                    return new ValueTask<SocketReceiveFromResult>(result);
                }
                else
                {
                    return new ValueTask<SocketReceiveFromResult>(
                        Task.FromException<SocketReceiveFromResult>(
                            new SocketException((int)args.SocketError)));
                }
            }

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            return new ValueTask<SocketReceiveFromResult>(tcs.Task);
        }

        public static ValueTask<int> SendToAsync(
            this Socket socket,
            ReadOnlyMemory<byte> buffer,
            SocketFlags socketFlags,
            EndPoint remoteEP,
            CancellationToken cancellationToken = default)
        {
            var args = new SocketAsyncEventArgs
            {
                RemoteEndPoint = remoteEP,
                SocketFlags = socketFlags
            };

            if (!MemoryMarshal.TryGetArray(buffer, out var segment))
            {
                throw new NotSupportedException("Only array-backed memory is supported.");
            }

            args.SetBuffer(segment.Array!, segment.Offset, segment.Count);

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            void CompletedHandler(object? s, SocketAsyncEventArgs e)
            {
                e.Completed -= CompletedHandler;
                e.Dispose();

                if (e.SocketError == SocketError.Success)
                {
                    tcs.TrySetResult(e.BytesTransferred);
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
            }

            args.Completed += CompletedHandler;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    args.Dispose();
                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            if (!socket.SendToAsync(args))
            {
                args.Completed -= CompletedHandler;
                args.Dispose();

                if (args.SocketError == SocketError.Success)
                {
                    return new ValueTask<int>(Task.FromResult(args.BytesTransferred));
                }
                else
                {
                    return new ValueTask<int>(Task.FromException<int>(new SocketException((int)args.SocketError)));
                }
            }

            return new ValueTask<int>(tcs.Task);
        }

        public static Task DisconnectAsync(this Socket socket, bool reuseSocket, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var args = new SocketAsyncEventArgs
            {
                DisconnectReuseSocket = reuseSocket
            };

            void CompletedHandler(object? s, SocketAsyncEventArgs e)
            {
                e.Completed -= CompletedHandler;

                if (e.SocketError == SocketError.Success)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }

                e.Dispose();
            }

            args.Completed += CompletedHandler;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    args.Completed -= CompletedHandler;
                    tcs.TrySetCanceled(cancellationToken);
                    args.Dispose();
                });
            }

            bool pending;
            try
            {
                pending = socket.DisconnectAsync(args);
            }
            catch (Exception ex)
            {
                args.Completed -= CompletedHandler;
                args.Dispose();
                return Task.FromException(ex);
            }

            if (!pending)
            {
                args.Completed -= CompletedHandler;
                args.Dispose();

                if (args.SocketError == SocketError.Success)
                {
                    return Task.CompletedTask;
                }
                else
                {
                    return Task.FromException(new SocketException((int)args.SocketError));
                }
            }

            return tcs.Task;
        }
#endif

#if !NET5_0_OR_GREATER
        public static Task ConnectAsync(this Socket socket, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var args = new SocketAsyncEventArgs
            {
                RemoteEndPoint = remoteEndPoint
            };

            void CompletedHandler(object s, SocketAsyncEventArgs e)
            {
                args.Completed -= CompletedHandler;

                if (e.SocketError == SocketError.Success)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
            }

            args.Completed += CompletedHandler;

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    args.Completed -= CompletedHandler;
                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            if (!socket.ConnectAsync(args))
            {
                args.Completed -= CompletedHandler;

                if (args.SocketError == SocketError.Success)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)args.SocketError));
                }
            }

            return tcs.Task;
        }
#endif
    }
}
