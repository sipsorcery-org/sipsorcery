#pragma warning disable
#nullable enable annotations
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

static partial class PolyfillExtensions
{
#if !NETSTANDARD2_1_OR_GREATER && !NETCOREAPP2_1_OR_GREATER
    extension(global::System.Net.IPAddress)
    {
        public static bool TryParse(ReadOnlySpan<char> ipString, [NotNullWhen(true)] out global::System.Net.IPAddress? address)
        {
            var ipStringStr = ipString.ToString();
            return global::System.Net.IPAddress.TryParse(ipStringStr, out address);
        }
        public static global::System.Net.IPAddress Parse(ReadOnlySpan<char> ipString)
        {
            var ipStringStr = ipString.ToString();
            return global::System.Net.IPAddress.Parse(ipStringStr);
        }
    }
#endif

#if !NET5_0_OR_GREATER
    extension(global::System.GC)
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
#endif

#if !NET8_0_OR_GREATER
    /// <summary>
    /// Parses the source <see cref="ReadOnlySpan{Char}"/> for the specified <paramref name="separator"/>, populating the <paramref name="destination"/> span
    /// with <see cref="Range"/> instances representing the regions between the separators.
    /// </summary>
    /// <param name="source">The source span to parse.</param>
    /// <param name="destination">The destination span into which the resulting ranges are written.</param>
    /// <param name="separator">A character that delimits the regions in this instance.</param>
    /// <param name="options">A bitwise combination of the enumeration values that specifies whether to trim whitespace and include empty ranges.</param>
    /// <returns>The number of ranges written into <paramref name="destination"/>.</returns>
    /// <remarks>
    /// <para>
    /// Delimiter characters are not included in the elements of the returned array.
    /// </para>
    /// <para>
    /// If the <paramref name="destination"/> span is empty, or if the <paramref name="options"/> specifies <see cref="StringSplitOptions.RemoveEmptyEntries"/> and <paramref name="source"/> is empty,
    /// or if <paramref name="options"/> specifies both <see cref="StringSplitOptions.RemoveEmptyEntries"/> and <see cref="StringSplitOptions.TrimEntries"/> and the <paramref name="source"/> is
    /// entirely whitespace, no ranges are written to the destination.
    /// </para>
    /// <para>
    /// If the span does not contain <paramref name="separator"/>, or if <paramref name="destination"/>'s length is 1, a single range will be output containing the entire <paramref name="source"/>,
    /// subject to the processing implied by <paramref name="options"/>.
    /// </para>
    /// <para>
    /// If there are more regions in <paramref name="source"/> than will fit in <paramref name="destination"/>, the first <paramref name="destination"/> length minus 1 ranges are
    /// stored in <paramref name="destination"/>, and a range for the remainder of <paramref name="source"/> is stored in <paramref name="destination"/>.
    /// </para>
    /// </remarks>
    /// <seealso href="https://github.com/dotnet/dotnet/blob/v10.0.101/src/runtime/src/libraries/System.Private.CoreLib/src/System/MemoryExtensions.cs#L5088-L5115"/>
    public static int Split(this ReadOnlySpan<char> source, Span<Range> destination, char separator, StringSplitOptions options = StringSplitOptions.None)
    {
        var count = 0;
        var start = 0;

        while (start <= source.Length)
        {
            var index = source.Slice(start).IndexOf(separator);
            var end = index != -1 ? start + index : source.Length;

            var isEmpty = end == start;
            if (options == StringSplitOptions.RemoveEmptyEntries && isEmpty)
            {
                start = end + 1;
                continue;
            }

            if (count == destination.Length - 1 && destination.Length > 0)
            {
                // Last slot gets the remainder
                destination[count] = new Range(start, source.Length);
                return destination.Length;
            }

            if (count >= destination.Length)
            {
                break;
            }

            destination[count++] = new Range(start, end);
            start = end + 1;
        }

        destination.Slice(count).Clear(); // Clear unused entries

        return count;
    }

