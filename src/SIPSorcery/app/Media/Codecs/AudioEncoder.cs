//-----------------------------------------------------------------------------
// Filename: AudioEncoder.cs
//
// Description: Audio codecs for the simpler codecs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using CommunityToolkit.HighPerformance.Buffers;
using Concentus;
using Concentus.Enums;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media;

public class AudioEncoder : IAudioEncoder, IDisposable
{
    private const int G722_BIT_RATE = 64000;              // G722 sampling rate is 16KHz with bits per sample of 16.
    private const int OPUS_SAMPLE_RATE = 48000;           // Opus codec sampling rate, 48KHz.
    private const int OPUS_CHANNELS = 2;                  // Opus codec number of channels.
    private const int OPUS_STREAM_CHANNELS = 1;           // The encoded stream is mono, matching the library's PCM convention.

    /// <summary>
    /// The max frame size that the OPUS encoder will accept is 2880 bytes (see IOpusEncoder.Encode). 2880 corresponds
    /// to a sample size of 30ms for a single channel at 48Khz with 16 bit PCM. Therefore the max sample size supported
    /// by OPUS is 30ms.
    /// </summary>
    private const int OPUS_MAXIMUM_INPUT_SAMPLES_PER_CHANNEL = 2880;

    /// <summary>
    /// OPUS max encode size (see IOpusEncoder.Encode).
    /// </summary>
    private const int OPUS_MAXIMUM_ENCODED_FRAME_SIZE = 1275;

    private static readonly ILogger logger = LogFactory.CreateLogger<AudioEncoder>();

    private bool _disposedValue;

    private sealed class G722CodecContext
    {
        public G722Codec? Codec { get; init; }
        public G722CodecState? State { get; init; }
    }

    private G722CodecContext? _g722CodecContext;
    private G722CodecContext? _g722DecoderContext;

    // Guards the lazy initialisation of the G722 codec/state pairs above. The pairs are two
    // separate fields, so without this an unlucky caller could observe the codec already set
    // while its state is still null and pass that null into Encode/Decode (a sporadic
    // NullReferenceException on the first G722 frame when two audio source timers encode at once).
    private readonly object _g722Lock = new object();

    private G729Encoder? _g729Encoder;
    private G729Decoder? _g729Decoder;

    private IOpusDecoder? _opusDecoder;
    private IOpusEncoder? _opusEncoder;

    private static readonly ReadOnlyMemory<AudioFormat> _linearFormats = new AudioFormat[]
    {
        new AudioFormat(AudioCodecsEnum.L16, 117, 16000),
        new AudioFormat(AudioCodecsEnum.L16, 118, 8000),

        // Not recommended due to very, very crude up-sampling in AudioEncoder class. PR's welcome :).
        //new AudioFormat(121, "L16", "L16/48000", null),
    };

    private List<AudioFormat> _supportedFormats = new List<AudioFormat>
    {
        new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
        new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
        new AudioFormat(SDPWellKnownMediaFormatsEnum.G722),
        new AudioFormat(SDPWellKnownMediaFormatsEnum.G729),

        // Need more testing befoer adding OPUS by default. 24 Dec 2024 AC.
        //new AudioFormat(111, nameof(AudioCodecsEnum.OPUS), OPUS_SAMPLE_RATE, OPUS_CHANNELS, "useinbandfec=1")
        // AudioCommonlyUsedFormats.OpusWebRTC
    };

    public List<AudioFormat> SupportedFormats
    {
        get => _supportedFormats;
    }

    /// <summary>
    /// Creates a new audio encoder instance.
    /// </summary>
    /// <param name="includeLinearFormats">
    /// If set to true the linear audio formats will be added to the list of supported formats. The reason they are only
    /// included if explicitly requested is they are not very popular for other VoIP systems and therefore needlessly
    /// pollute the SDP.
    /// </param>
    public AudioEncoder(bool includeLinearFormats = false, bool includeOpus = false)
    {
        if (includeLinearFormats)
        {
            _supportedFormats.AddRange(_linearFormats.Span);
        }

        if (includeOpus)
        {
            _supportedFormats.Add(AudioCommonlyUsedFormats.OpusWebRTC);
        }
    }

    public AudioEncoder(params AudioFormat[] supportedFormats)
    {
        _supportedFormats = [.. supportedFormats];
    }

