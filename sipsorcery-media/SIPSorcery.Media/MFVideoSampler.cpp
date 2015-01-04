#include "MFVideoSampler.h"

namespace SIPSorceryMedia {

	MFVideoSampler::MFVideoSampler()
	{ 
		if (!_isInitialised)
		{
			_isInitialised = true;
			CoInitializeEx(NULL, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
			MFStartup(MF_VERSION);
		}
	}

	MFVideoSampler::~MFVideoSampler()
	{
		if (_videoReader != NULL) {
			_videoReader->Release();
		}
	}

	void MFVideoSampler::Stop()
	{
		if (_videoReader != NULL) {
			_videoReader->Release();
			_videoReader = NULL;
		}
	}


	HRESULT MFVideoSampler::GetVideoDevices(/* out */ List<VideoMode^> ^% devices)
	{
		devices = gcnew List<VideoMode^>();

		IMFMediaSource *videoSource = NULL;
		UINT32 videoDeviceCount = 0;
		IMFAttributes *videoConfig = NULL;
		IMFActivate **videoDevices = NULL;
		IMFSourceReader *videoReader = NULL;

		// Create an attribute store to hold the search criteria.
		CHECK_HR(MFCreateAttributes(&videoConfig, 1), L"Error creating video configuation.");

		// Request video capture devices.
		CHECK_HR(videoConfig->SetGUID(
			MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
			MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID), L"Error initialising video configuration object.");

		// Enumerate the devices,
		CHECK_HR(MFEnumDeviceSources(videoConfig, &videoDevices, &videoDeviceCount), L"Error enumerating video devices.");

		for (int index = 0; index < videoDeviceCount; index++)
		{
			WCHAR *deviceFriendlyName;

			videoDevices[index]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, &deviceFriendlyName, NULL);

			// Request video capture device.
			CHECK_HR(videoDevices[index]->ActivateObject(IID_PPV_ARGS(&videoSource)), L"Error activating video device.");

			// Create a source reader.
			CHECK_HR(MFCreateSourceReaderFromMediaSource(
				videoSource,
				videoConfig,
				&videoReader), L"Error creating video source reader.");

			DWORD dwMediaTypeIndex = 0;
			HRESULT hr = S_OK;

			while (SUCCEEDED(hr))
			{
				IMFMediaType *pType = NULL;
				hr = videoReader->GetNativeMediaType(0, dwMediaTypeIndex, &pType);
				if (hr == MF_E_NO_MORE_TYPES)
				{
					hr = S_OK;
					break;
				}
				else if (SUCCEEDED(hr))
				{
					GUID videoSubType;
					UINT32 pWidth = 0, pHeight = 0;

					hr = pType->GetGUID(MF_MT_SUBTYPE, &videoSubType);
					MFGetAttributeSize(pType, MF_MT_FRAME_SIZE, &pWidth, &pHeight);

					auto videoMode = gcnew VideoMode();
					videoMode->DeviceFriendlyName = Marshal::PtrToStringUni((IntPtr)deviceFriendlyName);
					videoMode->DeviceIndex = index;
					videoMode->Width = pWidth;
					videoMode->Height = pHeight;
					videoMode->VideoSubType = FromGUID(videoSubType);
					devices->Add(videoMode);

					//devices->Add(Marshal::PtrToStringUni((IntPtr)deviceFriendlyName));

					pType->Release();
				}
				++dwMediaTypeIndex;
			}
		}

		return S_OK;
	}