    /// <summary>
    /// Parses the source <see cref="ReadOnlySpan{Char}"/> for one of the specified <paramref name="separators"/>, populating the <paramref name="destination"/> span
    /// with <see cref="Range"/> instances representing the regions between the separators.
    /// </summary>
    /// <param name="source">The source span to parse.</param>
    /// <param name="destination">The destination span into which the resulting ranges are written.</param>
    /// <param name="separators">Any number of strings that may delimit the regions in this instance.  If empty, all Unicode whitespace characters are used as the separators.</param>
    /// <param name="options">A bitwise combination of the enumeration values that specifies whether to trim whitespace and include empty ranges.</param>
    /// <returns>The number of ranges written into <paramref name="destination"/>.</returns>
    /// <remarks>
    /// <para>
    /// Delimiter characters are not included in the elements of the returned array.
    /// </para>
    /// <para>
    /// If the <paramref name="destination"/> span is empty, or if the <paramref name="options"/> specifies <see cref="StringSplitOptions.RemoveEmptyEntries"/> and <paramref name="source"/> is empty,
    /// or if <paramref name="options"/> specifies both <see cref="StringSplitOptions.RemoveEmptyEntries"/> and <see cref="StringSplitOptions.TrimEntries"/> and the <paramref name="source"/> is
    /// entirely whitespace, no ranges are written to the destination.
    /// </para>
    /// <para>
    /// If the span does not contain any of the <paramref name="separators"/>, or if <paramref name="destination"/>'s length is 1, a single range will be output containing the entire <paramref name="source"/>,
    /// subject to the processing implied by <paramref name="options"/>.
    /// </para>
    /// <para>
    /// If there are more regions in <paramref name="source"/> than will fit in <paramref name="destination"/>, the first <paramref name="destination"/> length minus 1 ranges are
    /// stored in <paramref name="destination"/>, and a range for the remainder of <paramref name="source"/> is stored in <paramref name="destination"/>.
    /// </para>
    /// </remarks>
    public static int SplitAny(this ReadOnlySpan<char> source, Span<Range> destination, ReadOnlySpan<char> separators, StringSplitOptions options = System.StringSplitOptions.None)
    {
        var count = 0;
        var start = 0;

        while (start <= source.Length)
        {
            var index = source.Slice(start).IndexOfAny(separators);
            var end = index != -1 ? start + index : source.Length;

            var isEmpty = end == start;
            if (options == StringSplitOptions.RemoveEmptyEntries && isEmpty)
            {
                start = end + 1;
                continue;
            }

            if (count == destination.Length - 1 && destination.Length > 0)
            {
                // Last slot gets the remainder
                destination[count] = start..source.Length;
                return destination.Length;
            }

            if (count >= destination.Length)
            {
                break;
            }

            destination[count++] = new Range(start, end);
            start = end + 1;
        }

        destination.Slice(count).Clear(); // Clear unused entries

        return count;
    }
#endif

#if !NET9_0_OR_GREATER
    /// <summary>
    /// Splits a span of elements into ranges based on a separator span.
    /// </summary>
    /// <typeparam name="T">The type of elements in the span.</typeparam>
    /// <param name="source">The span to split.</param>
    /// <param name="separator">The separator span used to delimit ranges in the source span.</param>
    /// <returns>An enumerator that iterates through the ranges in the source span.</returns>
    public static SpanSplitEnumerator<T> Split<T>(this ReadOnlySpan<T> source, ReadOnlySpan<T> separator)
        where T : IEquatable<T>?
    {
        return new SpanSplitEnumerator<T>(source, separator);
    }

    /// <summary>
    /// Splits a span of elements into ranges based on a separator element.
    /// </summary>
    /// <typeparam name="T">The type of elements in the span.</typeparam>
    /// <param name="source">The span to split.</param>
    /// <param name="separator">The separator element used to delimit ranges in the source span.</param>
    /// <returns>An enumerator that iterates through the ranges in the source span.</returns>
    public static SpanSplitEnumerator<T> Split<T>(this ReadOnlySpan<T> source, T separator)
        where T : IEquatable<T>?
    {
        return new SpanSplitEnumerator<T>(source, separator);
    }

