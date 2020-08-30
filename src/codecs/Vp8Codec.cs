using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vpxmd;

namespace SIPSorceryMedia.Windows.Codecs
{
    public class Vp8Codec : IDisposable
    {
        /// <summary>
        /// This is defined in vpx_encoder.h but is currently not being pulled across by CppSharp,
        /// see https://github.com/mono/CppSharp/issues/1399. Once the issue is solved this constant
        /// can be removed.
        /// </summary>
        private const int VPX_ENCODER_ABI_VERSION = 23;

        private const int VPX_DECODER_ABI_VERSION = 12;

        /// <summary>
        /// The parameter to use for the "soft deadline" when encoding.
        /// </summary>
        /// <remarks>
        /// Defined in vpx_encoder.h.
        /// </remarks>
        private const int VPX_DL_REALTIME = 1;

        /// <summary>
        /// Encoder flag to force the current sample to be a key frame.
        /// </summary>
        /// <remarks>
        /// Defined in vpx_encoder.h.
        /// </remarks>
        private const int VPX_EFLAG_FORCE_KF = 1;

        /// <summary>
        /// Indicates whether an encoded packet is a key frame.
        /// </summary>
        /// <remarks>
        /// Defined in vpx_encoder.h.
        /// </remarks>
        private const byte VPX_FRAME_IS_KEY = 0x1;

        public static ILogger logger = NullLogger.Instance;

        private VpxCodecCtx _vpxEncodeCtx;
        private VpxImage _vpxEncodeImg;
        private VpxCodecCtx _vpxDecodeCtx;
        private bool _isDisposing;

        uint _encodeWidth = 0;
        uint _encodeHeight = 0;

        // Setting config parameters in Chromium source.
        // https://chromium.googlesource.com/external/webrtc/stable/src/+/b8671cb0516ec9f6c7fe22a6bbe331d5b091cdbb/modules/video_coding/codecs/vp8/vp8.cc
        // Updated link 15 Jun 2020.
        // https://chromium.googlesource.com/external/webrtc/stable/src/+/refs/heads/master/modules/video_coding/codecs/vp8/vp8_impl.cc
        public void InitialiseEncoder(uint width, uint height)
        {
            _encodeWidth = width;
            _encodeHeight = height;

            _vpxEncodeCtx = new VpxCodecCtx();
            _vpxEncodeImg = new VpxImage();

            VpxCodecEncCfg vp8EncoderCfg = new VpxCodecEncCfg();

            var setConfigRes = vpx_encoder.VpxCodecEncConfigDefault(vp8cx.VpxCodecVp8Cx(), vp8EncoderCfg, 0);
            if (setConfigRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to set VP8 encoder configuration to default values, {setConfigRes}.");
            }

            vp8EncoderCfg.GW = _encodeWidth;
            vp8EncoderCfg.GH = _encodeHeight;

            //	vpxConfig.g_w = width;
            //	vpxConfig.g_h = height;
            //	vpxConfig.rc_target_bitrate = _rc_target_bitrate;//  300; // 5000; // in kbps.
            //	vpxConfig.rc_min_quantizer = _rc_min_quantizer;// 20; // 50;
            //	vpxConfig.rc_max_quantizer = _rc_max_quantizer;// 30; // 60;
            //	vpxConfig.g_pass = VPX_RC_ONE_PASS;
            //	if (_rc_is_cbr)
            //	{
            //		vpxConfig.rc_end_usage = VPX_CBR;
            //	}
            //	else
            //	{
            //		vpxConfig.rc_end_usage = VPX_VBR;
            //	}

            //	vpxConfig.g_error_resilient = VPX_ERROR_RESILIENT_DEFAULT;
            //	vpxConfig.g_lag_in_frames = 0;
            //	vpxConfig.rc_resize_allowed = 0;
            //	vpxConfig.kf_max_dist = 20;

            var initEncoderRes = vpx_encoder.VpxCodecEncInitVer(_vpxEncodeCtx, vp8cx.VpxCodecVp8Cx(), vp8EncoderCfg, 0, VPX_ENCODER_ABI_VERSION);
            if (initEncoderRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to initialise VP8 encoder, {vpx_codec.VpxCodecErrToString(initEncoderRes)}.");
            }

            VpxImage.VpxImgAlloc(_vpxEncodeImg, VpxImgFmt.VPX_IMG_FMT_I420, _encodeWidth, _encodeHeight, 1);
        }

