#nullable enable
using System;
using System.Buffers;

namespace SIPSorcery.Sys
{
    internal struct BorrowedArray: IDisposable
    {
        byte[]? data;
        int length;
        ArrayPool<byte>? dataOwner;

        public readonly bool IsNull() => data == null;
        public readonly Span<byte> Data => data.AsSpan(0, length);
        public readonly Span<byte> DataMayBeEmpty
            => data is { } array
            ? array.AsSpan(0, Math.Min(length, array.Length))
            : [];
        public readonly int Length => length;

        public static implicit operator Span<byte>(BorrowedArray borrowed) => borrowed.Data;

        public void Set(ReadOnlySpan<byte> data, ArrayPool<byte> pool)
        {
            if (this.data?.Length >= data.Length)
            {
                data.CopyTo(this.data);
                length = data.Length;
                return;
            }

            Empty();
            dataOwner = pool;
            this.data = pool.Rent(data.Length);
            data.CopyTo(this.data);
            length = data.Length;
        }

        public void Set(ReadOnlySpan<byte> data) => Set(data, ArrayPool<byte>.Shared);

        public void Set(byte[] data)
        {
            Empty();
            this.data = data;
            length = data.Length;
        }

        void Empty()
        {
            dataOwner?.Return(data);
            data = null;
            dataOwner = null;
        }

        public void Dispose() => Empty();
    }
}