    /// <summary>
    /// Returns a type that allows for enumeration of each element within a split span
    /// using any of the provided elements.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="source">The source span to be enumerated.</param>
    /// <param name="separators">The separators to be used to split the provided span.</param>
    /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/>.</returns>
    /// <remarks>
    /// If <typeparamref name="T"/> is <see cref="char"/> and if <paramref name="separators"/> is empty,
    /// all Unicode whitespace characters are used as the separators. This matches the behavior of when
    /// <see cref="string.Split(char[])"/> and related overloads are used with an empty separator array,
    /// or when <see cref="SplitAny(ReadOnlySpan{char}, Span{Range}, ReadOnlySpan{char}, StringSplitOptions)"/>
    /// is used with an empty separator span.
    /// </remarks>
    public static SpanSplitEnumerator<T> SplitAny<T>(this ReadOnlySpan<T> source, [UnscopedRef] params ReadOnlySpan<T> separators) where T : IEquatable<T> =>
        new SpanSplitEnumerator<T>(source, separators);

#if NET8_0_OR_GREATER
    /// <summary>
    /// Returns a type that allows for enumeration of each element within a split span
    /// using the provided <see cref="SpanSplitEnumerator{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    /// <param name="source">The source span to be enumerated.</param>
    /// <param name="separators">The <see cref="SpanSplitEnumerator{T}"/> to be used to split the provided span.</param>
    /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/>.</returns>
    /// <remarks>
    /// Unlike <see cref="SplitAny{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>, the <paramref name="separators"/> is not checked for being empty.
    /// An empty <paramref name="separators"/> will result in no separators being found, regardless of the type of <typeparamref name="T"/>,
    /// whereas <see cref="SplitAny{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/> will use all Unicode whitespace characters as separators if <paramref name="separators"/> is
    /// empty and <typeparamref name="T"/> is <see cref="char"/>.
    /// </remarks>
    public static SpanSplitEnumerator<T> SplitAny<T>(this ReadOnlySpan<T> source, SearchValues<T> separators) where T : IEquatable<T> =>
        new SpanSplitEnumerator<T>(source, separators);
#endif

