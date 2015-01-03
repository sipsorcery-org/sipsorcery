// http://msdn.microsoft.com/en-us/library/windows/desktop/aa473780%28v=vs.85%29.aspx bitmap orientation
// http://msdn.microsoft.com/en-us/library/windows/desktop/dd407212(v=vs.85).aspx Top-Down vs. Bottom-Up DIBs

#pragma once

#include <stdio.h>
#include <memory>
#include <mfapi.h>
#include <mfplay.h>
#include <mfapi.h>
#include <mftransform.h>
#include <mferror.h>
#include <mfobjects.h>
#include <mfreadwrite.h>
#include <errors.h>
#include <wmcodecdsp.h>

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Runtime::InteropServices;

#define CHECK_HR(hr, msg) if (HRHasFailed(hr, msg)) return hr;

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
	};

	public ref class MFVideoSampler
	{
	public:
		long Stride = -1;

		MFVideoSampler();
		~MFVideoSampler();
		//BOOL Init(int width, int height, const GUID MF_INPUT_FORMAT);
		HRESULT GetVideoDevices(/* out */ List<VideoMode^> ^% devices);
		HRESULT Init(int videoDeviceIndex, UInt32 width, UInt32 height);
		HRESULT FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, UInt32 width, UInt32 height, /* out */ IMFMediaType *&foundpType);
		HRESULT GetSample(/* out */ array<Byte> ^% buffer);
		void Stop();
		void DumpVideoSubTypes();
		
	private:

		static BOOL _isInitialised = false;

		IMFSourceReader * _videoReader = NULL;
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
	};
}

