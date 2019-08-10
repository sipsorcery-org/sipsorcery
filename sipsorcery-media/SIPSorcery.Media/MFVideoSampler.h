// http://msdn.microsoft.com/en-us/library/windows/desktop/aa473780%28v=vs.85%29.aspx bitmap orientation
// http://msdn.microsoft.com/en-us/library/windows/desktop/dd407212(v=vs.85).aspx Top-Down vs. Bottom-Up DIBs

#pragma once

#include <stdio.h>
#include <mfapi.h>
#include <mfobjects.h>
#include <mfplay.h>
#include <mftransform.h>
#include <mferror.h>
#include <mfobjects.h>
#include <mfreadwrite.h>
#include <errors.h>
#include <wmcodecdsp.h>
#include <wmsdkidl.h>
#include <mmdeviceapi.h>
#include <Audioclient.h>

#include "VideoSubTypes.h"

#include <iostream>
#include <memory>

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Runtime::InteropServices;

#define CHECK_HR(hr, msg) if (HRHasFailed(hr, msg)) return hr;
//#define CHECK_HR(hr, msg) if (hr != S_OK) { printf(msg); printf("Error: %.2X.\n", hr); goto done; }

#define CHECK_HR_EXTENDED(hr, msg) if (HRHasFailed(hr, msg)) { \
   MediaSampleProperties^ sampleProps; \
   sampleProps->Success = false; \
   sampleProps->Error = msg; \
   return sampleProps; \
 };

#define CHECKHR_GOTO(x, y) if(FAILED(x)) goto y

