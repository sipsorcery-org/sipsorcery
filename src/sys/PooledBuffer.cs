using System;
using System.Buffers;
using System.Collections.Generic;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// A pooled buffer writer that implements <see cref="IBufferWriter{T}"/> using <see cref="ArrayPool{T}.Shared"/> for efficient writing of data and
    /// allows reading the written content through a <see cref="ReadOnlySequence{T}"/> using the <see cref="GetReadOnlySequence"/> method.
    /// </summary>
    /// <typeparam name="T">The type of elements in the buffer.</typeparam>
    internal sealed class PooledBuffer<T> : IBufferWriter<T>, IDisposable
    {
        private const int DefaultBufferSize = 4096;

        private readonly List<T[]> _buffers = new();
        private readonly ArrayPool<T> _pool;
        private readonly int _minimalIncrease;
        private int _currentIndex;
        private int _currentOffset;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PooledBuffer{T}"/> class with an optional minimal increase size and array pool.
        /// </summary>
        /// <param name="minimalIncrease">The minimal increase size when adding new buffers. Defaults to 4096 elements.</param>
        /// <param name="pool">The array pool to use. If null, <see cref="ArrayPool{T}.Shared"/> is used.</param>
        public PooledBuffer(int minimalIncrease = DefaultBufferSize, ArrayPool<T>? pool = null)
        {
            _minimalIncrease = minimalIncrease;
            _pool = pool ?? ArrayPool<T>.Shared;
        }

        public int Length { get; private set; }

        /// <summary>
        /// Notifies the buffer writer that <paramref name="count"/> elements were written.
        /// </summary>
        /// <param name="count">The number of elements written.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative or exceeds the current buffer capacity.</exception>
        public void Advance(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (_buffers.Count == 0)
            {
                if (count > 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }
                return;
            }

            if (_currentOffset + count > _buffers[_currentIndex].Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
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
            return _buffers[_currentIndex].AsMemory(_currentOffset);
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> buffer to write to, ensuring at least <paramref name="sizeHint"/> elements are available.
        /// </summary>
        /// <param name="sizeHint">The minimum number of elements required. May be 0.</param>
        /// <returns>A writable span buffer.</returns>
        public Span<T> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffers[_currentIndex].AsSpan(_currentOffset);
        }

        /// <summary>
        /// Writes a span of data to the buffer.
        /// </summary>
        /// <param name="source">The data to write.</param>
        public void Write(ReadOnlySpan<T> source)
        {
            if (source.IsEmpty)
                return;

            var destination = GetSpan(source.Length);
            source.CopyTo(destination);
            Advance(source.Length);
        }

        /// <summary>
        /// Writes an array of data to the buffer.
        /// </summary>
        /// <param name="source">The data to write.</param>
        public void Write(T[] source)
        {
            if (source == null || source.Length == 0)
                return;

            Write(source.AsSpan());
        }

        /// <summary>
        /// Returns a <see cref="ReadOnlySequence{T}"/> representing the written data across all buffers.
        /// </summary>
        /// <returns>A read-only sequence of elements.</returns>
        public ReadOnlySequence<T> GetReadOnlySequence()
        {
            SequenceSegment? first = null;
            SequenceSegment? last = null;

            for (var i = 0; i < _buffers.Count; i++)
            {
                var buffer = _buffers[i];
                var length = (i == _currentIndex) ? _currentOffset : buffer.Length;

                if (length == 0)
                {
                    continue;
                }

                var segment = new SequenceSegment(buffer.AsMemory(0, length));

                if (first == null)
                {
                    first = segment;
                }

                if (last != null)
                {
                    last.SetNext(segment);
                }

                last = segment;
            }

            if (first == null || last == null)
            {
                return ReadOnlySequence<T>.Empty;
            }

            return new ReadOnlySequence<T>(first, 0, last, last.Memory.Length);
        }

        public void Clear()
        {
            foreach (var buffer in _buffers)
            {
                _pool.Return(buffer);
            }

            _buffers.Clear();
            Length = 0;
            _currentIndex = 0;
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

        private void EnsureCapacity(int sizeHint)
        {
            if (_buffers.Count == 0 || _currentOffset + sizeHint > _buffers[_currentIndex].Length)
            {
                var newSize = Math.Max(sizeHint, _minimalIncrease);
                AddNewBuffer(newSize);
            }
        }

        private void AddNewBuffer(int size)
        {
            var buffer = _pool.Rent(size);
            _buffers.Add(buffer);
            _currentIndex = _buffers.Count - 1;
            _currentOffset = 0;
        }

        private class SequenceSegment : ReadOnlySequenceSegment<T>
        {
            public SequenceSegment(ReadOnlyMemory<T> memory)
            {
                Memory = memory;
            }

            public void SetNext(SequenceSegment next)
            {
                Next = next;
                next.RunningIndex = RunningIndex + Memory.Length;
            }
        }
    }
}
