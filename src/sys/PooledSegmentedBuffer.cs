using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.Sys;

/// <summary>
/// A pooled buffer writer that implements <see cref="IBufferWriter{T}"/> using <see cref="ArrayPool{T}.Shared"/> for efficient writing of data and
/// allows reading the written content through a <see cref="ReadOnlySequence{T}"/> using the <see cref="GetReadOnlySequence"/> method.
/// </summary>
/// <typeparam name="T">The type of elements in the buffer.</typeparam>
internal sealed class PooledSegmentedBuffer<T> : IBufferWriter<T>, IDisposable
{
    private const int DefaultBufferSize = 4096;

    private readonly ArrayPool<T> _pool;
    private readonly int _minimalIncrease;
    private List<ReadOnlyMemory<T>>? _buffers;
    private T[]? _currentBuffer;
    private int _currentOffset;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledSegmentedBuffer{T}"/> class with an optional minimal increase size and array pool.
    /// </summary>
    /// <param name="minimalIncrease">The minimal increase size when adding new buffers. Defaults to 4096 elements.</param>
    /// <param name="pool">The array pool to use. If null, <see cref="ArrayPool{T}.Shared"/> is used.</param>
    public PooledSegmentedBuffer(int minimalIncrease = DefaultBufferSize, ArrayPool<T>? pool = null)
    {
        _minimalIncrease = minimalIncrease;
        _pool = pool ?? ArrayPool<T>.Shared;
    }

    /// <summary>
    /// Gets the total number of elements that have been written to the buffer across all segments.
    /// </summary>
    /// <value>The total length of data written to the buffer.</value>
    public int Length { get; private set; }

    /// <summary>
    /// Notifies the buffer writer that <paramref name="count"/> elements were written.
    /// </summary>
    /// <param name="count">The number of elements written.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative or exceeds the current buffer capacity.</exception>
    public void Advance(int count)
    {
        if (count == 0)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var currentCapacity = (_currentBuffer?.Length).GetValueOrDefault() - _currentOffset;

        if (count > currentCapacity)
        {
            ThrowInvalidOperationException("Insufficient buffer capacity.");
        }

        _currentOffset += count;
        Length += count;
    }

