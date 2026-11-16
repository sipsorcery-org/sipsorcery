//-----------------------------------------------------------------------------
// Filename: EncodedVideoSampleEvents.cs
//
// Description: The two encoded sample events of a video source, one raising
// each frame in an array and one as a span, with the buffer frames are encoded
// into for the span event.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 16 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;

namespace SIPSorceryMedia.Abstractions;

/// <summary>
/// The encoded sample events of a video source that implements <see cref="IVideoSourceSpan"/>, with the buffer each
/// frame is encoded into for the span event. The source forwards its
/// <see cref="IVideoSource.OnVideoSourceEncodedSample"/> and <see cref="IVideoSourceSpan.OnVideoSourceEncodedSampleFaster"/>
/// to an instance of this class and raises every frame through it:
/// <list type="bullet">
/// <item><description>When the span event has subscribers, <see cref="EncodeAndRaise{TState}"/> encodes the frame into
/// a buffer re-used from frame to frame and raises it there on the span event, and as a copy, which its subscribers
/// then own, on the array event.</description></item>
/// <item><description>Otherwise <see cref="Raise"/> raises a frame the source has encoded into an array of its
/// own.</description></item>
/// </list>
/// Either way each frame is encoded once, whichever events have subscribers.
/// </summary>
public sealed class EncodedVideoSampleEvents
{
    private readonly ReusedBufferWriter _frameBuffer = new ReusedBufferWriter();
    private readonly object _frameBufferLock = new object();

    // Set while a frame is being raised, so a frame that one of its handlers pushes meanwhile is noticed. Only used
    // under _frameBufferLock.
    private bool _raising;

    /// <summary>
    /// Raised with every encoded frame in an array the subscribers own. See
    /// <see cref="IVideoSource.OnVideoSourceEncodedSample"/>.
    /// </summary>
    public event EncodedSampleDelegate OnVideoSourceEncodedSample;

    /// <summary>
    /// Raised with every encoded frame as a span, only valid for the duration of the handler. See
    /// <see cref="IVideoSourceSpan.OnVideoSourceEncodedSampleFaster"/>.
    /// </summary>
    public event EncodedSampleFasterDelegate OnVideoSourceEncodedSampleFaster;

    /// <summary>True if either event has a subscriber.</summary>
    public bool HasSubscribers => OnVideoSourceEncodedSample != null || OnVideoSourceEncodedSampleFaster != null;

    /// <summary>
    /// True if the span event has a subscriber, in which case frames are best encoded with
    /// <see cref="EncodeAndRaise{TState}"/>, so they are not encoded into an array of their own.
    /// </summary>
    public bool HasSpanSubscribers => OnVideoSourceEncodedSampleFaster != null;

    /// <summary>
    /// Raises a frame encoded into an array the caller hands over: on the span event over the array, then on the array
    /// event with the array itself.
    /// </summary>
    /// <param name="durationRtpUnits">The duration of the frame in RTP timestamp units.</param>
    /// <param name="sample">The encoded frame. The array event's subscribers own it once it is raised.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sample"/> is null.</exception>
    public void Raise(uint durationRtpUnits, byte[] sample)
    {
        if (sample == null)
        {
            throw new ArgumentNullException(nameof(sample));
        }

        // Note the event handlers can be removed while the encoding is in progress.
        OnVideoSourceEncodedSampleFaster?.Invoke(durationRtpUnits, sample);
        OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, sample);
    }

    /// <summary>
    /// Encodes a frame into the buffer re-used from frame to frame and raises it: in place on the span event, then as a
    /// copy on the array event.
    /// </summary>
    /// <typeparam name="TState">What <paramref name="encode"/> needs to encode the frame.</typeparam>
    /// <param name="durationRtpUnits">The duration of the frame in RTP timestamp units.</param>
    /// <param name="state">Passed to <paramref name="encode"/>.</param>
    /// <param name="encode">Encodes the frame into the writer it is given and returns true, or returns false if there
    /// is no frame to raise, for example because the encoder holds it back, in which case anything written is
    /// discarded. A lambda that takes what it needs from <paramref name="state"/> rather than capturing it does not
    /// allocate per call.</param>
    /// <returns>The length of the frame raised, or 0 if <paramref name="encode"/> returned false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="encode"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Frames are encoded and raised one at a time. An exception from <paramref name="encode"/> is passed on before
    /// anything is raised, and an exception from a handler stops the handlers after it and is passed on, as for any
    /// event.
    /// </para>
    /// <para>
    /// While a frame is raised its lock is held, so a frame pushed meanwhile comes from one of its handlers, on the
    /// same thread, as a relay might do. That frame is encoded into a buffer of its own and raised in full first, so
    /// the handlers still to run get the frame they were raised with.
    /// </para>
    /// </remarks>
    public int EncodeAndRaise<TState>(uint durationRtpUnits, TState state, Func<TState, IBufferWriter<byte>, bool> encode)
    {
        if (encode == null)
        {
            throw new ArgumentNullException(nameof(encode));
        }

        lock (_frameBufferLock)
        {
            var output = _raising ? new ReusedBufferWriter() : _frameBuffer;
            output.Reset();

            if (!encode(state, output))
            {
                return 0;
            }

            ReadOnlySpan<byte> frame = output.WrittenSpan;
            bool wasRaising = _raising;
            _raising = true;

            try
            {
                // Note the event handlers can be removed while the encoding is in progress.
                OnVideoSourceEncodedSampleFaster?.Invoke(durationRtpUnits, frame);

                // Subscribers to the array event own what they are given, so they get a copy.
                OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, frame.ToArray());
            }
            finally
            {
                _raising = wasRaising;
            }

            return frame.Length;
        }
    }

    /// <summary>
    /// A buffer writer that keeps what is written in one array, re-used from frame to frame. ArrayBufferWriter is not
    /// available on every target framework of this library, and where it is, resetting it without clearing the
    /// memory is only possible from .NET 8.
    /// </summary>
    private sealed class ReusedBufferWriter : IBufferWriter<byte>
    {
        // The largest length of a byte array, Array.MaxLength, which not every target framework of this library has.
        private const int MAX_ARRAY_LENGTH = 0x7FFFFFC7;

        private byte[] _buffer = Array.Empty<byte>();
        private int _written;

        /// <summary>The data written since the last <see cref="Reset"/>.</summary>
        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _written);

        /// <summary>Discards what has been written, keeping the array for the next frame.</summary>
        public void Reset() => _written = 0;

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _written)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Grow(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Grow(sizeHint);
            return _buffer.AsSpan(_written);
        }

        // Makes room after what has been written for at least sizeHint bytes, or one byte when no size is given. It
        // can replace _buffer, so callers read _buffer only after it returns.
        private void Grow(int sizeHint)
        {
            if (sizeHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            }

            int needed = Math.Max(sizeHint, 1);
            if (_buffer.Length - _written < needed)
            {
                long required = (long)_written + needed;
                if (required > MAX_ARRAY_LENGTH)
                {
                    throw new OutOfMemoryException($"An encoded frame of {required} bytes would not fit in an array.");
                }

                Array.Resize(ref _buffer, (int)Math.Min(Math.Max(required, 2L * _buffer.Length), MAX_ARRAY_LENGTH));
            }
        }
    }
}