    public void EncodeAudio(ReadOnlySpan<short> pcm, AudioFormat format, IBufferWriter<byte> destination)
    {
        switch (format.Codec)
        {
            case AudioCodecsEnum.G722:
                {
                    if (_g722CodecContext is not { Codec: { } g722Encoder, State: { } g722EncoderState })
                    {
                        g722Encoder = new G722Codec();
                        g722EncoderState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                        _g722CodecContext = new()
                        {
                            Codec = g722Encoder,
                            State = g722EncoderState
                        };
                    }
                    Debug.Assert(g722Encoder is not null);
                    Debug.Assert(g722EncoderState is not null);
                    var outputBufferSize = pcm.Length / 2;
                    var encodedSpan = destination.GetSpan(outputBufferSize);
                    var res = g722Encoder.Encode(g722EncoderState, encodedSpan, pcm);
                    destination.Advance(res);
                }
                break;
            case AudioCodecsEnum.G729:
                {
                    if (_g729Encoder is null)
                    {
                        _g729Encoder = new G729Encoder();
                    }
                    Debug.Assert(_g729Encoder is { });
                    var speech = MemoryMarshal.AsBytes(pcm);
                    _g729Encoder.Process(speech, destination);
                }
                break;
            case AudioCodecsEnum.PCMA:
                {
                    var encoded = destination.GetSpan(pcm.Length);
                    for (var i = 0; i < pcm.Length; i++)
                    {
                        encoded[i] = ALawEncoder.LinearToALawSample(pcm[i]);
                    }
                    destination.Advance(encoded.Length);
                }
                break;
            case AudioCodecsEnum.PCMU:
                {
                    var encoded = destination.GetSpan(pcm.Length);
                    for (var i = 0; i < pcm.Length; i++)
                    {
                        encoded[i] = MuLawEncoder.LinearToMuLawSample(pcm[i]);
                    }
                    destination.Advance(encoded.Length);
                }
                break;
            case AudioCodecsEnum.L16:
                {
                    // Put on the wire in network byte order (big endian).
                    var encoded = MemoryMarshal.Cast<short, byte>(pcm);
                    encoded.CopyTo(destination.GetSpan(encoded.Length));
                    destination.Advance(encoded.Length);
                }
                break;
            case AudioCodecsEnum.PCM_S16LE:
                {
                    // Put on the wire as little endian.
                    var length = pcm.Length / 2;
                    var encoded = destination.GetSpan(length);
                    MemoryOperations.ToLittleEndianBytes(pcm, encoded);
                    destination.Advance(length);
                }
                break;
            case AudioCodecsEnum.OPUS:
                {
                    if (_opusEncoder is null)
                    {
                        // The PCM convention throughout this library is mono. The OPUS SDP format is
                        // always "opus/48000/2" (RFC 7587) but that is a fixed wire-format declaration,
                        // NOT the stream channel count, which is signalled in-band in each OPUS packet.
                        // Creating the encoder from the format's channel count and feeding it mono PCM
                        // makes it consume every two mono samples as one stereo frame, halving the
                        // duration and doubling the pitch of the audio.
                        _opusEncoder = OpusCodecFactory.CreateEncoder(format.ClockRate, OPUS_STREAM_CHANNELS, OpusApplication.OPUS_APPLICATION_VOIP);
                    }

                    Debug.Assert(_opusEncoder is { });

                    if (pcm.Length > _opusEncoder.NumChannels * OPUS_MAXIMUM_INPUT_SAMPLES_PER_CHANNEL)
                    {
                        logger.LogSettingAudioFormatWarning(nameof(AudioEncoder), pcm.Length, _opusEncoder.NumChannels * OPUS_MAXIMUM_INPUT_SAMPLES_PER_CHANNEL);
                    }
                    else
                    {
                        var encodedSample = destination.GetSpan(OPUS_MAXIMUM_ENCODED_FRAME_SIZE);
                        var encodedLength = _opusEncoder.Encode(pcm, pcm.Length / _opusEncoder.NumChannels, encodedSample, encodedSample.Length);
                        destination.Advance(encodedLength);
                    }
                }
                break;
            default:
                throw new SipSorceryException($"Audio format {format.Codec} cannot be encoded.");
        }
    }