    /// <summary>
    /// Returns a <see cref="Memory{T}"/> buffer to write to, ensuring at least <paramref name="sizeHint"/> elements are available.
    /// </summary>
    /// <param name="sizeHint">The minimum number of elements required. May be 0.</param>
    /// <returns>A writable memory buffer.</returns>
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _currentBuffer.AsMemory(_currentOffset);
    }

    /// <summary>
    /// Returns a <see cref="Span{T}"/> buffer to write to, ensuring at least <paramref name="sizeHint"/> elements are available.
    /// </summary>
    /// <param name="sizeHint">The minimum number of elements required. May be 0.</param>
    /// <returns>A writable span buffer.</returns>
    public Span<T> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _currentBuffer.AsSpan(_currentOffset);
    }

    /// <summary>
    /// Returns a <see cref="ReadOnlySequence{T}"/> representing the written data across all buffers.
    /// </summary>
    /// <returns>A read-only sequence of elements.</returns>
    public ReadOnlySequence<T> GetReadOnlySequence()
    {
        SequenceSegment? first = null;
        SequenceSegment? last = null;

        if (_buffers is { })
        {
#if NET5_0_OR_GREATER
            var buffers = CollectionsMarshal.AsSpan(_buffers);
#else
            var buffers = _buffers;
#endif
            foreach (var buffer in buffers)
            {
                if (buffer.Length == 0)
                {
                    continue;
                }

                var segment = new SequenceSegment(buffer);

                if (first is null)
                {
                    first = segment;
                }

                last?.SetNext(segment);

                last = segment;
            }
        }

        if (_currentBuffer is { } && _currentOffset > 0)
        {
            var currentSegment = new SequenceSegment(_currentBuffer.AsMemory(0, _currentOffset));
            if (first is null)
            {
                first = currentSegment;
            }

            last?.SetNext(currentSegment);

            last = currentSegment;
        }

        if (first is null || last is null)
        {
            return ReadOnlySequence<T>.Empty;
        }

        return new ReadOnlySequence<T>(first, 0, last, last.Memory.Length);
    }

    /// <summary>
    /// Slices the internal buffers to only contain the requested range, discarding all other data.
    /// Efficiently trims from start and end without allocating new lists or copying data.
    /// </summary>
    /// <param name="offset">The starting offset of the slice.</param>
    /// <param name="length">The length of the slice.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if offset or length are out of bounds.</exception>
    /// <remarks>
    /// <para>
    /// This method modifies the buffer in-place by trimming unwanted data from both ends.
    /// It efficiently returns unused buffer segments back to the <see cref="ArrayPool{T}"/> 
    /// and adjusts internal state to only contain the specified range.
    /// </para>
    /// <para>
    /// After slicing, the <see cref="Length"/> property will be updated to reflect the new size,
    /// and subsequent operations will only work with the sliced data range.
    /// </para>
    /// <para>
    /// This is a destructive operation - data outside the specified range is permanently discarded
    /// and cannot be recovered.
    /// </para>
    /// </remarks>
    public void Slice(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > Length)
        {
            throw new ArgumentOutOfRangeException($"Invalid slice: offset={offset}, length={length}, buffer length={Length}");
        }

        var remainingLength = length;

        if (_buffers is { Count: > 0 })
        {
#if NET5_0_OR_GREATER
            var buffers = CollectionsMarshal.AsSpan(_buffers);
            var buffersLength = buffers.Length;
#else
            var buffers = _buffers;
            var buffersLength = buffers.Count;
#endif

            var trimStartCount = buffersLength;

            for (var i = 0; i < buffersLength; i++)
            {
#if NET5_0_OR_GREATER
                ref var buffer = ref buffers[i];
#else
                var buffer = buffers[i];
#endif
                var bufferLength = buffer.Length;

                if (bufferLength < offset)
                {
                    offset -= bufferLength;
                    ReturnBuffer(buffer);
                }
                else
                {
                    if (offset > 0)
                    {
                        buffer = buffer.Slice(offset);
#if !NET5_0_OR_GREATER
                        buffers[i] = buffer;
#endif
                    }

                    trimStartCount = i;
                    offset = 0;
                    break;
                }
            }

            var trimEndStartIndex = buffersLength;

            for (var i = trimStartCount; i < buffersLength; i++)
            {
                trimEndStartIndex = i + 1;

#if NET5_0_OR_GREATER
                ref var buffer = ref buffers[i];
#else
                var buffer = buffers[i];
#endif
                var bufferLength = buffer.Length;

                if (bufferLength > remainingLength)
                {
                    buffer = buffer.Slice(0, remainingLength);
#if !NET5_0_OR_GREATER
                    buffers[i] = buffer;
#endif
                    remainingLength = 0;
                    break;
                }
                else
                {
                    remainingLength -= bufferLength;
                }
            }

            if (trimEndStartIndex < buffersLength)
            {
                for (var i = trimEndStartIndex; i < buffersLength; i++)
                {
                    ReturnBuffer(_buffers[i]);
                }

                _buffers.RemoveRange(trimEndStartIndex, _buffers.Count - trimEndStartIndex);
            }

            if (trimStartCount > 0)
            {
                _buffers.RemoveRange(0, trimStartCount);
            }
        }

        if (remainingLength > 0)
        {
            Debug.Assert(_currentBuffer is { });
            Debug.Assert(remainingLength <= _currentOffset);

            (_buffers ??= new()).Add(_currentBuffer.AsMemory(offset, remainingLength));
        }
        else if (_currentBuffer is { })
        {
            _pool.Return(_currentBuffer);
        }

        _currentBuffer = null;
        _currentOffset = 0;

        // Set new Length
        Length = length;
    }

    /// <summary>
    /// Returns a <see cref="Stream"/> that provides read-only access to the buffer contents.
    /// This method is only available for <see cref="PooledSegmentedBuffer{T}"/> where T is <see cref="byte"/>.
    /// </summary>
    /// <returns>A read-only stream that can be used to read the buffer contents.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when T is not <see cref="byte"/>. Stream access is only supported for byte buffers.
    /// </exception>
    /// <remarks>
    /// The returned stream provides efficient direct access to the internal buffer segments
    /// without requiring a copy operation. The stream supports seeking and reading operations
    /// but does not support writing or modification operations.
    /// </remarks>
    public Stream AsStream()
    {
        if (typeof(T) != typeof(byte))
        {
            throw new InvalidOperationException($"AsStream is only supported for PooledSegmentedBuffer<byte>, but this is PooledSegmentedBuffer<{typeof(T).Name}>.");
        }

        return new PooledSegmentedBufferStream(this);
    }

    /// <summary>
    /// Copies the written data to a new T[] array.
    /// </summary>
    /// <returns>A new array containing all the written data.</returns>
    public T[] ToArray()
    {
        if (Length == 0)
        {
            return Array.Empty<T>();
        }

#if NET5_0_OR_GREATER
        // Use uninitialized array allocation for better performance
        // since we're going to overwrite all elements anyway
        var result = GC.AllocateUninitializedArray<T>(Length);
#else
        var result = new T[Length];
#endif
        CopyToArray(result.AsSpan(), 0, Length);
        return result;
    }

    /// <summary>
    /// Copies data from the internal buffers to the destination span.
    /// </summary>
    /// <param name="destination">The destination span to copy data to.</param>
    /// <param name="sourceOffset">The starting offset in the source buffer.</param>
    /// <param name="count">The number of elements to copy.</param>
    private void CopyToArray(Span<T> destination, int sourceOffset, int count)
    {
        var destinationOffset = 0;
        var remainingToCopy = count;
        var currentSourceOffset = sourceOffset;

        // Copy from completed buffers first
        if (_buffers is { Count: > 0 })
        {
#if NET5_0_OR_GREATER
            foreach (var buffer in CollectionsMarshal.AsSpan(_buffers))
#else
            foreach (var buffer in _buffers)
#endif
            {
                if (remainingToCopy <= 0 || currentSourceOffset >= buffer.Length)
                {
                    if (currentSourceOffset >= buffer.Length)
                    {
                        currentSourceOffset -= buffer.Length;
                        continue;
                    }
                    break;
                }

                var availableInBuffer = buffer.Length - currentSourceOffset;
                var elementsToCopyFromBuffer = Math.Min(remainingToCopy, availableInBuffer);

                // Direct span access - much more efficient
                buffer.Span.Slice(currentSourceOffset, elementsToCopyFromBuffer)
                    .CopyTo(destination.Slice(destinationOffset));

                destinationOffset += elementsToCopyFromBuffer;
                remainingToCopy -= elementsToCopyFromBuffer;
                currentSourceOffset = 0; // After first buffer, always start from beginning
            }
        }

        // Copy from current buffer if needed
        if (remainingToCopy > 0 && _currentBuffer is { } currentBuffer && _currentOffset > 0)
        {
            var availableInCurrentBuffer = _currentOffset - currentSourceOffset;
            if (availableInCurrentBuffer > 0)
            {
                var elementsToCopyFromCurrentBuffer = Math.Min(remainingToCopy, availableInCurrentBuffer);
                currentBuffer.AsSpan(currentSourceOffset, elementsToCopyFromCurrentBuffer).CopyTo(destination.Slice(destinationOffset));
            }
        }
    }

    /// <summary>
    /// Resets the buffer to an empty state and returns all pooled arrays back to the <see cref="ArrayPool{T}"/>.
    /// After calling this method, the buffer can be reused for new data.
    /// </summary>
    public void Clear()
    {
        Length = 0;

        if (_buffers is { })
        {
#if NET5_0_OR_GREATER
            foreach (var buffer in CollectionsMarshal.AsSpan(_buffers))
#else
            foreach (var buffer in _buffers)
#endif
            {
                ReturnBuffer(buffer);
            }

            _buffers.Clear();
        }

        if (_currentBuffer is { })
        {
            _pool.Return(_currentBuffer);
        }

        _currentBuffer = null;
        _currentOffset = 0;
    }

    /// <summary>
    /// Releases all buffers back to the pool and clears internal state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int sizeHint)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(PooledSegmentedBuffer<T>));

        if (_currentBuffer is null || _currentOffset + sizeHint >= _currentBuffer.Length)
        {
            var newSize = Math.Max(sizeHint, _minimalIncrease);
            AddNewBuffer(newSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddNewBuffer(int size)
    {
        if (_currentBuffer is { } && _currentOffset > 0)
        {
            (_buffers ??= new()).Add(_currentBuffer.AsMemory(0, _currentOffset));
        }

        _currentBuffer = _pool.Rent(size);
        _currentOffset = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnBuffer(ReadOnlyMemory<T> buffer)
    {
        MemoryMarshal.TryGetArray(buffer, out var arraySegment);
        Debug.Assert(arraySegment.Array is { });
        _pool.Return(arraySegment.Array);
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
    [DoesNotReturn]
#if NET6_OR_GREATER
    [StackTraceHidden]
#endif
#endif
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidOperationException(string message)
    {
        throw new InvalidOperationException(message);
    }

    private sealed class SequenceSegment : ReadOnlySequenceSegment<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequenceSegment(ReadOnlyMemory<T> memory)
        {
            Memory = memory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetNext(SequenceSegment next)
        {
            Next = next;
            next.RunningIndex = RunningIndex + Memory.Length;
        }
    }

    /// <summary>
    /// A read-only stream that provides direct access to the internal buffer contents without using ReadOnlySequence.
    /// </summary>
    private sealed class PooledSegmentedBufferStream : Stream
    {
        private readonly PooledSegmentedBuffer<T> _buffer;
        private long _position;
        private bool _disposed;

        public PooledSegmentedBufferStream(PooledSegmentedBuffer<T> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        public override bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !_disposed && !_buffer._disposed;
        }
        public override bool CanSeek
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !_disposed && !_buffer._disposed;
        }
        public override bool CanWrite
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => false;
        }
        public override long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _buffer.Length;
        }

        public override long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
            // No-op for read-only stream
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(PooledSegmentedBufferStream));
            ObjectDisposedException.ThrowIf(_buffer._disposed, nameof(PooledSegmentedBuffer<T>));

            ArgumentNullException.ThrowIfNull(buffer);

            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffer.Length);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - offset);

            var remainingBytes = _buffer.Length - _position;
            var bytesToRead = (int)Math.Min(count, remainingBytes);

            CopyFromBuffers(buffer.AsSpan(offset, bytesToRead), (int)_position, bytesToRead);
            _position += bytesToRead;

            return bytesToRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(cancellationToken);
            }

            try
            {
                var bytesRead = Read(buffer, offset, count);
                return Task.FromResult(bytesRead);
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

#if NET5_0_OR_GREATER
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(PooledSegmentedBufferStream));
            ObjectDisposedException.ThrowIf(_buffer._disposed, nameof(PooledSegmentedBuffer<T>));

            var remainingBytes = _buffer.Length - _position;
            var bytesToRead = (int)Math.Min(buffer.Length, remainingBytes);

            CopyFromBuffers(buffer.Slice(0, bytesToRead), (int)_position, bytesToRead);
            _position += bytesToRead;

            return bytesToRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
            }

            try
            {
                var bytesRead = Read(buffer.Span);
                return new ValueTask<int>(bytesRead);
            }
            catch (Exception ex)
            {
                return new ValueTask<int>(Task.FromException<int>(ex));
            }
        }