    /// <summary>
    /// Enables enumerating each split within a <see cref="ReadOnlySpan{T}"/> that has been divided using one or more separators.
    /// </summary>
    /// <typeparam name="T">The type of items in the <see cref="SpanSplitEnumerator{T}"/>.</typeparam>
    public ref struct SpanSplitEnumerator<T> : IEnumerator<Range> where T : IEquatable<T>
    {
        /// <summary>The input span being split.</summary>
        private readonly ReadOnlySpan<T> _source;

        /// <summary>A single separator to use when <see cref="_splitMode"/> is <see cref="SpanSplitEnumeratorMode.SingleElement"/>.</summary>
        private readonly T _separator = default!;
        /// <summary>
        /// A separator span to use when <see cref="_splitMode"/> is <see cref="SpanSplitEnumeratorMode.Sequence"/> (in which case
        /// it's treated as a single separator) or <see cref="SpanSplitEnumeratorMode.Any"/> (in which case it's treated as a set of separators).
        /// </summary>
        private readonly ReadOnlySpan<T> _separatorBuffer;
#if NET8_0_OR_GREATER
        /// <summary>A set of separators to use when <see cref="_splitMode"/> is <see cref="SpanSplitEnumeratorMode.SearchValues"/>.</summary>
        private readonly SearchValues<T> _searchValues = default!;
#endif
        /// <summary>Mode that dictates how the instance was configured and how its fields should be used in <see cref="MoveNext"/>.</summary>
        private SpanSplitEnumeratorMode _splitMode;
        /// <summary>The inclusive starting index in <see cref="_source"/> of the current range.</summary>
        private int _startCurrent = 0;
        /// <summary>The exclusive ending index in <see cref="_source"/> of the current range.</summary>
        private int _endCurrent = 0;
        /// <summary>The index in <see cref="_source"/> from which the next separator search should start.</summary>
        private int _startNext = 0;

        /// <summary>Gets an enumerator that allows for iteration over the split span.</summary>
        /// <returns>Returns a <see cref="SpanSplitEnumerator{T}"/> that can be used to iterate over the split span.</returns>
        public SpanSplitEnumerator<T> GetEnumerator() => this;

        /// <summary>Gets the source span being enumerated.</summary>
        /// <returns>Returns the <see cref="ReadOnlySpan{T}"/> that was provided when creating this enumerator.</returns>
        public readonly ReadOnlySpan<T> Source => _source;

        /// <summary>Gets the current element of the enumeration.</summary>
        /// <returns>Returns a <see cref="Range"/> instance that indicates the bounds of the current element within the source span.</returns>
        public Range Current => new Range(_startCurrent, _endCurrent);

#if NET8_0_OR_GREATER
        /// <summary>Initializes the enumerator for <see cref="SpanSplitEnumeratorMode.SearchValues"/>.</summary>
        internal SpanSplitEnumerator(ReadOnlySpan<T> source, SearchValues<T> searchValues)
        {
            _source = source;
            _splitMode = SpanSplitEnumeratorMode.SearchValues;
            _searchValues = searchValues;
        }
#endif

        /// <summary>Initializes the enumerator for <see cref="SpanSplitEnumeratorMode.Any"/>.</summary>
        /// <remarks>
        /// If <paramref name="separators"/> is empty and <typeparamref name="T"/> is <see cref="char"/>, as an optimization
        /// it will instead use <see cref="SpanSplitEnumeratorMode.SearchValues"/> with a cached <see cref="SearchValues{Char}"/>
        /// for all whitespace characters.
        /// </remarks>
        internal SpanSplitEnumerator(ReadOnlySpan<T> source, ReadOnlySpan<T> separators)
        {
            _source = source;
            if (typeof(T) == typeof(char) && separators.Length == 0)
            {
#if NET8_0_OR_GREATER
                _searchValues = Unsafe.As<SearchValues<T>>(global::SIPSorcery.Sys.SearchValues.WhiteSpaceChars);
#else
                _separatorBuffer = Unsafe.As<T[]>(global::SIPSorcery.Sys.SearchValues.WhiteSpaceChars.ToArray());
#endif
                _splitMode = SpanSplitEnumeratorMode.SearchValues;
            }
            else
            {
                _separatorBuffer = separators;
                _splitMode = SpanSplitEnumeratorMode.Any;
            }
        }

        /// <summary>Initializes the enumerator for <see cref="SpanSplitEnumeratorMode.Sequence"/> (or <see cref="SpanSplitEnumeratorMode.EmptySequence"/> if the separator is empty).</summary>
        /// <remarks><paramref name="treatAsSingleSeparator"/> must be true.</remarks>
        internal SpanSplitEnumerator(ReadOnlySpan<T> source, ReadOnlySpan<T> separator, bool treatAsSingleSeparator)
        {
            Debug.Assert(treatAsSingleSeparator, "Should only ever be called as true; exists to differentiate from separators overload");

            _source = source;
            _separatorBuffer = separator;
            _splitMode = separator.Length == 0 ?
                SpanSplitEnumeratorMode.EmptySequence :
                SpanSplitEnumeratorMode.Sequence;
        }

        /// <summary>Initializes the enumerator for <see cref="SpanSplitEnumeratorMode.SingleElement"/>.</summary>
        internal SpanSplitEnumerator(ReadOnlySpan<T> source, T separator)
        {
            _source = source;
            _separator = separator;
            _splitMode = SpanSplitEnumeratorMode.SingleElement;
        }

        /// <summary>
        /// Advances the enumerator to the next element of the enumeration.
        /// </summary>
        /// <returns><see langword="true"/> if the enumerator was successfully advanced to the next element; <see langword="false"/> if the enumerator has passed the end of the enumeration.</returns>
        public bool MoveNext()
        {
            // Search for the next separator index.
            int separatorIndex, separatorLength;
            switch (_splitMode)
            {
                case SpanSplitEnumeratorMode.None:
                    return false;

                case SpanSplitEnumeratorMode.SingleElement:
                    separatorIndex = _source.Slice(_startNext).IndexOf(_separator);
                    separatorLength = 1;
                    break;

                case SpanSplitEnumeratorMode.Any:
                    separatorIndex = _source.Slice(_startNext).IndexOfAny(_separatorBuffer);
                    separatorLength = 1;
                    break;

                case SpanSplitEnumeratorMode.EmptySequence:
                    separatorIndex = -1;
                    separatorLength = 1;
                    break;

#if NET8_0_OR_GREATER
                case SpanSplitEnumeratorMode.Sequence:
                    separatorIndex = _source.Slice(_startNext).IndexOf(_separatorBuffer);
                    separatorLength = _separatorBuffer.Length;
                    break;

                default:
                    Debug.Assert(_splitMode == SpanSplitEnumeratorMode.SearchValues, $"Unknown split mode: {_splitMode}");
                    separatorIndex = _source.Slice(_startNext).IndexOfAny(_searchValues);
                    separatorLength = 1;
                    break;
#else
                default:
                    Debug.Assert(_splitMode == SpanSplitEnumeratorMode.Sequence, $"Unknown split mode: {_splitMode}");
                    separatorIndex = _source.Slice(_startNext).IndexOf(_separatorBuffer);
                    separatorLength = _separatorBuffer.Length;
                    break;
#endif
            }

            _startCurrent = _startNext;
            if (separatorIndex >= 0)
            {
                _endCurrent = _startCurrent + separatorIndex;
                _startNext = _endCurrent + separatorLength;
            }
            else
            {
                _startNext = _endCurrent = _source.Length;

                // Set _splitMode to None so that subsequent MoveNext calls will return false.
                _splitMode = SpanSplitEnumeratorMode.None;
            }

            return true;
        }

        /// <inheritdoc />
        object IEnumerator.Current => Current;

        /// <inheritdoc />
        void IEnumerator.Reset() => throw new NotSupportedException();

        /// <inheritdoc />
        void IDisposable.Dispose() { }
    }

