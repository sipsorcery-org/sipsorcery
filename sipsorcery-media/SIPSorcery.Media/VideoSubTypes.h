#pragma once

#include <mfapi.h>

extern "C"
{
	#include "libswscale\swscale.h"
}

namespace SIPSorceryMedia {

	public enum class VideoSubTypesEnum
	{
		I420,
		RGB24,
		RGB32,
		YUY2,
		BGR24,
	};

	public ref class VideoSubTypesHelper
	{
	public:
		static GUID GetGuidForVideoSubType(VideoSubTypesEnum videoModeEnum)
		{
			switch (videoModeEnum)
			{
				case VideoSubTypesEnum::I420: return MFVideoFormat_I420;
				case VideoSubTypesEnum::RGB24: return MFVideoFormat_RGB24;
				case VideoSubTypesEnum::RGB32: return MFVideoFormat_RGB32;
				case VideoSubTypesEnum::YUY2: return MFVideoFormat_YUY2;
				case VideoSubTypesEnum::BGR24: return MFVideoFormat_RGB24;
				default: throw gcnew System::ApplicationException("Video mode not recognised in GetGuidForVideoSubType.");
			}
		};

		static AVPixelFormat GetPixelFormatForVideoSubType(VideoSubTypesEnum videoModeEnum)
		{
			switch (videoModeEnum)
			{
				case VideoSubTypesEnum::I420: return AVPixelFormat::AV_PIX_FMT_YUV420P;
				case VideoSubTypesEnum::RGB24: return AVPixelFormat::AV_PIX_FMT_RGB24;
				case VideoSubTypesEnum::RGB32: return AVPixelFormat::AV_PIX_FMT_RGB32;
				case VideoSubTypesEnum::YUY2: return AVPixelFormat::AV_PIX_FMT_YUYV422;
				case VideoSubTypesEnum::BGR24: return AVPixelFormat::AV_PIX_FMT_BGR24;
				default: throw gcnew System::ApplicationException("Video mode not recognised in GetPixelFormatForVideoSubType.");
			}
		}
	};
}