        public void InitialiseDecoder()
        {
            _vpxDecodeCtx = new VpxCodecCtx();

            var initDecoderRes = vpx_decoder.VpxCodecDecInitVer(_vpxDecodeCtx, vp8dx.VpxCodecVp8Dx(), null, 0, VPX_DECODER_ABI_VERSION);
            if (initDecoderRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to initialise VP8 decoder, {vpx_codec.VpxCodecErrToString(initDecoderRes)}.");
            }
        }

        public byte[] Encode(byte[] i420, bool forceKeyFrame = false)
        {
            byte[] encodedSample = null;

            unsafe
            {
                fixed (byte* pI420 = i420)
                {
                    VpxImage.VpxImgWrap(_vpxEncodeImg, VpxImgFmt.VPX_IMG_FMT_I420, _encodeWidth, _encodeHeight, 1, pI420);

                    int flags = (forceKeyFrame) ? VPX_EFLAG_FORCE_KF : 0;

                    var encodeRes = vpx_encoder.VpxCodecEncode(_vpxEncodeCtx, _vpxEncodeImg, 1, 1, flags, VPX_DL_REALTIME);
                    if (encodeRes != VpxCodecErrT.VPX_CODEC_OK)
                    {
                        throw new ApplicationException($"VP8 encode attempt failed, {vpx_codec.VpxCodecErrToString(encodeRes)}.");
                    }

                    IntPtr iter = IntPtr.Zero;

                    var pkt = vpx_encoder.VpxCodecGetCxData(_vpxEncodeCtx, (void**)&iter);

                    while (pkt != null)
                    {
                        switch (pkt.Kind)
                        {
                            case VpxCodecCxPktKind.VPX_CODEC_CX_FRAME_PKT:
                                //Console.WriteLine($"is key frame={(pkt.data.frame.Flags & VPX_FRAME_IS_KEY) > 0}, length {pkt.data.Raw.Sz}.");
                                encodedSample = new byte[pkt.data.Raw.Sz];
                                Marshal.Copy(pkt.data.Raw.Buf, encodedSample, 0, encodedSample.Length);
                                break;
                            default:
                                throw new ApplicationException($"Unexpected packet type received from encoder, {pkt.Kind}.");
                        }

                        pkt = vpx_encoder.VpxCodecGetCxData(_vpxEncodeCtx, (void**)&iter);
                    }
                }
            }

            return encodedSample;
        }