    /// <summary>
    /// Decodes to 16bit signed PCM samples.
    /// </summary>
    /// <param name="encodedSample">The span containing the encoded sample.</param>
    /// <param name="format">The audio format of the encoded sample.</param>
    /// <param name="destination">
    /// A <see cref="IBufferWriter{T}"/> of <see langword="short"/> to receive the decoded PCM samples.
    /// </param>
    public void DecodeAudio(ReadOnlySpan<byte> encodedSample, AudioFormat format, IBufferWriter<short> destination)
    {
        switch (format.Codec)
        {
            case AudioCodecsEnum.G722:
                {
                    // Same ordered, double-checked init as EncodeAudio: never pass a null state to Decode.
                    if (_g722DecoderContext is not { Codec: { } g722Decoder, State: { } g722DecoderState })
                    {
                        g722Decoder = new G722Codec();
                        g722DecoderState = new G722CodecState(G722_BIT_RATE, G722Flags.None);
                        _g722DecoderContext = new()
                        {
                            Codec = g722Decoder,
                            State = g722DecoderState
                        };
                    }

                    Debug.Assert(g722Decoder is { });
                    Debug.Assert(g722DecoderState is { });

                    // Use the new IBufferWriter-based decode method directly
                    g722Decoder.Decode(g722DecoderState, destination, encodedSample);
                }

                break;
            case AudioCodecsEnum.G729:
                {
                    if (_g729Decoder is null)
                    {
                        _g729Decoder = new G729Decoder();
                    }

                    // Use the new span-based decode method directly
                    using var buffer = new ArrayPoolBufferWriter<byte>(8192);
                    _g729Decoder.Process(encodedSample, buffer);
                    destination.Write(MemoryMarshal.Cast<byte, short>(buffer.WrittenSpan));
                }

                break;
            case AudioCodecsEnum.PCMA:
                {
                    var outputSpan = destination.GetSpan(encodedSample.Length);
                    for (var i = 0; i < encodedSample.Length; i++)
                    {
                        outputSpan[i] = ALawDecoder.ALawToLinearSample(encodedSample[i]);
                    }
                    destination.Advance(encodedSample.Length);
                }

                break;
            case AudioCodecsEnum.PCMU:
                {
                    var outputSpan = destination.GetSpan(encodedSample.Length);
                    for (var i = 0; i < encodedSample.Length; i++)
                    {
                        outputSpan[i] = MuLawDecoder.MuLawToLinearSample(encodedSample[i]);
                    }
                    destination.Advance(encodedSample.Length);
                }

                break;
            case AudioCodecsEnum.L16:
                {
                    // Samples are on the wire as big endian.
                    var sampleCount = encodedSample.Length / 2;
                    var outputSpan = destination.GetSpan(sampleCount);
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var byteIndex = i * 2;
                        outputSpan[i] = (short)(encodedSample[byteIndex] << 8 | encodedSample[byteIndex + 1]);
                    }
                    destination.Advance(sampleCount);
                }

                break;
            case AudioCodecsEnum.PCM_S16LE:
                {
                    // Samples are on the wire as little endian.
                    var sampleCount = encodedSample.Length / 2;
                    var outputSpan = destination.GetSpan(sampleCount);
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var byteIndex = i * 2;
                        outputSpan[i] = (short)(encodedSample[byteIndex + 1] << 8 | encodedSample[byteIndex]);
                    }
                    destination.Advance(sampleCount);
                }

                break;
            case AudioCodecsEnum.OPUS:
                {
                    if (_opusDecoder is null)
                    {
                        // See the comment in EncodeAudio: the library PCM convention is mono and the
                        // SDP channel count for OPUS does not describe the stream. A mono decoder
                        // downmixes any stereo stream, whereas a stereo decoder would upmix mono
                        // streams to interleaved stereo and feed double-length samples into a
                        // pipeline (resampler, sinks) that assumes mono.
                        _opusDecoder = OpusCodecFactory.CreateDecoder(format.ClockRate, OPUS_STREAM_CHANNELS);
                    }

                    var maxSamples = OPUS_MAXIMUM_INPUT_SAMPLES_PER_CHANNEL * _opusDecoder.NumChannels;

                    var outputSpan = destination.GetSpan(maxSamples);

                    var samplesPerChannel = _opusDecoder.Decode(
                        encodedSample,
                        outputSpan,
                        maxSamples,
                        false);

                    var totalSamples = samplesPerChannel * _opusDecoder.NumChannels;

                    if (totalSamples > 0)
                    {
                        destination.Advance(totalSamples);
                    }
                }

                break;
            default:
                throw new SipSorceryException($"Audio format {format.Codec} cannot be decoded.");
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                (_opusEncoder as IDisposable)?.Dispose();
                (_opusDecoder as IDisposable)?.Dispose();
                (_g729Encoder as IDisposable)?.Dispose();
                (_g729Decoder as IDisposable)?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
