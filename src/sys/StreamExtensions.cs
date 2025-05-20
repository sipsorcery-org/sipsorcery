using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys
{
#if !(NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER) || NETFRAMEWORK
    internal static class StreamExtensions
    {
        public static Task WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                throw new NotSupportedException("The ReadOnlySpan<byte> is not backed by an array.");
            }

            return stream.WriteAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken);
        }
        
        public static async ValueTask<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
            {
                throw new NotSupportedException("The provided Memory<byte> is not backed by an array.");
            }

            return await stream.ReadAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask DisposeAsync(this Stream stream)
        {
            if (stream is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                stream.Dispose();
            }
        }
    }
#endif
}
