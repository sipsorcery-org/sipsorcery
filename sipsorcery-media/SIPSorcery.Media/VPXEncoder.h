#pragma once

#include <stdio.h>
#include "libvpx\vpx\vpx_encoder.h"
#include "libvpx\vpx\vpx_decoder.h"
#include "libvpx\vpx\vp8cx.h"
#include "libvpx\vpx\vp8dx.h"

extern "C"
{
#include "libswscale\swscale.h"
#include "libavcodec\avcodec.h"
#include "libavformat\avformat.h"
#include "libavutil\avutil.h"
}

using namespace System;
using namespace System::Runtime::InteropServices;

namespace SIPSorceryMedia {

	public ref class VPXEncoder
	{
		public:
			VPXEncoder();
			~VPXEncoder();
			//int Init(unsigned int width, unsigned int height);
			int InitEncoder(unsigned int width, unsigned int height);
			int InitDecoder();
			int Encode(unsigned char * i420, int i420Length, int sampleCount, array<Byte> ^% buffer);
			int EncodeRGB24(unsigned char * bmp, int bmpLength, int sampleCount, array<Byte> ^% buffer);
			//void Decode(unsigned char * buf, int bufLength, array<Byte> ^% buffer);
			int Decode(unsigned char* buffer, int bufferSize, array<Byte> ^% outBuffer, unsigned int % width, unsigned int % height);

		private:
			const vpx_img_fmt VIDEO_INPUT_FORMAT = VPX_IMG_FMT_I420; // VPX_IMG_FMT_YV12

			//vpx_codec_enc_cfg_t * _vpxConfig;
			vpx_codec_ctx_t * _vpxCodec;
			vpx_codec_ctx_t * _vpxDecoder;
			vpx_image_t * _rawImage;
			int _width = 0, _height = 0;
			
			//AVCodec * _codec;
			//AVCodecContext * _codecContext;

			AVFrame* ConvertToI420(int width, int height);
	};
}

