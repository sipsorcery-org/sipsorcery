//-----------------------------------------------------------------------------
// Filename: VP8Codec.cs
//
// Description: Implements a VP8 video encoder and decoder.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 05 Nov 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace Vpx.Net
{
    public class VP8Codec : IVideoEncoder, IDisposable
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<VP8Codec>();

        private static readonly List<VideoFormat> _supportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, 100)
        };

        public List<VideoFormat> SupportedFormats
        {
            get { return _supportedFormats; }
        }

        //private Vp8Codec _vp8Encoder;
        private vpx_codec_ctx_t _vp8Decoder;
        private bool _forceKeyFrame = false;
        private Object _decoderLock = new object();
        private Object _encoderLock = new object();

        /// <summary>
        /// Creates a new video encoder can encode and decode samples.
        /// </summary>
        public VP8Codec()
        { }

        // Pooled per-instance plane-split buffers, reused across calls.
        // Resized lazily when the input dimensions change. Allocating these
        // afresh each frame was a major chunk of the encoder's GC pressure
        // (~440 KB per 640x480 frame).
        private byte[] _srcY;
        private byte[] _srcU;
        private byte[] _srcV;

        public void ForceKeyFrame() => _forceKeyFrame = true;
        public bool IsSupported(VideoCodecsEnum codec) => codec == VideoCodecsEnum.VP8;

        // Default base quantizer for the foundation encoder. Keyframes only;
        // there is no rate control, so this is a fixed Q. Mid-quality.
        private const int DEFAULT_BASE_QINDEX = 32;

        private int _baseQIndex = DEFAULT_BASE_QINDEX;

        // Per-codec-instance scratch buffers used by frame_encoder. Holds
        // the LAST_FRAME reference between key/inter calls; instance-scoped
        // (vs ThreadStatic) so that calls dispatched onto different
        // thread-pool threads -- a Timer callback in particular -- still
        // share the same reference frame.
        private readonly FrameEncoderBuffers _frameBuffers = new FrameEncoderBuffers();

        /// <summary>
        /// VP8 base quantizer index used for every frame. Range [0, 127];
        /// 0 is highest quality / largest frames, 127 is lowest quality /
        /// smallest frames. Default is 32 (mid-quality).
        ///
        /// Tuning this is the primary lever for trading bitrate against
        /// visible quality on this foundation encoder, since the encoder
        /// is keyframe-only with no rate control. As a rough guide on
        /// 640x480 high-detail content (e.g. the standard test pattern):
        ///   Q=32 -> ~50 KB/frame (~12 Mbps at 30 fps) — too high for
        ///                         most receivers' un-paced burst
        ///                         tolerance.
        ///   Q=64 -> ~28 KB/frame (~6.7 Mbps at 30 fps).
        ///   Q=96 -> ~16 KB/frame (~3.8 Mbps at 30 fps) — visible block
        ///                         artefacts but typically streamable
        ///                         with audio intact.
        /// Lower-detail content (typical webcam) lands at materially
        /// smaller frame sizes for the same Q.
        /// </summary>
        public int BaseQIndex
        {
            get => _baseQIndex;
            set
            {
                if (value < 0 || value > 127)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "BaseQIndex must be in the range [0, 127] (VP8 base quantizer index).");
                }
                _baseQIndex = value;
            }
        }

        // Default cadence for keyframes when inter coding lands in a
        // future PR. 30 = one keyframe per second at 30 fps. Currently
        // every frame is still a keyframe (the inter-encoding path is
        // built up across PRs 2-5 of the P-frame foundation series), so
        // this property only controls the internal state machine for now.
        private const int DEFAULT_KEYFRAME_INTERVAL_FRAMES = 30;

        private int _keyframeIntervalFrames = DEFAULT_KEYFRAME_INTERVAL_FRAMES;
        private int _framesSinceLastKeyframe = 0;

        /// <summary>
        /// Number of frames between forced keyframes. Set to 1 to force
        /// every frame to be a keyframe; 30 (default) gives one keyframe
        /// per second at 30 fps. Range [1, int.MaxValue].
        ///
        /// Inter frames between keyframes are encoded as ZEROMV
        /// referencing LAST_FRAME (PR 5 of the P-frame foundation
        /// series). The trade-off vs all-keyframes is dramatically lower
        /// bitrate at the cost of error-resilience -- a lost inter frame
        /// will desync the decoder until the next keyframe.
        /// </summary>
        public int KeyframeIntervalFrames
        {
            get => _keyframeIntervalFrames;
            set
            {
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "KeyframeIntervalFrames must be at least 1.");
                }
                _keyframeIntervalFrames = value;
            }
        }

        /// <summary>
        /// Read-only count of frames emitted since the last keyframe
        /// (resets to 0 immediately after a keyframe is encoded).
        /// Exposed primarily for tests and diagnostics.
        /// </summary>
        public int FramesSinceLastKeyframe => _framesSinceLastKeyframe;

        public byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            lock (_encoderLock)
            {
                if (width <= 0 || width % 16 != 0 || height <= 0 || height % 16 != 0)
                {
                    // The foundation encoder requires multiples of 16 — no
                    // padding/cropping support yet.
                    throw new NotSupportedException(
                        $"Width and height must be positive multiples of 16. Got {width}x{height}.");
                }

                // Convert the input sample into planar I420.
                byte[] i420 = (pixelFormat == VideoPixelFormatsEnum.I420)
                    ? sample
                    : PixelConverter.ToI420(width, height, width, sample, pixelFormat);

                int ySize = width * height;
                int cSize = (width / 2) * (height / 2);
                if (i420.Length != ySize + 2 * cSize)
                {
                    throw new ArgumentException(
                        $"I420 buffer length {i420.Length} does not match expected {ySize + 2 * cSize} for {width}x{height}.");
                }

                if (_srcY == null || _srcY.Length < ySize) { _srcY = new byte[ySize]; }
                if (_srcU == null || _srcU.Length < cSize) { _srcU = new byte[cSize]; _srcV = new byte[cSize]; }
                Buffer.BlockCopy(i420, 0,             _srcY, 0, ySize);
                Buffer.BlockCopy(i420, ySize,         _srcU, 0, cSize);
                Buffer.BlockCopy(i420, ySize + cSize, _srcV, 0, cSize);

                // Decide keyframe vs inter for this call.
                // - Forced keyframe (via ForceKeyFrame()): always keyframe.
                // - Frame counter reached interval: keyframe.
                // - First frame of stream / no valid reference: keyframe.
                // - Otherwise: inter (ZEROMV LAST_FRAME).
                bool forceKey = _forceKeyFrame
                              || _framesSinceLastKeyframe == 0
                              || _framesSinceLastKeyframe >= _keyframeIntervalFrames;
                _forceKeyFrame = false;

                byte[] result;
                if (forceKey)
                {
                    result = frame_encoder.EncodeKeyframeWithBuffers(_srcY, _srcU, _srcV, width, height, _baseQIndex, _frameBuffers);
                    _framesSinceLastKeyframe = 1;
                }
                else
                {
                    // Inter (P) frame: ZEROMV referencing LAST_FRAME for
                    // every macroblock. The reference frame is the
                    // reconstruction of the previous keyframe / inter
                    // frame, cached on the per-thread FrameEncoderBuffers.
                    result = frame_encoder.EncodeInterFrameWithBuffers(_srcY, _srcU, _srcV, width, height, _baseQIndex, _frameBuffers);
                    _framesSinceLastKeyframe++;
                }
                return result;
            }
        }

        public unsafe IEnumerable<VideoSample> DecodeVideo(byte[] frame, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            lock (_decoderLock)
            {
                if (_vp8Decoder == null)
                {
                    _vp8Decoder = new vpx_codec_ctx_t();
                    vpx_codec_iface_t algo = vp8_dx.vpx_codec_vp8_dx();
                    vpx_codec_dec_cfg_t cfg = new vpx_codec_dec_cfg_t { threads = 1 };
                    vpx_codec_err_t res = vpx_decoder.vpx_codec_dec_init(_vp8Decoder, algo, cfg, 0);
                }

                //logger.LogDebug($"Attempting to decode {frame.Length} bytes.");
                //Console.WriteLine(frame.HexStr());

                fixed (byte* pFrame = frame)
                {
                    var result = vpx_decoder.vpx_codec_decode(_vp8Decoder, pFrame, (uint)frame.Length, IntPtr.Zero, 0);
                    //logger.LogDebug($"VP8 decode result {result}.");
                    if (result != vpx_codec_err_t.VPX_CODEC_OK)
                    {
                        logger.LogWarning("VP8 decode of video sample failed with {Result}.", result);
                    }
                }

                IntPtr iter = IntPtr.Zero;
                var img = vpx_decoder.vpx_codec_get_frame(_vp8Decoder, iter);

                if (img == null)
                {
                    logger.LogWarning("Image could not be acquired from VP8 decoder stage.");
                }
                else
                {
                    int dwidth = (int)img.d_w;
                    int dheight = (int)img.d_h;
                    int sz = dwidth * dheight;

                    var yPlane = img.planes[0];
                    var uPlane = img.planes[1];
                    var vPlane = img.planes[2];

                    byte[] decodedBuffer = new byte[dwidth * dheight * 3 / 2];

                    for (uint row = 0; row < dheight; row++)
                    {
                        Marshal.Copy((IntPtr)(yPlane + row * img.stride[0]), decodedBuffer, (int)(row * dwidth), (int)dwidth);

                        if (row < dheight / 2)
                        {
                            Marshal.Copy((IntPtr)(uPlane + row * img.stride[1]), decodedBuffer, (int)(sz + row * (dwidth / 2)), (int)dwidth / 2);
                            Marshal.Copy((IntPtr)(vPlane + row * img.stride[2]), decodedBuffer, (int)(sz + sz / 4 + row * (dwidth / 2)), (int)dwidth / 2);
                        }
                    }

                    byte[] rgb = PixelConverter.I420toBGR(decodedBuffer, dwidth, dheight, out _);
                    return new List<VideoSample> { new VideoSample { Width = img.d_w, Height = img.d_h, Sample = rgb } };
                }

                return new List<VideoSample>();
            }
        }

        public void Dispose()
        {
            //_vp8Encoder?.Dispose();
            //_vp8Decoder?.Dispose();
        }

        public byte[] EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<RawImage> DecodeVideoFaster(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            throw new NotImplementedException();
        }
    }

    public static class StrHelper
    {
        private static readonly sbyte[] _hexDigits =
           { -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              0,1,2,3,4,5,6,7,8,9,-1,-1,-1,-1,-1,-1,
              -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, };

        private static readonly char[] hexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        public static string HexStr(this byte[] buffer, char? separator = null)
        {
            return buffer.HexStr(buffer.Length, separator);
        }

        public static string HexStr(this byte[] buffer, int length, char? separator = null)
        {
            string rv = string.Empty;

            for (int i = 0; i < length; i++)
            {
                var val = buffer[i];
                rv += hexmap[val >> 4];
                rv += hexmap[val & 15];

                if (separator != null && i != length - 1)
                {
                    rv += separator;
                }
            }

            return rv.ToUpper();
        }

        public static byte[] ParseHexStr(string hexStr)
        {
            List<byte> buffer = new List<byte>();
            var chars = hexStr.ToCharArray();
            int posn = 0;
            while (posn < hexStr.Length)
            {
                while (char.IsWhiteSpace(chars[posn]))
                {
                    posn++;
                }
                sbyte c = _hexDigits[chars[posn++]];
                if (c == -1)
                {
                    break;
                }
                sbyte n = (sbyte)(c << 4);
                c = _hexDigits[chars[posn++]];
                if (c == -1)
                {
                    break;
                }
                n |= c;
                buffer.Add((byte)n);
            }
            return buffer.ToArray();
        }
    }
}
