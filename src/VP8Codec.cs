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
using SIPSorceryMedia.Abstractions.V1;

namespace Vpx.Net
{
    public class VP8Codec : IVideoEncoder, IDisposable
    {
        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<VP8Codec>();

        public static readonly List<VideoCodecsEnum> SupportedCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8
        };

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

        public void ForceKeyFrame() => _forceKeyFrame = true;
        public bool IsSupported(VideoCodecsEnum codec) => codec == VideoCodecsEnum.VP8;

        public byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            //lock (_encoderLock)
            //{
            //    if (_vp8Encoder == null)
            //    {
            //        _vp8Encoder = new Vp8Codec();
            //        _vp8Encoder.InitialiseEncoder((uint)width, (uint)height);
            //    }

            //    var i420Buffer = PixelConverter.ToI420(width, height, sample, pixelFormat);
            //    var encodedBuffer = _vp8Encoder.Encode(i420Buffer, _forceKeyFrame);

            //    if (_forceKeyFrame)
            //    {
            //        _forceKeyFrame = false;
            //    }

            //    return encodedBuffer;
            //}

            return null;
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
                        logger.LogWarning($"VP8 decode of video sample failed with {result}.");
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

                    byte[] rgb = PixelConverter.I420toBGR(decodedBuffer, dwidth, dheight);
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
