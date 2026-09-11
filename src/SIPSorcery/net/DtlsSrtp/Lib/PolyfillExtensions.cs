using System;
using System.Runtime.CompilerServices;

internal static partial class PolyfillExtensions
{
#if !NET8_0_OR_GREATER
    extension(GC)
    {
        /// <summary>
        /// Allocate an array while skipping zero-initialization if possible.
        /// </summary>
        /// <typeparam name="T">Specifies the type of the array element.</typeparam>
        /// <param name="length">Specifies the length of the array.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // forced to ensure no perf drop for small memory buffers (hot path)
        public static T[] AllocateUninitializedArray<T>(int length) // T[] rather than T?[] to match `new T[length]` behavior
        {
            return new T[length];
        }
    }

    extension<T>(ArraySegment<T> source)
    {
        public int Length => source.Count;

        public T[] ToArray()
        {
            var array = new T[source.Count];
            Array.Copy(source.Array, source.Offset, array, 0, source.Count);
            return array;
        }

        public void CopyTo(Span<T> destination)
        {
            new ReadOnlySpan<T>(source.Array, source.Offset, source.Count).CopyTo(destination);
        }

        public ArraySegment<T> Slice(int index)
        {
            if (source.Array == null)
            {
                throw new InvalidOperationException();
            }

            if ((uint)index > (uint)source.Count)
            {
                throw new ArgumentOutOfRangeException();
            }

            return new ArraySegment<T>(source.Array, source.Offset + index, source.Count - index);
        }

        public ArraySegment<T> Slice(int index, int count)
        {
            if (source.Array == null)
            {
                throw new InvalidOperationException();
            }

            if ((uint)index > (uint)source.Count || (uint)count > (uint)(source.Count - index))
            {
                throw new ArgumentOutOfRangeException();
            }

            return new ArraySegment<T>(source.Array, source.Offset + index, count);
        }

        public void CopyTo(T[] destination)
        {
            new ReadOnlySpan<T>(source.Array, source.Offset, source.Count).CopyTo(destination);
        }
    }

    extension (byte[] bytes)
    {
        public void CopyTo(Span<byte> destination)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (destination.Length < bytes.Length)
            {
                throw new ArgumentException("Destination span is too small.", nameof(destination));
            }
            bytes.AsSpan().CopyTo(destination);
        }
    }
#endif

#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> Slice<T>(this T[] array, int index) => array.AsSpan(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> Slice<T>(this T[] array, int index, int count) => array.AsSpan(index, count);
#else
    public static ArraySegment<T> Slice<T>(this T[] array, int index) => new ArraySegment<T>(array, index, array.Length - index);

    public static ArraySegment<T> Slice<T>(this T[] array, int index, int count) => new ArraySegment<T>(array, index, count);
#endif

    public static ArraySegment<T> AsArraySegment<T>(this T[] array) => array != null ? new ArraySegment<T>(array) : default(ArraySegment<T>);

    public static ArraySegment<T> AsArraySegment<T>(this T[] array, int count) => array != null ? new ArraySegment<T>(array, array.Length - count, count) : default(ArraySegment<T>);

    public static ArraySegment<T> AsArraySegment<T>(this T[] array, int offset, int count) => array != null ? new ArraySegment<T>(array, offset, count) : default(ArraySegment<T>);
}