    /// <summary>Indicates in which mode <see cref="SpanSplitEnumerator{T}"/> is operating, with regards to how it should interpret its state.</summary>
    private enum SpanSplitEnumeratorMode
    {
        /// <summary>Either a default <see cref="SpanSplitEnumerator{T}"/> was used, or the enumerator has finished enumerating and there's no more work to do.</summary>
        None = 0,

        /// <summary>A single T separator was provided.</summary>
        SingleElement,

        /// <summary>A span of separators was provided, each of which should be treated independently.</summary>
        Any,

        /// <summary>The separator is a span of elements to be treated as a single sequence.</summary>
        Sequence,

        /// <summary>The separator is an empty sequence, such that no splits should be performed.</summary>
        EmptySequence,

        /// <summary>
        /// A <see cref="SearchValues{Char}"/> was provided and should behave the same as with <see cref="Any"/> but with the separators in the <see cref="SearchValues"/>
        /// instance instead of in a <see cref="ReadOnlySpan{Char}"/>.
        /// </summary>
        SearchValues
    }
#endif

#if !NET6_0_OR_GREATER
    extension(global::System.Net.Sockets.Socket socket)
    {
#if !NET5_0_OR_GREATER
        public Task ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken)
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

        public ValueTask<SocketReceiveFromResult> ReceiveFromAsync(
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

        public ValueTask<int> SendToAsync(
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

        public Task DisconnectAsync(bool reuseSocket, CancellationToken cancellationToken = default)
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
    }
#endif
}

#if !NET8_0_OR_GREATER
namespace System.Threading.Tasks
{
    internal static class ConfigureAwaitOptions
    {
        public const bool None = false;
    }
}
#endif
