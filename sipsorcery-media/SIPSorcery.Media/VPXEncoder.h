#pragma once

#include <stdio.h>
#include "vpx\vpx_encoder.h"
#include "vpx\vpx_decoder.h"
#include "vpx\vp8cx.h"
#include "vpx\vp8dx.h"

#include <memory>

using namespace System;
using namespace System::Runtime::InteropServices;

namespace SIPSorceryMedia {

	public ref class VPXEncoder
	{
		public:
			VPXEncoder();
			~VPXEncoder();
			int InitEncoder(unsigned int width, unsigned int height, unsigned int stride);
			int InitDecoder();
			int Encode(unsigned char * i420, int i420Length, int sampleCount, array<Byte> ^% buffer);
			int Decode(unsigned char* buffer, int bufferSize, array<Byte> ^% outBuffer, unsigned int % width, unsigned int % height);

      int GetWidth() { return _width; }
      int GetHeight() { return _height; }
      int GetStride() { return _stride; }

		private:

			vpx_codec_ctx_t * _vpxCodec;
			vpx_codec_ctx_t * _vpxDecoder;
			vpx_image_t * _rawImage;
			int _width = 0, _height = 0, _stride = 0;
	};
}

