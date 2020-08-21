using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using vpxmd;

namespace SIPSorceryMedia.Windows.Codecs
{
    public class Vp8Encoder : IDisposable
    {
        //vpx_codec_ctx_t* _vpxCodec;
        //private IntPtr _vpxCodec;
        //vpx_codec_ctx_t* _vpxDecoder;
        //private IntPtr _vpxDecoder;
        //vpx_image_t* _rawImage;
        //private IntPtr _rawImage;

        uint _width = 0;
        uint _height = 0;
        uint _stride = 0;

        // Setting config parameters in Chromium source.
        // https://chromium.googlesource.com/external/webrtc/stable/src/+/b8671cb0516ec9f6c7fe22a6bbe331d5b091cdbb/modules/video_coding/codecs/vp8/vp8.cc
        // Updated link 15 Jun 2020.
        // https://chromium.googlesource.com/external/webrtc/stable/src/+/refs/heads/master/modules/video_coding/codecs/vp8/vp8_impl.cc
        public void InitialiseEncoder(uint width, uint height, uint stride)
        {
            _width = width;
            _height = height;
            _stride = stride;

            VpxCodecEncCfg vp8EncoderCfg = new VpxCodecEncCfg();

            var setConfigRes = vpx_encoder.VpxCodecEncConfigDefault(vp8cx.VpxCodecVp8Cx(), vp8EncoderCfg, 0);
            if (setConfigRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to set VP8 encoder configuration to default values, {setConfigRes}.");
            }

            vp8EncoderCfg.GW = _width;
            vp8EncoderCfg.GH = _height;

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

            VpxCodecCtx vpxCodec = new VpxCodecCtx();
            var initEncoderRes = vpx_encoder.VpxCodecEncInitVer(vpxCodec, vp8cx.VpxCodecVp8Cx(), vp8EncoderCfg, 0, 23);
            if(initEncoderRes != VpxCodecErrT.VPX_CODEC_OK)
            {
                throw new ApplicationException($"Failed to initialise VP8 encoder, {initEncoderRes}.");
            }


            //_vpxCodec = new vpx_codec_ctx_t();
            //_rawImage = new vpx_image_t();

            //_rawImage = vpx_img_alloc(IntPtr.Zero, VpxImageFormat.VPX_IMG_FMT_I420, _width, _height, _stride);

            //	vpx_img_alloc(_rawImage, VPX_IMG_FMT_I420, width, height, stride);
        }

        public static int GetCodecVersion()
        {
            return vpxmd.vpx_codec.VpxCodecVersion();
        }

        public static string GetCodecVersionStr()
        {
            return vpxmd.vpx_codec.VpxCodecVersionStr();
        }

        public static string GetCodecVersionExtraStr()
        {
            return vpxmd.vpx_codec.VpxCodecVersionExtraStr();
        }

        public void Dispose()
        {
            //         if(_rawImage != IntPtr.Zero)
            //         {
            //	vpx_img_free(_rawImage);
            //}
        }
    }
}
