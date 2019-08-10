#include "ImageConvert.h"

namespace SIPSorceryMedia {

	ImageConvert::ImageConvert()
	{ }

	ImageConvert::~ImageConvert()
	{
		sws_freeContext(_swsContextRGBToYUV);
		sws_freeContext(_swsContextYUVToRGB);
		//sws_freeContext(_swsContext);
	}

	int ImageConvert::ConvertRGBtoYUV(unsigned char* bmp, VideoSubTypesEnum rgbInputFormat, int width, int height, int stride, VideoSubTypesEnum yuvOutputFormat, /* out */ array<Byte> ^% buffer)
	{
		AVPixelFormat rgbPixelFormat = VideoSubTypesHelper::GetPixelFormatForVideoSubType(rgbInputFormat);
		AVPixelFormat yuvPixelFormat = VideoSubTypesHelper::GetPixelFormatForVideoSubType(yuvOutputFormat);

		_swsContextRGBToYUV = sws_getCachedContext(_swsContextRGBToYUV, width, height, rgbPixelFormat, width, height, yuvPixelFormat, SWS_BILINEAR, NULL, NULL, NULL);

		if (!_swsContextRGBToYUV) {
			fprintf(stderr, "Could not initialize the conversion context in ImageConvert::ConvertRGBtoYUV.\n");
			return -1;
		}

		AVFrame* dstFrame = av_frame_alloc();
		int num_bytes = avpicture_get_size(yuvPixelFormat, width, height);
		int bufferSize = num_bytes*sizeof(uint8_t);
		uint8_t* dstFrameBuffer = (uint8_t *)av_malloc(bufferSize);
		avpicture_fill((AVPicture*)dstFrame, dstFrameBuffer, yuvPixelFormat, width, height);

		uint8_t * srcData[1] = { bmp };		// RGB has one plane
    int srcLinesize[1] = { stride };

		int res = sws_scale(_swsContextRGBToYUV, srcData, srcLinesize, 0, height, dstFrame->data, dstFrame->linesize);

		if (res == 0) {
			fprintf(stderr, "The conversion failed in ImageConvert::ConvertRGBtoYUV.\n");
			return -1;
		}
	
		buffer = gcnew array<Byte>(bufferSize);
		Marshal::Copy((IntPtr)dstFrameBuffer, buffer, 0, bufferSize);

		av_freep(&dstFrameBuffer);
		av_frame_free(&dstFrame);
		
		return 0;
	}

	int ImageConvert::ConvertYUVToRGB(unsigned char* yuv, VideoSubTypesEnum yuvInputFormat, int width, int height, VideoSubTypesEnum rgbOutputFormat, /* out */ array<Byte> ^% buffer)
	{
		AVPixelFormat yuvPixelFormat = VideoSubTypesHelper::GetPixelFormatForVideoSubType(yuvInputFormat);
		AVPixelFormat rgbPixelFormat = VideoSubTypesHelper::GetPixelFormatForVideoSubType(rgbOutputFormat);

		_swsContextYUVToRGB = sws_getCachedContext(_swsContextYUVToRGB, width, height, yuvPixelFormat, width, height, rgbPixelFormat, SWS_BILINEAR, NULL, NULL, NULL);

		if (!_swsContextYUVToRGB) {
			fprintf(stderr, "Could not initialize the conversion context in ImageConvert::ConvertRGBtoYUV.\n");
			return -1;
		}

		AVFrame* srcFrame = av_frame_alloc();
		int srcNumBytes = avpicture_get_size(yuvPixelFormat, width, height);
		int srcBufferSize = srcNumBytes * sizeof(uint8_t);
		uint8_t* srcFrameBuffer = (uint8_t *)av_malloc(srcBufferSize);
		avpicture_fill((AVPicture*)srcFrame, yuv, yuvPixelFormat, width, height);

		AVFrame* dstFrame = av_frame_alloc();
		int num_bytes = avpicture_get_size(rgbPixelFormat, width, height);
		int bufferSize = num_bytes*sizeof(uint8_t);
		uint8_t* dstFrameBuffer = (uint8_t *)av_malloc(bufferSize);
		avpicture_fill((AVPicture*)dstFrame, dstFrameBuffer, rgbPixelFormat, width, height);

		int res = sws_scale(_swsContextYUVToRGB, srcFrame->data, srcFrame->linesize, 0, height, dstFrame->data, dstFrame->linesize);

		if (res == 0) {
			fprintf(stderr, "The conversion failed in ImageConvert::ConvertRGBtoYUV.\n");
			return -1;
		}

		buffer = gcnew array<Byte>(bufferSize);
		Marshal::Copy((IntPtr)dstFrameBuffer, buffer, 0, bufferSize);

		av_freep(&dstFrameBuffer);
		av_frame_free(&dstFrame);
		av_freep(&srcFrameBuffer);
		av_frame_free(&srcFrame);

		return 0;
	}
}