#endif

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(PooledSegmentedBufferStream));
            ObjectDisposedException.ThrowIf(_buffer._disposed, nameof(PooledSegmentedBuffer<T>));

            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _buffer.Length + offset,
                _ => throw new ArgumentException("Invalid seek origin.", nameof(origin))
            };

            if (newPosition < 0)
            {
                throw new IOException("Cannot seek to a negative position.");
            }

            if (newPosition > _buffer.Length)
            {
                newPosition = _buffer.Length;
            }

            _position = newPosition;
            return _position;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void SetLength(long value)
        {
            throw new NotSupportedException("Cannot set length on a read-only stream.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Cannot write to a read-only stream.");
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                base.Dispose(disposing);
            }
        }

        private void CopyFromBuffers(Span<byte> destination, int sourceOffset, int count)
        {
            var destinationOffset = 0;
            var remainingToCopy = count;
            var currentSourceOffset = sourceOffset;

            // Copy from completed buffers first
            if (_buffer._buffers is { Count: > 0 } buffers)
            {
                foreach (var bufferMemory in buffers)
                {
                    if (remainingToCopy <= 0 || currentSourceOffset >= bufferMemory.Length)
                    {
                        if (currentSourceOffset >= bufferMemory.Length)
                        {
                            currentSourceOffset -= bufferMemory.Length;
                            continue;
                        }
                        break;
                    }

                    var availableInBuffer = bufferMemory.Length - currentSourceOffset;
                    var bytesToCopyFromBuffer = Math.Min(remainingToCopy, availableInBuffer);

                    // Get the underlying array from ReadOnlyMemory<T> and cast to byte[]
                    if (MemoryMarshal.TryGetArray(bufferMemory, out var arraySegment) && arraySegment.Array is { })
                    {
                        var tArray = arraySegment.Array;
                        var byteArray = Unsafe.As<T[], byte[]>(ref tArray);
                        var sourceStart = arraySegment.Offset + currentSourceOffset;
                        byteArray.AsSpan(sourceStart, bytesToCopyFromBuffer).CopyTo(destination.Slice(destinationOffset));
                    }

                    destinationOffset += bytesToCopyFromBuffer;
                    remainingToCopy -= bytesToCopyFromBuffer;
                    currentSourceOffset = 0; // After first buffer, always start from beginning
                }
            }

            // Copy from current buffer if needed
            if (remainingToCopy > 0 && _buffer._currentBuffer is { } currentBuffer && _buffer._currentOffset > 0)
            {
                var availableInCurrentBuffer = _buffer._currentOffset - currentSourceOffset;
                if (availableInCurrentBuffer > 0)
                {
                    var bytesToCopyFromCurrentBuffer = Math.Min(remainingToCopy, availableInCurrentBuffer);

                    // Cast current buffer to byte[] since we know T is byte
                    var byteCurrentBuffer = Unsafe.As<T[], byte[]>(ref currentBuffer);
                    byteCurrentBuffer.AsSpan(currentSourceOffset, bytesToCopyFromCurrentBuffer).CopyTo(destination.Slice(destinationOffset));
                }
            }
        }
    }
}