	HRESULT MFVideoSampler::Init(int videoDeviceIndex, UInt32 width, UInt32 height)
	{
		const GUID MF_INPUT_FORMAT = MFVideoFormat_RGB24;
		IMFMediaSource *videoSource = NULL;
		UINT32 videoDeviceCount = 0;
		IMFAttributes *videoConfig = NULL;
		IMFActivate **videoDevices = NULL;
		IMFMediaType *videoType = NULL;
		IMFMediaType *desiredInputVideoType = NULL;

		_width = width;
		_height = height;

		// Create an attribute store to hold the search criteria.
		CHECK_HR(MFCreateAttributes(&videoConfig, 1), L"Error creating video configuation.");
		
		// Request video capture devices.
		CHECK_HR(videoConfig->SetGUID(
			MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
			MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID), L"Error initialising video configuration object.");

		// Enumerate the devices,
		CHECK_HR(MFEnumDeviceSources(videoConfig, &videoDevices, &videoDeviceCount), L"Error enumerating video devices.");

		printf("Video device Count: %i.\n", videoDeviceCount);

		if (videoDeviceIndex >= videoDeviceCount) {
			printf("Video device index %i is invalid.\n", videoDeviceIndex);
			return false;
		}
		else {

			// Request video capture device.
			CHECK_HR(videoDevices[videoDeviceIndex]->ActivateObject(IID_PPV_ARGS(&videoSource)), L"Error activating video device.");

			// Create the source readers. Need to pin the video reader as it's a managed resource being access by native code.
			cli::pin_ptr<IMFSourceReader*> pinnedVideoReader = &_videoReader;

			CHECK_HR(MFCreateSourceReaderFromMediaSource(
				videoSource,
				videoConfig,
				reinterpret_cast<IMFSourceReader**>(pinnedVideoReader)), L"Error creating video source reader.");

			FindVideoMode(_videoReader, MF_INPUT_FORMAT, width, height, desiredInputVideoType);

			if (desiredInputVideoType == NULL) {
				printf("The specified media type could not be found for the MF video reader.\n");
			}
			else {
				CHECK_HR(_videoReader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, NULL, desiredInputVideoType),
					L"Error setting video reader media type.\n");

				CHECK_HR(_videoReader->GetCurrentMediaType(
					(DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
					&videoType), L"Error retrieving current media type from first video stream.");

				long stride = -1;
				CHECK_HR(GetDefaultStride(videoType, &stride), L"There was an error retrieving the stride for the media type.");
				Stride = stride;

				// Get the frame dimensions and stride
				/*UINT32 nWidth, nHeight;
				LONG lFrameStride;
				MFGetAttributeSize(videoType, MF_MT_FRAME_SIZE, &nWidth, &nHeight);
				videoType->GetUINT32(MF_MT_DEFAULT_STRIDE, (UINT32*)&lFrameStride);*/

				// Register the color converter DSP for this process, in the video 
				// processor category. This will enable the sink writer to enumerate
				// the color converter when the sink writer attempts to match the
				// media types.
				CHECK_HR(MFTRegisterLocalByCLSID(
					__uuidof(CColorConvertDMO),
					MFT_CATEGORY_VIDEO_PROCESSOR,
					L"",
					MFT_ENUM_FLAG_SYNCMFT,
					0,
					NULL,
					0,
					NULL
					), L"Error registering colour converter DSP.");
			}

			videoConfig->Release();
			videoSource->Release();
			videoType->Release();
			desiredInputVideoType->Release();

			return S_OK;
		}
	}

