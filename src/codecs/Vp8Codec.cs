using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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

        private VpxCodecCtx _vpxEncodeCtx;
        private VpxImage _vpxEncodeImg;
        private VpxCodecCtx _vpxDecodeCtx;
        private VpxImage _vpxDecodeImg;

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
            if(initEncoderRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to initialise VP8 encoder, {initEncoderRes}.");
            }

            VpxImage.VpxImgAlloc(_vpxEncodeImg, VpxImgFmt.VPX_IMG_FMT_I420, _encodeWidth, _encodeHeight, 1);
        }

        public void InitialiseDecoder()
        {
            _vpxDecodeCtx = new VpxCodecCtx();
            _vpxEncodeImg = new VpxImage();

            VpxCodecDecCfg vp8DecoderCfg = new VpxCodecDecCfg();

            var initDecoderRes = vpx_decoder.VpxCodecDecInitVer(_vpxDecodeCtx, vp8dx.VpxCodecVp8Dx(), vp8DecoderCfg, 0, VPX_DECODER_ABI_VERSION);
            if (initDecoderRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to initialise VP8 decoder, {initDecoderRes}.");
            }

            //VpxImage.VpxImgAlloc(_vpxEncodeImg, VpxImgFmt.VPX_IMG_FMT_I420, _encodeWidth, _encodeHeight, 1);
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
                    if(encodeRes != VpxCodecErrT.VPX_CODEC_OK)
                    {
                        throw new ApplicationException($"VP8 encode attempt failed, {encodeRes}.");
                    }

                    IntPtr iter = IntPtr.Zero;

                    var pkt = vpx_encoder.VpxCodecGetCxData(_vpxEncodeCtx, (void**)&iter);

                    while (pkt != null)
                    {
                        switch(pkt.Kind)
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
        public byte[] Decode(byte[] buffer, int bufferSize, out int width, out int height)
        {
            width = 0;
            height = 0;

            //vpx_codec_iter_t iter = NULL;
            //vpx_image_t* img;

            ///* Decode the frame */
            //vpx_codec_err_t decodeResult = vpx_codec_decode(_vpxDecoder, (const uint8_t*)buffer, bufferSize, NULL, 0);

            //if (decodeResult != VPX_CODEC_OK)
            //{
            //    printf("VPX codec failed to decode the frame: %s.\n", vpx_codec_err_to_string(decodeResult));
            //    return -1;
            //}
            //else
            //{
            //    int pointer = 0;

            //    while ((img = vpx_codec_get_frame(_vpxDecoder, &iter)))
            //    {

            //        width = img->d_w;
            //        height = img->d_h;

            //        //int outputSize = img->stride[0] * img->d_w * 3 / 2;
            //        int outputSize = img->stride[0] * img->d_h * 3 / 2;
            //        unsigned char* bufferOut = (unsigned char*)malloc(outputSize);
            //        unsigned int plane, y;

            //        for (plane = 0; plane < 3; plane++)
            //        {

            //            unsigned char* buf = img->planes[plane];

            //            for (y = 0; y < (plane ? (img->d_h + 1) >> 1 : img->d_h); y++)
            //            {

            //                int numberofbitsToCopy = (plane ? (img->d_w + 1) >> 1 : img->d_w);
            //                memcpy(bufferOut + pointer, buf, numberofbitsToCopy);
            //                pointer += numberofbitsToCopy;

            //                buf += img->stride[plane];
            //            }
            //        }

            //        outBuffer = gcnew array<Byte>(outputSize);
            //        Marshal::Copy((IntPtr)bufferOut, outBuffer, 0, outputSize);

            //        free(bufferOut);

            //        vpx_img_free(img);
            //        img = nullptr;
            //    }
            //}

            return null;
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
            if(_vpxEncodeCtx != null)
            {
                vpx_codec.VpxCodecDestroy(_vpxEncodeCtx);
            }

            if(_vpxEncodeImg != null)
            {
                VpxImage.VpxImgFree(_vpxEncodeImg);
            }
        }
    }
}
