#pragma once

#include <stdio.h>

#include "VideoSubTypes.h"

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

	public ref class ImageConvert
	{
	public:
		ImageConvert();
		~ImageConvert();
		int ConvertRGBtoYUV(unsigned char* bmp, VideoSubTypesEnum rgbInputFormat, int width, int height, int stride, VideoSubTypesEnum yuvOutputFormat, /* out */ array<Byte> ^% buffer);
		int ConvertYUVToRGB(unsigned char* yuv, VideoSubTypesEnum yuvInputFormat, int width, int height, VideoSubTypesEnum rgbOutputFormat, /* out */ array<Byte> ^% buffer);

	private:
		SwsContext* _swsContextRGBToYUV = NULL;
		SwsContext* _swsContextYUVToRGB = NULL;
	};
}