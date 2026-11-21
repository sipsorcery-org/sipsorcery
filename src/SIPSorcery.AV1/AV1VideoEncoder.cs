//-----------------------------------------------------------------------------
// Filename: AV1VideoEncoder.cs
//
// Description: A realtime AV1 video encoder for SIPSorcery, built on av1rt, a
// managed port of libaom's realtime encoder.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using Av1rt;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.AV1;

/// <summary>
/// A realtime AV1 encoder for SIPSorcery, built on av1rt. Each encoded frame is one AV1 temporal
/// unit in the low overhead bitstream format, ready for SIPSorcery's AV1 RTP packetiser.
/// </summary>
/// <remarks>
/// <para>Encode only: the decode methods throw <see cref="NotSupportedException"/>.</para>
/// <para>
/// The encoder is created on the first frame and re-created, starting with a key frame, whenever the
/// frame size changes. Calls are serialised internally.
/// </para>
/// <para>
/// RGB input is converted with the matrix coefficients and range the stream signals. When the options
/// leave the colour primaries, transfer and matrix unspecified, that is BT.709, which is also what
/// browsers assume for a stream that signals no colour, and the stream signals BT.709 for all three;
/// a matrix left unspecified on its own is signalled as BT.709 too. RGB input with a matrix that is
/// not a weighting of R, G and B, such as YCgCo, is not supported. For I420 and NV12 input, set the
/// options to match the source.
/// </para>
/// <para>
/// SIPSorcery passes no timestamps, so <see cref="EncodeVideo(int, int, byte[], VideoPixelFormatsEnum, VideoCodecsEnum)"/>
/// and <see cref="EncodeVideoFaster"/> time each frame by its arrival: the rate control follows the
/// rate frames actually come in at, and the target bitrate holds whatever the source's frame rate.
/// </para>
/// </remarks>
public sealed class AV1VideoEncoder : IVideoEncoder
{
    /// <summary>The RTP payload type SIPSorcery suggests for AV1.</summary>
    public const int AV1_SUGGESTED_FORMAT_ID = 101;

    private readonly List<VideoFormat> _supportedFormats = new() { new VideoFormat(VideoCodecsEnum.AV1, AV1_SUGGESTED_FORMAT_ID) };
    private readonly Av1EncoderOptions _template;
    private readonly object _lock = new();
    private readonly ArrayBufferWriter<byte> _output = new(1 << 20);
    private readonly long _created = Stopwatch.GetTimestamp();
    private Av1Encoder? _encoder;
    // The colour _encoder signals, and the weights RGB input is converted with to match it.
    private Colour _colour;
    private RgbToYuvWeights _weights;
    private bool _forceKeyFrame;
    private int _targetBitrateKbps;
    private bool _disposed;

    /// <summary>
    /// Creates an encoder. <paramref name="options"/> supplies everything except the frame size,
    /// which is taken from each frame; null means libwebrtc's defaults (speed 11, 35 Mbps CBR).
    /// </summary>
    public AV1VideoEncoder(Av1EncoderOptions? options = null)
    {
        _template = options ?? new Av1EncoderOptions();
        _targetBitrateKbps = _template.TargetBitrateKbps;
    }

    /// <inheritdoc/>
    public List<VideoFormat> SupportedFormats => _supportedFormats;