	HRESULT MFVideoSampler::FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, UInt32 width, UInt32 height, /* out */ IMFMediaType *&foundpType)
	{
		HRESULT hr = NULL;
		DWORD dwMediaTypeIndex = 0;

		while (SUCCEEDED(hr))
		{
			IMFMediaType *pType = NULL;
			hr = pReader->GetNativeMediaType(0, dwMediaTypeIndex, &pType);
			if (hr == MF_E_NO_MORE_TYPES)
			{
				hr = S_OK;
				break;
			}
			else if (SUCCEEDED(hr))
			{
				// Examine the media type. (Not shown.)
				//CMediaTypeTrace *nativeTypeMediaTrace = new CMediaTypeTrace(pType);
				//printf("Native media type: %s.\n", nativeTypeMediaTrace->GetString());

				GUID videoSubType;
				UINT32 pWidth = 0, pHeight = 0;
				
				hr = pType->GetGUID(MF_MT_SUBTYPE, &videoSubType);
				MFGetAttributeSize(pType, MF_MT_FRAME_SIZE, &pWidth, &pHeight);

				if (SUCCEEDED(hr))
				{
					//printf("Video subtype %s, width=%i, height=%i.\n", STRING_FROM_GUID(videoSubType), pWidth, pHeight);

					if (videoSubType == mediaSubType && pWidth == width && pHeight == height)
					{
						foundpType = pType;
						printf("Media type successfully located.\n");
						break;
					}
				}

				pType->Release();
			}
			++dwMediaTypeIndex;
		}

		return S_OK;
	}

	HRESULT MFVideoSampler::GetSample(/* out */ array<Byte> ^% buffer)
	{
		if (_videoReader == NULL) {
			return -1;
		}
		else { 
			IMFSample *videoSample = NULL;
			DWORD streamIndex, flags;
			LONGLONG llVideoTimeStamp;

			// Initial read results in a null pSample??
			CHECK_HR(_videoReader->ReadSample(
				MF_SOURCE_READER_ANY_STREAM,    // Stream index.
				0,                              // Flags.
				&streamIndex,                   // Receives the actual stream index. 
				&flags,                         // Receives status flags.
				&llVideoTimeStamp,                   // Receives the time stamp.
				&videoSample                        // Receives the sample or NULL.
				), L"Error reading video sample.");

			if (!videoSample)
			{
				printf("Failed to get video sample from MF.\n");
			}
			else
			{
				DWORD nCurrBufferCount = 0;
				CHECK_HR(videoSample->GetBufferCount(&nCurrBufferCount), L"Failed to get the buffer count from the video sample.\n");

				IMFMediaBuffer * pMediaBuffer;
				CHECK_HR(videoSample->ConvertToContiguousBuffer(&pMediaBuffer), L"Failed to extract the video sample into a raw buffer.\n");

				DWORD nCurrLen = 0;
				CHECK_HR(pMediaBuffer->GetCurrentLength(&nCurrLen), L"Failed to get the length of the raw buffer holding the video sample.\n");

				byte *imgBuff;
				DWORD buffCurrLen = 0;
				DWORD buffMaxLen = 0;
				pMediaBuffer->Lock(&imgBuff, &buffMaxLen, &buffCurrLen);

				if (Stride != -1 && Stride < 0) {
					// Bitmap needs to be flipped.
					int bmpSize = buffCurrLen; // ToDo: Don't assume RGB/BGR 24.
					int absStride = Stride * -1;
					byte *flipBuf = new byte[bmpSize];

					for (int row = 0; row < _height; row++) {
						for (int col = 0; col < absStride; col += 3) {
							flipBuf[row * absStride + col] = imgBuff[((_height - row - 1) * absStride) + col];
							flipBuf[row * absStride + col + 1] = imgBuff[((_height - row - 1) * absStride) + col + 1];
							flipBuf[row * absStride + col + 2] = imgBuff[((_height - row - 1) * absStride) + col + 2];
						}
					}

					buffer = gcnew array<Byte>(buffCurrLen);
					Marshal::Copy((IntPtr)flipBuf, buffer, 0, buffCurrLen);

					delete flipBuf;
				}
				else {
					buffer = gcnew array<Byte>(buffCurrLen);
					Marshal::Copy((IntPtr)imgBuff, buffer, 0, buffCurrLen);
				}

				pMediaBuffer->Unlock();
				pMediaBuffer->Release();

				videoSample->Release();

				return S_OK;
			}
		}
	}

	HRESULT MFVideoSampler::GetDefaultStride(IMFMediaType *pType, /* out */ LONG *plStride)
	{
		LONG lStride = 0;

		// Try to get the default stride from the media type.
		HRESULT hr = pType->GetUINT32(MF_MT_DEFAULT_STRIDE, (UINT32*)&lStride);
		if (FAILED(hr))
		{
			// Attribute not set. Try to calculate the default stride.

			GUID subtype = GUID_NULL;

			UINT32 width = 0;
			UINT32 height = 0;

			// Get the subtype and the image size.
			hr = pType->GetGUID(MF_MT_SUBTYPE, &subtype);
			if (FAILED(hr))
			{
				goto done;
			}

			hr = MFGetAttributeSize(pType, MF_MT_FRAME_SIZE, &width, &height);
			if (FAILED(hr))
			{
				goto done;
			}

			hr = MFGetStrideForBitmapInfoHeader(subtype.Data1, width, &lStride);
			if (FAILED(hr))
			{
				goto done;
			}

			// Set the attribute for later reference.
			(void)pType->SetUINT32(MF_MT_DEFAULT_STRIDE, UINT32(lStride));
		}

		if (SUCCEEDED(hr))
		{
			*plStride = lStride;
		}

	done:
		return hr;
	}

	/*
	Or just go to http://msdn.microsoft.com/en-us/library/windows/desktop/dd757532(v=vs.85).aspx.
	*/
	void MFVideoSampler::DumpVideoSubTypes()
	{
		char* buffer;

		UuidToString((const _GUID *)&MFVideoFormat_RGB24, (RPC_WSTR*)&buffer);
		Console::WriteLine("RGB24: " + Marshal::PtrToStringUni((IntPtr)buffer));

		UuidToString((const _GUID *)&MFVideoFormat_I420, (RPC_WSTR*)&buffer);
		Console::WriteLine("I420: " + Marshal::PtrToStringUni((IntPtr)buffer));
	}
}