        // https://swift.im/git/swift-contrib/tree/Swiften/ScreenSharing/VP8Decoder.cpp?id=6247ed394302ff2cf1f33a71df808bebf7241242
        public List<byte[]> Decode(byte[] buffer, int bufferSize, out uint width, out uint height)
        {
            List<byte[]> decodedBuffers = new List<byte[]>();
            width = 0;
            height = 0;

            if (!_isDisposing)
            {
                unsafe
                {
                    fixed (byte* pBuffer = buffer)
                    {
                        var decodeRes = vpx_decoder.VpxCodecDecode(_vpxDecodeCtx, pBuffer, (uint)bufferSize, IntPtr.Zero, 1);
                        if (decodeRes != VpxCodecErrT.VPX_CODEC_OK)
                        {
                            //throw new ApplicationException($"VP8 decode attempt failed, {vpx_codec.VpxCodecErrToString(decodeRes)}.");
                            logger.LogWarning($"VP8 decode attempt failed, {vpx_codec.VpxCodecErrToString(decodeRes)}.");
                        }
                        else
                        {
                            IntPtr iter = IntPtr.Zero;

                            VpxImage img = vpx_decoder.VpxCodecGetFrame(_vpxDecodeCtx, (void**)&iter);
                            while (img != null)
                            {
                                width = img.DW;
                                height = img.DH;
                                int sz = (int)(width * height);
                                int qtrw = (int)width / 4;

                                var yPlane = (byte*)img.PlaneY;
                                var uPlane = (byte*)img.PlaneU;
                                var vPlane = (byte*)img.PlaneV;

                                //var yPlane = img.PlaneY;
                                //var uPlane = img.PlaneU;
                                //var vPlane = img.PlaneV;

                                byte[] decodedBuffer = new byte[width * height * 3 / 2];

                                //Marshal.Copy(yPlane, decodedBuffer, 0, sz);
                                //Marshal.Copy(uPlane, decodedBuffer, sz, sz / 4);
                                //Marshal.Copy(vPlane, decodedBuffer, sz + sz / 4, sz / 4);

                                //Marshal.Copy((IntPtr)img.ImgData, decodedBuffer, 0, decodedBuffer.Length);

                                for (uint row = 0; row < height; row++)
                                {
                                    //Console.WriteLine($"Copy yplane[{row * img.Stride[0]}] to out[{row * width}] length {width}.");
                                    //Console.WriteLine($"Copy uplane[{row * img.Stride[1]}] to out[{sz + row * qtrw}] length {qtrw}.");
                                    //Console.WriteLine($"Copy vplane[{row * img.Stride[2]}] to out[{sz + sz / 4 + row * qtrw}] length {qtrw}.");

                                    Marshal.Copy((IntPtr)(yPlane + row * img.Stride[0]), decodedBuffer, (int)(row * width), (int)width);
                                    //Marshal.Copy((IntPtr)(uPlane + row * img.Stride[1]), decodedBuffer, (int)(sz + row * qtrw), qtrw);
                                    //Marshal.Copy((IntPtr)(vPlane + row * img.Stride[2]), decodedBuffer, (int)(sz + sz / 4 + row * qtrw), qtrw);
                                }

                                //byte[] data = new byte[width * height * 3];

                                //int i = 0;
                                //for (uint imgY = 0; imgY < height; imgY++)
                                //{
                                //    for (uint imgX = 0; imgX < width; imgX++)
                                //    {
                                //        int y = yPlane[imgY * img.Stride[0] + imgX];
                                //        int u = uPlane[(imgY / 2) * img.Stride[1] + (imgX / 2)];
                                //        int v = vPlane[(imgY / 2) * img.Stride[2] + (imgX / 2)];

                                //        int c = y - 16;
                                //        int d = (u - 128);
                                //        int e = (v - 128);

                                //        // TODO: adjust colors ?

                                //        int r = (298 * c + 409 * e + 128) >> 8;
                                //        int g = (298 * c - 100 * d - 208 * e + 128) >> 8;
                                //        int b = (298 * c + 516 * d + 128) >> 8;

                                //        r = r < 0 ? 0 : r > 255 ? 255 : r;
                                //        g = g < 0 ? 0 : g > 255 ? 255 : g;
                                //        b = b < 0 ? 0 : b > 255 ? 255 : b;

                                //        // TODO: cast instead of clamp8

                                //        data[i + 0] = (byte)(b);
                                //        data[i + 1] = (byte)(g);
                                //        data[i + 2] = (byte)(r);

                                //        i += 3;
                                //    }
                                //}

                                decodedBuffers.Add(decodedBuffer);
                                //decodedBuffers.Add(data);

                                VpxImage.VpxImgFree(img);

                                img = vpx_decoder.VpxCodecGetFrame(_vpxDecodeCtx, (void**)&iter);
                            }
                        }
                    }
                }
            }

            return decodedBuffers;
        }

        private int clamp8(int v)
        {
            return Math.Min(Math.Max(v, 0), 255);
        }

        public static int GetCodecVersion()
        {
            return vpxmd.vpx_codec.VpxCodecVersion();
        }

        public static string GetCodecVersionStr()
        {
            return vpxmd.vpx_codec.VpxCodecVersionStr();
        }

        public void Dispose()
        {
            _isDisposing = true;

            if (_vpxEncodeCtx != null)
            {
                vpx_codec.VpxCodecDestroy(_vpxEncodeCtx);
            }

            if (_vpxEncodeImg != null)
            {
                VpxImage.VpxImgFree(_vpxEncodeImg);
            }

            if (_vpxDecodeCtx != null)
            {
                vpx_codec.VpxCodecDestroy(_vpxDecodeCtx);
            }
        }
    }
}
