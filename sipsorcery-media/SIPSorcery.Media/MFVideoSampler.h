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

#include <msclr\marshal.h>
#include <msclr\marshal_cppstd.h>

#include "VideoSubTypes.h"

#include <chrono>
#include <ctime>
#include <iostream>
#include <memory>
#include <string>

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Runtime::InteropServices;

#define CHECK_HR(hr, msg) if (hr != S_OK) { std::cout << msg << std::endl; return hr; }
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

  LPCSTR STRING_FROM_GUID(GUID Attr);
  std::string GetMediaTypeDescription(IMFMediaType * pMediaType);

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
    bool EndOfStream;
    String ^ Error;
    int Width;
    int Height;
    int Stride;
    Guid VideoSubType;
    String ^ VideoSubTypeFriendlyName;
    UInt64 Timestamp;
    UInt32 FrameCount;               // Number of audio of video frames contained in the raw sample.
    UInt64 NowMilliseconds;         // THe current time the sample was received in millisecodn resolution.

    MediaSampleProperties():
      Success(true),
      HasVideoSample(false),
      HasAudioSample(false),
      EndOfStream(false),
      Error(),
      Width(0),
      Height(0),
      Stride(0),
      VideoSubType(Guid::Empty),
      VideoSubTypeFriendlyName(),
      Timestamp(0),
      FrameCount(0)
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
    Guid VideoMajorType;
    Guid VideoMinorType;

		MFVideoSampler();
		~MFVideoSampler();
		HRESULT GetVideoDevices(/* out */ List<VideoMode^> ^% devices);
		HRESULT Init(int videoDeviceIndex, VideoSubTypesEnum videoSubType, UInt32 width, UInt32 height);
		HRESULT InitFromFile(String^ path);
		HRESULT FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, UInt32 width, UInt32 height, /* out */ IMFMediaType *&foundpType);
    MediaSampleProperties^ GetSample(/* out */ array<Byte> ^% buffer);
		HRESULT GetAudioSample(/* out */ array<Byte> ^% buffer);
    MediaSampleProperties^ GetNextSample(int streamTypeIndex, /* out */ array<Byte> ^% buffer, uint64_t delayUntil);
		HRESULT PlayAudio();
		void Stop();
		//void DumpVideoSubTypes();
		HRESULT MFVideoSampler::PlayTestAudio();
		HRESULT MFVideoSampler::PlayFileToSpeaker();

		property int Width {
			int get() { return _width; }
		}

		property int Height {
			int get() { return _height; }
		}

    property int Stride {
      int get() { return _stride; }
    }

	private:

		static BOOL _isInitialised = false;

		IMFSourceReader * _sourceReader = NULL;
		IMFMediaSink * _audioSink = NULL;             // Streaming audio renderer (SAR)
		DWORD videoStreamIndex;
		int _width, _height, _stride;

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
	};
}

