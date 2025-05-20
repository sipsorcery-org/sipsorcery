//-----------------------------------------------------------------------------
// Filename: SequenceReader.cs
//
// Description: Helper methods.
//
// Author(s):
// Aaron Clauson
//
// History:
// ??	Aaron Clauson	Created.
// 21 Jan 2020  Aaron Clauson   Added HexStr and ParseHexStr (borrowed from
//                              Bitcoin Core source).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;

#if NETFRAMEWORK || NETSTANDARD2_0
namespace System.Buffers;

public ref partial struct SequenceReader<T> where T : unmanaged, IEquatable<T>
{
    private readonly ReadOnlySequence<T> _sequence;
    private SequencePosition _currentPosition;
    private ReadOnlyMemory<T> _currentMemory;
#pragma warning disable CS0414
    private int _currentIndex;
#pragma warning restore CS0414
    private long _consumed;
    private readonly long _length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SequenceReader(ReadOnlySequence<T> sequence)
    {
        _sequence = sequence;
        _currentPosition = sequence.Start;
        _currentMemory = default;
        _currentIndex = 0;
        _consumed = 0;
        _length = -1;

        if (!sequence.IsEmpty)
        {
            if (sequence.TryGet(ref _currentPosition, out _currentMemory))
            {
                _currentIndex = 0;
            }
        }
    }

    private readonly long Length
    {
        get
        {
            if (_length < 0)
            {
                // Cast-away readonly to initialize lazy field
                Unsafe.AsRef(in _length) = _sequence.Length;
            }
            return _length;
        }
    }
    
    public readonly long Remaining => Length - _consumed;

    public readonly long Consumed => _consumed;

    public readonly SequencePosition Position => _sequence.GetPosition(_consumed);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool TryCopyTo(Span<T> destination)
    {
        if (Remaining < destination.Length)
        {
            return false;
        }

        if (destination.Length == 0)
        {
            return true;
        }

        // Create a slice from current position with the required length
        var sourceSlice = _sequence.Slice(_consumed, destination.Length);
        
        // Copy manually to avoid ambiguity
        CopySequenceToSpan(sourceSlice, destination);
        
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopySequenceToSpan(ReadOnlySequence<T> source, Span<T> destination)
    {
        if (source.IsSingleSegment)
        {
            source.First.Span.CopyTo(destination);
        }
        else
        {
            var destinationIndex = 0;
            foreach (var segment in source)
            {
                var segmentSpan = segment.Span;
                segmentSpan.CopyTo(destination.Slice(destinationIndex));
                destinationIndex += segmentSpan.Length;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        var maxAdvance = Length - _consumed;
        
        if (count > maxAdvance)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"Cannot advance {count} elements when only {maxAdvance} elements remain in sequence.");
        }

        _consumed += count;
        
        // Update current position for efficient slicing
        if (_consumed >= Length)
        {
            _currentPosition = _sequence.End;
            _currentMemory = default;
            _currentIndex = 0;
        }
        else
        {
            _currentPosition = _sequence.GetPosition(_consumed);
            if (!_sequence.TryGet(ref _currentPosition, out _currentMemory))
            {
                _currentMemory = default;
                _currentIndex = 0;
            }
            else
            {
                _currentIndex = 0;
            }
        }
    }
}

/// <summary>
/// Extension methods for ReadOnlySequence&lt;T&gt; for older .NET versions.
/// </summary>
internal static class ReadOnlySequenceExtensions
{
    /// <summary>
    /// Copies the <see cref="ReadOnlySequence{T}"/> to the specified <see cref="Span{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the items in the <see cref="ReadOnlySequence{T}"/>.</typeparam>
    /// <param name="source">The source <see cref="ReadOnlySequence{T}"/>.</param>
    /// <param name="destination">The destination <see cref="Span{T}"/>.</param>
    public static void _CopyTo<T>(this in ReadOnlySequence<T> source, Span<T> destination)
    {
        if (source.IsSingleSegment)
        {
            source.First.Span.CopyTo(destination);
        }
        else
        {
            var destinationIndex = 0;
            foreach (var segment in source)
            {
                var segmentSpan = segment.Span;
                segmentSpan.CopyTo(destination.Slice(destinationIndex));
                destinationIndex += segmentSpan.Length;
            }
        }
    }
}
#endif