    /// <summary>Target bitrate in kilobits per second. Changes apply from the next frame.</summary>
    public int TargetBitrateKbps
    {
        get => _targetBitrateKbps;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lock (_lock)
            {
                _targetBitrateKbps = value;
                _encoder?.SetTargetBitrate(value);
            }
        }
    }

    /// <summary>Raised after every encoded (not dropped) frame, from the encoding thread.</summary>
    public event Action<Av1EncodeResult>? FrameEncoded;

    /// <inheritdoc/>
    public void ForceKeyFrame() => _forceKeyFrame = true;

    /// <inheritdoc/>
    /// <returns>The temporal unit, or null if the rate control dropped the frame.</returns>
    public byte[]? EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
    {
        ArgumentNullException.ThrowIfNull(sample);
        CheckCodec(codec);
        lock (_lock)
        {
            _output.ResetWrittenCount();
            Av1EncodeResult result = EncodeVideo(width, height, sample, I420Converter.PackedStride(pixelFormat, width), pixelFormat,
                                                 _output, ArrivalTime());
            return result.Dropped ? null : _output.WrittenSpan.ToArray();
        }
    }

    /// <inheritdoc/>
    /// <returns>The temporal unit, or null if the rate control dropped the frame.</returns>
    public byte[]? EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec)
    {
        ArgumentNullException.ThrowIfNull(rawImage);
        if (rawImage.Sample == IntPtr.Zero)
        {
            throw new ArgumentException("The image has no pixel data.", nameof(rawImage));
        }
        CheckCodec(codec);
        int stride = rawImage.Stride > 0 ? rawImage.Stride : I420Converter.PackedStride(rawImage.PixelFormat, rawImage.Width);
        if (stride < I420Converter.MinStride(rawImage.PixelFormat, rawImage.Width))
        {
            throw new ArgumentException($"A stride of {stride} is too small for a {rawImage.Width} pixel wide {rawImage.PixelFormat} frame.", nameof(rawImage));
        }
        lock (_lock)
        {
            _output.ResetWrittenCount();
            Av1EncodeResult result = EncodeCore(rawImage.Width, rawImage.Height, default, rawImage.Sample, stride,
                                                rawImage.PixelFormat, _output, ArrivalTime());
            return result.Dropped ? null : _output.WrittenSpan.ToArray();
        }
    }

    /// <summary>
    /// Encodes a frame into a caller-supplied writer without allocating a result array. The picture
    /// is converted straight into the encoder's input buffer, by several threads.
    /// </summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="sample">The picture; for I420 and NV12 the chroma plane(s) follow the luma rows.</param>
    /// <param name="stride">Bytes between rows of the luma plane or of the packed pixels; 0 for tightly packed.</param>
    /// <param name="pixelFormat">I420, NV12, BGRA, RGBA, BGR or RGB.</param>
    /// <param name="output">Receives the temporal unit.</param>
    /// <param name="timestamp">
    /// Presentation (capture) time in 90 kHz units, or -1 for the nominal frame rate; see
    /// <see cref="Av1Encoder.Encode(in I420Frame, IBufferWriter{byte}, long)"/>.
    /// </param>
    /// <returns>What was encoded; <see cref="Av1EncodeResult.Dropped"/> if nothing was written.</returns>
    public Av1EncodeResult EncodeVideo(int width, int height, ReadOnlyMemory<byte> sample, int stride,
                                       VideoPixelFormatsEnum pixelFormat, IBufferWriter<byte> output, long timestamp = -1)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (stride <= 0)
        {
            stride = I420Converter.PackedStride(pixelFormat, width);
        }
        if (stride < I420Converter.MinStride(pixelFormat, width))
        {
            throw new ArgumentException($"A stride of {stride} is too small for a {width} pixel wide {pixelFormat} frame.", nameof(stride));
        }
        if (sample.Length < I420Converter.SourceSize(pixelFormat, stride, width, height))
        {
            throw new ArgumentException($"The sample is too small for a {width}x{height} {pixelFormat} frame.", nameof(sample));
        }
        lock (_lock)
        {
            return EncodeCore(width, height, sample, IntPtr.Zero, stride, pixelFormat, output, timestamp);
        }
    }

    // Now, in 90 kHz units since the encoder was created.
    private long ArrivalTime() => Stopwatch.GetElapsedTime(_created).Ticks * 9 / 1000;

    // The picture comes from `sample`, or from `native` when that is not zero.
    private Av1EncodeResult EncodeCore(int width, int height, ReadOnlyMemory<byte> sample, IntPtr native, int stride,
                                       VideoPixelFormatsEnum pixelFormat, IBufferWriter<byte> output, long timestamp)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Av1Encoder encoder = EnsureEncoder(width, height, pixelFormat);
        Av1InputFrame input = encoder.GetInputFrame();
        // Converted by the encoder's own threads, which are idle between frames.
        if (native != IntPtr.Zero)
        {
            I420Converter.Convert(native, stride, pixelFormat, _weights, input, encoder);
        }
        else
        {
            I420Converter.Convert(sample, stride, pixelFormat, _weights, input, encoder);
        }

        if (_forceKeyFrame)
        {
            _forceKeyFrame = false;
            encoder.ForceKeyFrame();
        }
        Av1EncodeResult result = encoder.EncodeInputFrame(output, timestamp);
        if (!result.Dropped)
        {
            FrameEncoded?.Invoke(result);
        }
        return result;
    }

    private static void CheckCodec(VideoCodecsEnum codec)
    {
        if (codec != VideoCodecsEnum.AV1)
        {
            throw new NotSupportedException($"This encoder only produces AV1, not {codec}.");
        }
    }

    /// <summary>Not supported: this is an encoder only.</summary>
    public IEnumerable<VideoSample> DecodeVideo(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec) =>
        throw new NotSupportedException("SIPSorcery.AV1 only encodes; use another component to decode AV1.");

    /// <summary>Not supported: this is an encoder only.</summary>
    public IEnumerable<RawImage> DecodeVideoFaster(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec) =>
        throw new NotSupportedException("SIPSorcery.AV1 only encodes; use another component to decode AV1.");

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _encoder?.Dispose();
            _encoder = null;
        }
    }

    private Av1Encoder EnsureEncoder(int width, int height, VideoPixelFormatsEnum pixelFormat)
    {
        var o = _template;
        Colour colour = Colour.Of(o);
        if (I420Converter.IsRgb(pixelFormat))
        {
            colour = Colour.ForRgb(o);
            if (!RgbToYuvWeights.TryGet(colour.Matrix, o.FullRange, out _weights))
            {
                throw new NotSupportedException($"RGB input can't be converted for the matrix coefficients {colour.Matrix}. " +
                                                "Use I420 or NV12 input, or BT.709, BT.601, BT.470BG, FCC, SMPTE 240M or BT.2020 NCL.");
            }
        }
        if (_encoder != null && _encoder.Width == width && _encoder.Height == height && _colour == colour)
        {
            return _encoder;
        }

        _encoder?.Dispose();
        _encoder = null;
        _encoder = new Av1Encoder(new Av1EncoderOptions
        {
            Width = width, Height = height, FrameRate = o.FrameRate, TargetBitrateKbps = _targetBitrateKbps,
            Speed = o.Speed, Threads = o.Threads, TileColumnsLog2 = o.TileColumnsLog2, TileRowsLog2 = o.TileRowsLog2,
            Superblock = o.Superblock, MinQuantizer = o.MinQuantizer, MaxQuantizer = o.MaxQuantizer,
            FrameDropThreshold = o.FrameDropThreshold, KeyFrameInterval = o.KeyFrameInterval,
            EnableCdef = o.EnableCdef, EnableCyclicRefresh = o.EnableCyclicRefresh,
            ColorPrimaries = colour.Primaries, TransferCharacteristics = colour.Transfer, MatrixCoefficients = colour.Matrix,
            FullRange = o.FullRange,
        });
        _colour = colour;
        return _encoder;
    }

    // The colour description a stream signals.
    private readonly record struct Colour(Av1ColorPrimaries Primaries, Av1TransferCharacteristics Transfer, Av1MatrixCoefficients Matrix)
    {
        public static Colour Of(Av1EncoderOptions o) => new(o.ColorPrimaries, o.TransferCharacteristics, o.MatrixCoefficients);

        // For RGB input: BT.709 for whatever the options leave unspecified, primaries and transfer
        // only if the options specify no colour at all.
        public static Colour ForRgb(Av1EncoderOptions o)
        {
            Colour colour = Of(o);
            if (colour == new Colour(Av1ColorPrimaries.Unspecified, Av1TransferCharacteristics.Unspecified, Av1MatrixCoefficients.Unspecified))
            {
                return new(Av1ColorPrimaries.Bt709, Av1TransferCharacteristics.Bt709, Av1MatrixCoefficients.Bt709);
            }
            return colour.Matrix == Av1MatrixCoefficients.Unspecified ? colour with { Matrix = Av1MatrixCoefficients.Bt709 } : colour;
        }
    }
}