#define INTERNAL_GUID_TO_STRING( _Attribute, _skip ) \
if (Attr == _Attribute) \
{ \
	pAttrStr = #_Attribute; \
	C_ASSERT((sizeof(#_Attribute) / sizeof(#_Attribute[0])) > _skip); \
	pAttrStr += _skip; \
	goto done; \
} \

#if defined(__cplusplus)
extern "C" {

	// See https://social.msdn.microsoft.com/Forums/en-US/8a4adc97-7f74-44bf-8bae-144a273e62fe/guid-6d703461767a494db478f29d25dc9037?forum=os_windowsprotocols and
	// https://msdn.microsoft.com/en-us/library/dd757766(v=vs.85).aspx
	DEFINE_GUID(MFMPEG4Format_MP4A, 0x6d703461, 0x767a, 0x494d, 0xb4, 0x78, 0xf2, 0x9d, 0x25, 0xdc, 0x90, 0x37);
}
#endif

namespace SIPSorceryMedia {

	/* Used to describe the modes of the attached video devices. */
	public ref class VideoMode
	{
	public:
		String ^ DeviceFriendlyName;
		int DeviceIndex;
		UInt32 Width;
		UInt32 Height;
		Guid VideoSubType;
		String ^ VideoSubTypeFriendlyName;
	};

  public ref class MediaSampleProperties
  {
  public:
    bool Success;
    bool HasVideoSample;
    bool HasAudioSample;
    String ^ Error;
    UInt32 Width;
    UInt32 Height;
    Guid VideoSubType;
    String ^ VideoSubTypeFriendlyName;
    UInt64 Timestamp;

    MediaSampleProperties():
      Success(true),
      HasVideoSample(false),
      HasAudioSample(false),
      Error(),
      Width(0),
      Height(0),
      VideoSubType(Guid::Empty),
      VideoSubTypeFriendlyName(),
      Timestamp(0)
    {}

    MediaSampleProperties(const MediaSampleProperties % copy)
    {
      Success = copy.Success;
      HasVideoSample = copy.HasVideoSample;
      HasAudioSample = copy.HasAudioSample;
      Error = copy.Error;
      Width = copy.Width;
      Height = copy.Height;
      VideoSubType = copy.VideoSubType;
      VideoSubTypeFriendlyName = copy.VideoSubTypeFriendlyName;
      Timestamp = copy.Timestamp;
    }
  };

	public ref class MFVideoSampler
	{
	public:
		long Stride = -1;
    Guid VideoMajorType;
    Guid VideoMinorType;

		MFVideoSampler();
		~MFVideoSampler();
		HRESULT GetVideoDevices(/* out */ List<VideoMode^> ^% devices);
		HRESULT Init(int videoDeviceIndex, VideoSubTypesEnum videoSubType, UInt32 width, UInt32 height);
		HRESULT InitFromFile();
		HRESULT FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, UInt32 width, UInt32 height, /* out */ IMFMediaType *&foundpType);
    MediaSampleProperties^ GetSample(/* out */ array<Byte> ^% buffer);
		HRESULT GetAudioSample(/* out */ array<Byte> ^% buffer);
    MediaSampleProperties^ GetNextSample(/* out */ array<Byte> ^% buffer);
		HRESULT PlayAudio();
		void Stop();
		void DumpVideoSubTypes();
		String^ MFVideoSampler::GetMediaTypeDescription(IMFMediaType * pMediaType);
		HRESULT MFVideoSampler::PlayTestAudio();
		HRESULT MFVideoSampler::PlayFileToSpeaker();

		property int Width {
			int get() { return _width; }
		}

		property int Height {
			int get() { return _height; }
		}

	private:

		static BOOL _isInitialised = false;

		IMFSourceReader * _sourceReader = NULL;
		IMFMediaSink * _audioSink = NULL;             // Streaming audio renderer (SAR)
		DWORD videoStreamIndex;
		int _width, _height;

		HRESULT GetDefaultStride(IMFMediaType *pType, /* out */ LONG *plStride);

		/*
		Helper method to check a method call's HRESULT. If the result is a failure then this helper method will
		cause the parent method to print out an error message and immediately return.
		*/
		static BOOL HRHasFailed(HRESULT hr, WCHAR* errtext)
		{
			if (FAILED(hr))
			{
				TCHAR szErr[MAX_ERROR_TEXT_LEN];
				DWORD res = AMGetErrorText(hr, szErr, MAX_ERROR_TEXT_LEN);

				if (res)
				{
					wprintf(L"Error %x: %s\n%s\n", hr, errtext, szErr);
				}
				else
				{
					wprintf(L"Error %x: %s\n", hr, errtext);
				}

				return TRUE;
			}

			return FALSE;
		}

		/* Helper method to convert a native GUID to a managed GUID. */
		static Guid FromGUID(_GUID& guid) {
			return Guid(guid.Data1, guid.Data2, guid.Data3,
				guid.Data4[0], guid.Data4[1],
				guid.Data4[2], guid.Data4[3],
				guid.Data4[4], guid.Data4[5],
				guid.Data4[6], guid.Data4[7]);
		}

		static LPCSTR STRING_FROM_GUID(GUID Attr)
		{
			LPCSTR pAttrStr = NULL;

			// Generics
			INTERNAL_GUID_TO_STRING(MF_MT_MAJOR_TYPE, 6);                     // MAJOR_TYPE
			INTERNAL_GUID_TO_STRING(MF_MT_SUBTYPE, 6);                        // SUBTYPE
			INTERNAL_GUID_TO_STRING(MF_MT_ALL_SAMPLES_INDEPENDENT, 6);        // ALL_SAMPLES_INDEPENDENT   
			INTERNAL_GUID_TO_STRING(MF_MT_FIXED_SIZE_SAMPLES, 6);             // FIXED_SIZE_SAMPLES
			INTERNAL_GUID_TO_STRING(MF_MT_COMPRESSED, 6);                     // COMPRESSED
			INTERNAL_GUID_TO_STRING(MF_MT_SAMPLE_SIZE, 6);                    // SAMPLE_SIZE
			INTERNAL_GUID_TO_STRING(MF_MT_USER_DATA, 6);                      // MF_MT_USER_DATA

			// Audio
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_NUM_CHANNELS, 12);            // NUM_CHANNELS
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_SAMPLES_PER_SECOND, 12);      // SAMPLES_PER_SECOND
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 12);    // AVG_BYTES_PER_SECOND
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_BLOCK_ALIGNMENT, 12);         // BLOCK_ALIGNMENT
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_BITS_PER_SAMPLE, 12);         // BITS_PER_SAMPLE
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_VALID_BITS_PER_SAMPLE, 12);   // VALID_BITS_PER_SAMPLE
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_SAMPLES_PER_BLOCK, 12);       // SAMPLES_PER_BLOCK
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_CHANNEL_MASK, 12);            // CHANNEL_MASK
			INTERNAL_GUID_TO_STRING(MF_MT_AUDIO_PREFER_WAVEFORMATEX, 12);     // PREFER_WAVEFORMATEX

			// Video
			INTERNAL_GUID_TO_STRING(MF_MT_FRAME_SIZE, 6);                     // FRAME_SIZE
			INTERNAL_GUID_TO_STRING(MF_MT_FRAME_RATE, 6);                     // FRAME_RATE
			INTERNAL_GUID_TO_STRING(MF_MT_PIXEL_ASPECT_RATIO, 6);             // PIXEL_ASPECT_RATIO
			INTERNAL_GUID_TO_STRING(MF_MT_INTERLACE_MODE, 6);                 // INTERLACE_MODE
			INTERNAL_GUID_TO_STRING(MF_MT_AVG_BITRATE, 6);                    // AVG_BITRATE
			INTERNAL_GUID_TO_STRING(MF_MT_DEFAULT_STRIDE, 6);				  // STRIDE
			INTERNAL_GUID_TO_STRING(MF_MT_AVG_BIT_ERROR_RATE, 6);
			INTERNAL_GUID_TO_STRING(MF_MT_GEOMETRIC_APERTURE, 6);
			INTERNAL_GUID_TO_STRING(MF_MT_MINIMUM_DISPLAY_APERTURE, 6);
			INTERNAL_GUID_TO_STRING(MF_MT_PAN_SCAN_APERTURE, 6);
			INTERNAL_GUID_TO_STRING(MF_MT_VIDEO_NOMINAL_RANGE, 6);

			// Major type values
			INTERNAL_GUID_TO_STRING(MFMediaType_Default, 12);                 // Default
			INTERNAL_GUID_TO_STRING(MFMediaType_Audio, 12);                   // Audio
			INTERNAL_GUID_TO_STRING(MFMediaType_Video, 12);                   // Video
			INTERNAL_GUID_TO_STRING(MFMediaType_Script, 12);                  // Script
			INTERNAL_GUID_TO_STRING(MFMediaType_Image, 12);                   // Image
			INTERNAL_GUID_TO_STRING(MFMediaType_HTML, 12);                    // HTML
			INTERNAL_GUID_TO_STRING(MFMediaType_Binary, 12);                  // Binary
			INTERNAL_GUID_TO_STRING(MFMediaType_SAMI, 12);                    // SAMI
			INTERNAL_GUID_TO_STRING(MFMediaType_Protected, 12);               // Protected

			// Minor video type values
			// https://msdn.microsoft.com/en-us/library/windows/desktop/aa370819(v=vs.85).aspx
			INTERNAL_GUID_TO_STRING(MFVideoFormat_Base, 14);                  // Base
			INTERNAL_GUID_TO_STRING(MFVideoFormat_MP43, 14);                  // MP43
			INTERNAL_GUID_TO_STRING(MFVideoFormat_WMV1, 14);                  // WMV1
			INTERNAL_GUID_TO_STRING(MFVideoFormat_WMV2, 14);                  // WMV2
			INTERNAL_GUID_TO_STRING(MFVideoFormat_WMV3, 14);                  // WMV3
			INTERNAL_GUID_TO_STRING(MFVideoFormat_MPG1, 14);                  // MPG1
			INTERNAL_GUID_TO_STRING(MFVideoFormat_MPG2, 14);                  // MPG2
			INTERNAL_GUID_TO_STRING(MFVideoFormat_RGB24, 14);				  // RGB24
			INTERNAL_GUID_TO_STRING(MFVideoFormat_YUY2, 14);				  // YUY2
			INTERNAL_GUID_TO_STRING(MFVideoFormat_I420, 14);				  // I420

			// Minor audio type values
			INTERNAL_GUID_TO_STRING(MFAudioFormat_Base, 14);                  // Base
			INTERNAL_GUID_TO_STRING(MFAudioFormat_PCM, 14);                   // PCM
			INTERNAL_GUID_TO_STRING(MFAudioFormat_DTS, 14);                   // DTS
			INTERNAL_GUID_TO_STRING(MFAudioFormat_Dolby_AC3_SPDIF, 14);       // Dolby_AC3_SPDIF
			INTERNAL_GUID_TO_STRING(MFAudioFormat_Float, 14);                 // IEEEFloat
			INTERNAL_GUID_TO_STRING(MFAudioFormat_WMAudioV8, 14);             // WMAudioV8
			INTERNAL_GUID_TO_STRING(MFAudioFormat_WMAudioV9, 14);             // WMAudioV9
			INTERNAL_GUID_TO_STRING(MFAudioFormat_WMAudio_Lossless, 14);      // WMAudio_Lossless
			INTERNAL_GUID_TO_STRING(MFAudioFormat_WMASPDIF, 14);              // WMASPDIF
			INTERNAL_GUID_TO_STRING(MFAudioFormat_MP3, 14);                   // MP3
			INTERNAL_GUID_TO_STRING(MFAudioFormat_MPEG, 14);                  // MPEG

			// Media sub types
			INTERNAL_GUID_TO_STRING(WMMEDIASUBTYPE_I420, 15);                  // I420
			INTERNAL_GUID_TO_STRING(WMMEDIASUBTYPE_WVC1, 0);
			INTERNAL_GUID_TO_STRING(WMMEDIASUBTYPE_WMAudioV8, 0);
			INTERNAL_GUID_TO_STRING(MFImageFormat_RGB32, 0);

			// MP4 Media Subtypes.
			INTERNAL_GUID_TO_STRING(MF_MT_MPEG4_SAMPLE_DESCRIPTION, 6);
			INTERNAL_GUID_TO_STRING(MF_MT_MPEG4_CURRENT_SAMPLE_ENTRY, 6);
			//INTERNAL_GUID_TO_STRING(MFMPEG4Format_MP4A, 0);

		done:
			return pAttrStr;
		}
	};
}

