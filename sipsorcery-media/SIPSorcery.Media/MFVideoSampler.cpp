#include "MFVideoSampler.h"

namespace SIPSorceryMedia {

  MFVideoSampler::MFVideoSampler()
  {
    if(!_isInitialised)
    {
      _isInitialised = true;
      CoInitializeEx(NULL, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
      MFStartup(MF_VERSION);

      // Register the color converter DSP for this process, in the video 
      // processor category. This will enable the sink writer to enumerate
      // the color converter when the sink writer attempts to match the
      // media types.
      MFTRegisterLocalByCLSID(
        __uuidof(CColorConvertDMO),
        MFT_CATEGORY_VIDEO_PROCESSOR,
        L"",
        MFT_ENUM_FLAG_SYNCMFT,
        0,
        NULL,
        0,
        NULL);
    }
  }

  MFVideoSampler::~MFVideoSampler()
  {
    if(_sourceReader != NULL) {
      _sourceReader->Release();
    }
  }

  void MFVideoSampler::Stop()
  {
    if(_sourceReader != NULL) {
      _sourceReader->Release();
      _sourceReader = NULL;
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

    for(int index = 0; index < videoDeviceCount; index++)
    {
      WCHAR *deviceFriendlyName;

      videoDevices[index]->GetAllocatedString(MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME, &deviceFriendlyName, NULL);

      //Console::WriteLine("Video device[{0}] name: {1}.", index, Marshal::PtrToStringUni((IntPtr)deviceFriendlyName));

      // Request video capture device.
      CHECK_HR(videoDevices[index]->ActivateObject(IID_PPV_ARGS(&videoSource)), L"Error activating video device.");

      // Create a source reader.
      CHECK_HR(MFCreateSourceReaderFromMediaSource(
        videoSource,
        videoConfig,
        &videoReader), L"Error creating video source reader.");

      DWORD dwMediaTypeIndex = 0;
      HRESULT hr = S_OK;

      while(SUCCEEDED(hr))
      {
        IMFMediaType *pType = NULL;
        hr = videoReader->GetNativeMediaType(0, dwMediaTypeIndex, &pType);
        if(hr == MF_E_NO_MORE_TYPES)
        {
          hr = S_OK;
          break;
        }
        else if(SUCCEEDED(hr))
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
          videoMode->VideoSubTypeFriendlyName = gcnew System::String(STRING_FROM_GUID(videoSubType));
          devices->Add(videoMode);

          //devices->Add(Marshal::PtrToStringUni((IntPtr)deviceFriendlyName));

          pType->Release();
        }
        ++dwMediaTypeIndex;
      }
    }

    return S_OK;
  }

  HRESULT MFVideoSampler::Init(int videoDeviceIndex, VideoSubTypesEnum videoSubType, UInt32 width, UInt32 height)
  {
    const GUID MF_INPUT_FORMAT = VideoSubTypesHelper::GetGuidForVideoSubType(videoSubType); //WMMEDIASUBTYPE_YUY2; // MFVideoFormat_YUY2; // MFVideoFormat_RGB24; //WMMEDIASUBTYPE_YUY2
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

    if(videoDeviceIndex >= videoDeviceCount) {
      printf("Video device index %i is invalid.\n", videoDeviceIndex);
      return false;
    }
    else {

      // Request video capture device.
      CHECK_HR(videoDevices[videoDeviceIndex]->ActivateObject(IID_PPV_ARGS(&videoSource)), L"Error activating video device.");

      // Create the source readers. Need to pin the video reader as it's a managed resource being access by native code.
      cli::pin_ptr<IMFSourceReader*> pinnedVideoReader = &_sourceReader;

      CHECK_HR(MFCreateSourceReaderFromMediaSource(
        videoSource,
        videoConfig,
        reinterpret_cast<IMFSourceReader**>(pinnedVideoReader)), L"Error creating video source reader.");

      FindVideoMode(_sourceReader, MF_INPUT_FORMAT, width, height, desiredInputVideoType);

      if(desiredInputVideoType == NULL) {
        printf("The specified media type could not be found for the MF video reader.\n");
      }
      else {
        CHECK_HR(_sourceReader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, NULL, desiredInputVideoType),
          L"Error setting video reader media type.\n");

        CHECK_HR(_sourceReader->GetCurrentMediaType(
          (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
          &videoType), L"Error retrieving current media type from first video stream.");

        long stride = -1;
        CHECK_HR(GetDefaultStride(videoType, &stride), L"There was an error retrieving the stride for the media type.");
        _stride = (int)stride;

        // Get the frame dimensions and stride
        /*UINT32 nWidth, nHeight;
        LONG lFrameStride;
        MFGetAttributeSize(videoType, MF_MT_FRAME_SIZE, &nWidth, &nHeight);
        videoType->GetUINT32(MF_MT_DEFAULT_STRIDE, (UINT32*)&lFrameStride);*/
      }

      videoConfig->Release();
      videoSource->Release();
      videoType->Release();
      desiredInputVideoType->Release();

      return S_OK;
    }
  }

  HRESULT MFVideoSampler::InitFromFile(String^ path)
  {
    MF_OBJECT_TYPE ObjectType = MF_OBJECT_INVALID;

    IMFSourceResolver* pSourceResolver = nullptr;
    IUnknown* uSource = nullptr;
    IMFMediaSource *mediaFileSource = nullptr;
    IMFAttributes *mediaFileConfig = nullptr;
    IMFMediaType *pVideoOutType = nullptr, *pAudioOutType = nullptr;
    IMFMediaType *videoType = nullptr;
    IMFMediaType *audioType = nullptr;

    IMFPresentationDescriptor *pSourcePD = nullptr;
    DWORD sourceStreamCount = 0;
    IMFMediaSession *pSession = nullptr;

    std::wstring pathNative = msclr::interop::marshal_as<std::wstring>(path);

    // Create the source resolver.
    CHECK_HR(MFCreateSourceResolver(&pSourceResolver), L"MFCreateSourceResolver failed.");

    // Use the source resolver to create the media source.
    CHECK_HR(pSourceResolver->CreateObjectFromURL(
      pathNative.c_str(),
      MF_RESOLUTION_MEDIASOURCE,  // Create a source object.
      NULL,                       // Optional property store.
      &ObjectType,        // Receives the created object type. 
      &uSource            // Receives a pointer to the media source.
    ), L"CreateObjectFromURL failed.");

    // Get the IMFMediaSource interface from the media source.
    CHECK_HR(uSource->QueryInterface(IID_PPV_ARGS(&mediaFileSource)), L"Failed to get IMFMediaSource.");

    //CHECK_HR(mediaFileSource->CreatePresentationDescriptor(&pSourcePD), L"Failed to create presentation descriptor from source.\n");

    //// Get the number of streams in the media source.
    //CHECK_HR(pSourcePD->GetStreamDescriptorCount(&sourceStreamCount), L"Failed to get source stream count.\n");

    //printf("Source stream count %i.\n", sourceStreamCount);

    //CHECK_HR(MFCreateMediaSession(NULL, &pSession), L"Failed to create media session.\n");

    //IMFMediaEvent * mediaEvent = nullptr;
    //DWORD flags = 0;
    //CHECK_HR(pSession->GetEvent(flags, &mediaEvent), L"Failed to get event from session.");

    CHECK_HR(MFCreateAttributes(&mediaFileConfig, 2), L"Failed to create MF atttributes.");;

    CHECK_HR(mediaFileConfig->SetGUID(
      MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
      MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID), L"Failed to set dev source attribute type for reader config.");

    CHECK_HR(mediaFileConfig->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1), L"Failed to set enable video processing attribute type for reader config.");

    // Create the source readers. Need to pin the video reader as it's a managed resource being access by native code.
    cli::pin_ptr<IMFSourceReader*> pinnedVideoReader = &_sourceReader;

    CHECK_HR(MFCreateSourceReaderFromMediaSource(
      mediaFileSource,
      mediaFileConfig,
      reinterpret_cast<IMFSourceReader**>(pinnedVideoReader)), L"Error creating video source reader.");

    CHECK_HR(_sourceReader->GetCurrentMediaType(
      (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
      &videoType), L"Error retrieving current media type from first video stream.");

    Console::WriteLine("Source File Video Description:");
    std::cout << GetMediaTypeDescription(videoType) << std::endl;

    CHECK_HR(MFCreateMediaType(&pVideoOutType), L"Failed to create output media type.");
    CHECK_HR(pVideoOutType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), L"Failed to set output media major type.");
    //CHECK_HR(pVideoOutType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB24), L"Failed to set output media sub type (RGB24).");
    //CHECK_HR(pVideoOutType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32), L"Failed to set output media sub type (RGB32)."); // **
    //CHECK_HR(pVideoOutType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12), L"Failed to set output media sub type (NV12).");
    //CHECK_HR(pVideoOutType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_YV12), L"Failed to set output media sub type (NV12).");
    CHECK_HR(pVideoOutType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_I420), L"Failed to set output media sub type (I420).");
    //CHECK_HR(pVideoOutType->SetUINT32(MF_MT_FRAME_RATE, 10), L"Failed to set the output frame rate.");
    //CHECK_HR(pVideoOutType->SetUINT32(MF_MT_AVG_BITRATE, 308722), L"Failed to set the video output average bit rate."); // Original was 617544.
    //CHECK_HR(pVideoOutType->SetUINT32(MF_MT_FIXED_SIZE_SAMPLES, FALSE), L"Failed to set the video output fixed size samples.");

    CHECK_HR(_sourceReader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, NULL, pVideoOutType),
      L"Error setting video reader media type.\n");

    CHECK_HR(_sourceReader->GetCurrentMediaType(
      (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
      &videoType), L"Error retrieving current media type from first video stream.");

    Console::WriteLine("Output Video Description:");
    std::cout << GetMediaTypeDescription(videoType) << std::endl;

    GUID majorVidType;
    videoType->GetMajorType(&majorVidType);
    VideoMajorType = FromGUID(majorVidType);

    /* PROPVARIANT subVidType;
     videoType->GetItem(MF_MT_SUBTYPE, &subVidType);
     subVidType*/

     // Get the frame dimensions and stride
    UINT32 nWidth, nHeight;
    MFGetAttributeSize(videoType, MF_MT_FRAME_SIZE, &nWidth, &nHeight);
    _width = nWidth;
    _height = nHeight;

    long stride = -1;
    CHECK_HR(GetDefaultStride(videoType, &stride), L"There was an error retrieving the stride for the media type.");
    _stride = (int)stride;

    // Set audio type.
    CHECK_HR(_sourceReader->GetCurrentMediaType(
      (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,
      &audioType), L"Error retrieving current type from first audio stream.");

    std::cout << GetMediaTypeDescription(audioType) << std::endl;

    CHECK_HR(MFCreateMediaType(&pAudioOutType), L"Failed to create output media type.");
    CHECK_HR(pAudioOutType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), L"Failed to set output media major type.");
    CHECK_HR(pAudioOutType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM), L"Failed to set output audio sub type (PCM).");
    //CHECK_HR(pAudioOutType->SetGUID(MF_MT_SUBTYPE, WAVE_FORMAT_MULAW), L"Failed to set output audio sub type (MULAW).");
    //CHECK_HR(pAudioOutType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float), L"Failed to set output audio sub type (Float).");
    //CHECK_HR(pAudioOutType->SetUINT64(MF_MT_AUDIO_SAMPLES_PER_SECOND, 48000), L"Failed to set output audio samples per second (Float).");
    CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 1), L"Failed to set audio output to mono.");
    CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16), L"Failed to set audio bits per sample.");
    CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 8000), L"Failed to set audio samples per second.");
    //CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 16000), L"Failed to set the audio average bytes per second.");

    CHECK_HR(_sourceReader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, NULL, pAudioOutType),
      L"Error setting reader audio type.\n");

    CHECK_HR(_sourceReader->GetCurrentMediaType(
      (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,
      &audioType), L"Error retrieving current type from first audio stream.");

    Console::WriteLine("Output Audio Description:");
    std::cout << GetMediaTypeDescription(audioType) << std::endl;

    //done:
    //SafeRelease(&pSourceResolver);
    //SafeRelease(&pSource);
    videoType->Release();
    audioType->Release();

    return S_OK;
  }

  HRESULT MFVideoSampler::FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, UInt32 width, UInt32 height, /* out */ IMFMediaType *&foundpType)
  {
    HRESULT hr = NULL;
    DWORD dwMediaTypeIndex = 0;

    while(SUCCEEDED(hr))
    {
      IMFMediaType *pType = NULL;
      hr = pReader->GetNativeMediaType(0, dwMediaTypeIndex, &pType);
      if(hr == MF_E_NO_MORE_TYPES)
      {
        hr = S_OK;
        break;
      }
      else if(SUCCEEDED(hr))
      {
        // Examine the media type. (Not shown.)
        //CMediaTypeTrace *nativeTypeMediaTrace = new CMediaTypeTrace(pType);
        //printf("Native media type: %s.\n", nativeTypeMediaTrace->GetString());

        GUID videoSubType;
        UINT32 pWidth = 0, pHeight = 0;

        hr = pType->GetGUID(MF_MT_SUBTYPE, &videoSubType);
        MFGetAttributeSize(pType, MF_MT_FRAME_SIZE, &pWidth, &pHeight);

        if(SUCCEEDED(hr))
        {
          //printf("Video subtype %s, width=%i, height=%i.\n", STRING_FROM_GUID(videoSubType), pWidth, pHeight);

          if(videoSubType == mediaSubType && pWidth == width && pHeight == height)
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

  MediaSampleProperties^ MFVideoSampler::GetSample(/* out */ array<Byte> ^% buffer)
  {
    MediaSampleProperties^ sampleProps = gcnew MediaSampleProperties();

    if(_sourceReader == NULL) {
      sampleProps->Success = false;
      return sampleProps;
    }
    else {
      IMFSample *videoSample = NULL;
      DWORD streamIndex, flags;
      LONGLONG llVideoTimeStamp;

      // Initial read results in a null pSample??
      CHECK_HR_EXTENDED(_sourceReader->ReadSample(
        //MF_SOURCE_READER_ANY_STREAM,    // Stream index.
        MF_SOURCE_READER_FIRST_VIDEO_STREAM,
        0,                              // Flags.
        &streamIndex,                   // Receives the actual stream index. 
        &flags,                         // Receives status flags.
        &llVideoTimeStamp,                   // Receives the time stamp.
        &videoSample                        // Receives the sample or NULL.
      ), L"Error reading video sample.");

      if(flags & MF_SOURCE_READERF_ENDOFSTREAM)
      {
        wprintf(L"\tEnd of stream\n");
      }
      if(flags & MF_SOURCE_READERF_NEWSTREAM)
      {
        wprintf(L"\tNew stream\n");
      }
      if(flags & MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED)
      {
        wprintf(L"\tNative type changed\n");
      }
      if(flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)
      {
        wprintf(L"\tCurrent type changed\n");

        IMFMediaType *videoType = NULL;
        CHECK_HR_EXTENDED(_sourceReader->GetCurrentMediaType(
          (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
          &videoType), L"Error retrieving current media type from first video stream.");

        std::cout << GetMediaTypeDescription(videoType) << std::endl;

        // Get the frame dimensions and stride
        UINT32 nWidth, nHeight;
        CHECK_HR_EXTENDED(MFGetAttributeSize(videoType, MF_MT_FRAME_SIZE, &nWidth, &nHeight), L"There was an error retrieving the dimensions for the media type.");
        _width = nWidth;
        _height = nHeight;

        long stride = -1;
        CHECK_HR_EXTENDED(GetDefaultStride(videoType, &stride), L"There was an error retrieving the stride for the media type.");
        _stride = (int)stride;

        sampleProps->Width = nWidth;
        sampleProps->Height = nHeight;
        sampleProps->Stride = stride;

        videoType->Release();
      }
      if(flags & MF_SOURCE_READERF_STREAMTICK)
      {
        wprintf(L"\tStream tick\n");
      }

      if(!videoSample)
      {
        printf("Failed to get video sample from MF.\n");
      }
      else
      {
        DWORD nCurrBufferCount = 0;
        CHECK_HR_EXTENDED(videoSample->GetBufferCount(&nCurrBufferCount), L"Failed to get the buffer count from the video sample.\n");

        IMFMediaBuffer * pMediaBuffer;
        CHECK_HR_EXTENDED(videoSample->ConvertToContiguousBuffer(&pMediaBuffer), L"Failed to extract the video sample into a raw buffer.\n");

        DWORD nCurrLen = 0;
        CHECK_HR_EXTENDED(pMediaBuffer->GetCurrentLength(&nCurrLen), L"Failed to get the length of the raw buffer holding the video sample.\n");

        byte *imgBuff;
        DWORD buffCurrLen = 0;
        DWORD buffMaxLen = 0;
        pMediaBuffer->Lock(&imgBuff, &buffMaxLen, &buffCurrLen);

        buffer = gcnew array<Byte>(buffCurrLen);
        Marshal::Copy((IntPtr)imgBuff, buffer, 0, buffCurrLen);

        pMediaBuffer->Unlock();
        pMediaBuffer->Release();

        videoSample->Release();

        return sampleProps;
      }
    }
  }

  HRESULT MFVideoSampler::GetAudioSample(/* out */ array<Byte> ^% buffer)
  {
    if(_sourceReader == NULL) {
      return -1;
    }
    else {
      IMFSample *audioSample = NULL;
      DWORD streamIndex, flags;
      LONGLONG llVideoTimeStamp;

      // Initial read results in a null pSample??
      CHECK_HR(_sourceReader->ReadSample(
        MF_SOURCE_READER_FIRST_AUDIO_STREAM,
        0,                              // Flags.
        &streamIndex,                   // Receives the actual stream index. 
        &flags,                         // Receives status flags.
        &llVideoTimeStamp,                   // Receives the time stamp.
        &audioSample                        // Receives the sample or NULL.
      ), L"Error reading audio sample.");

      if(flags & MF_SOURCE_READERF_ENDOFSTREAM)
      {
        wprintf(L"\tEnd of stream\n");
      }
      if(flags & MF_SOURCE_READERF_NEWSTREAM)
      {
        wprintf(L"\tNew stream\n");
      }
      if(flags & MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED)
      {
        wprintf(L"\tNative type changed\n");
      }
      if(flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)
      {
        wprintf(L"\tCurrent type changed\n");

        IMFMediaType *audioType = NULL;
        CHECK_HR(_sourceReader->GetCurrentMediaType(
          (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,
          &audioType), L"Error retrieving current media type from first audio stream.");

        std::cout << GetMediaTypeDescription(audioType) << std::endl;

        audioType->Release();
      }
      if(flags & MF_SOURCE_READERF_STREAMTICK)
      {
        wprintf(L"\tStream tick\n");
      }

      if(!audioSample)
      {
        printf("Failed to get audio sample from MF.\n");
      }
      else
      {
        DWORD nCurrBufferCount = 0;
        CHECK_HR(audioSample->GetBufferCount(&nCurrBufferCount), L"Failed to get the buffer count from the audio sample.\n");
        //Console::WriteLine("Buffer count " + nCurrBufferCount);

        IMFMediaBuffer * pMediaBuffer;
        CHECK_HR(audioSample->ConvertToContiguousBuffer(&pMediaBuffer), L"Failed to extract the audio sample into a raw buffer.\n");

        DWORD nCurrLen = 0;
        CHECK_HR(pMediaBuffer->GetCurrentLength(&nCurrLen), L"Failed to get the length of the raw buffer holding the audio sample.\n");

        byte *audioBuff;
        DWORD buffCurrLen = 0;
        DWORD buffMaxLen = 0;
        pMediaBuffer->Lock(&audioBuff, &buffMaxLen, &buffCurrLen);

        buffer = gcnew array<Byte>(buffCurrLen);
        Marshal::Copy((IntPtr)audioBuff, buffer, 0, buffCurrLen);

        //for(int i=0; i<buffCurrLen; i++) {
        //  buffer[i] = (buffer[i] < 0x80) ? 0x80 | buffer[i] : 0x7f & buffer[i]; // Convert from an 8 bit unsigned sample to an 8 bit 2's complement signed sample.
        //}

        pMediaBuffer->Unlock();
        pMediaBuffer->Release();

        audioSample->Release();

        return S_OK;
      }
    }
  }

  // Gets the next available sample from the source reader.
  MediaSampleProperties^ MFVideoSampler::GetNextSample(int streamTypeIndex, /* out */ array<Byte> ^% buffer, uint64_t delayUntil)
  {
    MediaSampleProperties^ sampleProps = gcnew MediaSampleProperties();

    if(_sourceReader == nullptr) {
      sampleProps->Success = false;
      return sampleProps;
    }
    else {
      IMFSample *sample = nullptr;
      DWORD streamIndex, flags;
      LONGLONG sampleTimestamp;

      CHECK_HR_EXTENDED(_sourceReader->ReadSample(
        streamTypeIndex, // MF_SOURCE_READER_ANY_STREAM or MF_SOURCE_READER_FIRST_AUDIO_STREAM or MF_SOURCE_READER_FIRST_VIDEO_STREAM
        0,                              // Flags.
        &streamIndex,                   // Receives the actual stream index. 
        &flags,                         // Receives status flags.
        &sampleTimestamp,               // Receives the time stamp.
        &sample                         // Receives the sample or NULL.
      ), L"Error reading media sample.");

      if(flags & MF_SOURCE_READERF_ENDOFSTREAM)
      {
        std::cout << "End of stream." << std::endl;
        sampleProps->EndOfStream = true;
      }
      else
      {
        if(flags & MF_SOURCE_READERF_NEWSTREAM)
        {
          std::cout << "New stream." << std::endl;
        }
        if(flags & MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED)
        {
          std::cout << "Native type changed." << std::endl;
        }
        if(flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)
        {
          std::cout << "Current type changed for stream index " << streamIndex << "." << std::endl;

          IMFMediaType *videoType = nullptr;
          CHECK_HR_EXTENDED(_sourceReader->GetCurrentMediaType(
            (DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
            &videoType), L"Error retrieving current media type from first video stream.");

          std::cout << GetMediaTypeDescription(videoType) << std::endl;

          // Get the frame dimensions and stride
          UINT32 nWidth, nHeight;
          MFGetAttributeSize(videoType, MF_MT_FRAME_SIZE, &nWidth, &nHeight);
          _width = nWidth;
          _height = nHeight;

          LONG lFrameStride;
          videoType->GetUINT32(MF_MT_DEFAULT_STRIDE, (UINT32*)&lFrameStride);

          sampleProps->Width = nWidth;
          sampleProps->Height = nHeight;
          sampleProps->Stride = lFrameStride;

          videoType->Release();
        }
        if(flags & MF_SOURCE_READERF_STREAMTICK)
        {
          std::cout << "Stream tick." << std::endl;
        }

        if(sample == nullptr)
        {
          std::cout << "Failed to get media sample in from source reader." << std::endl;
        }
        else
        {
          //std::cout << "Stream index " << streamIndex << ", timestamp " <<  sampleTimestamp << ", flags " << flags << "." << std::endl;

          // Accroding to https://docs.microsoft.com/en-us/windows/win32/api/mfreadwrite/nf-mfreadwrite-imfsourcereader-readsample 
          // the timestamp is in 100ns units.
          sampleProps->Timestamp = sampleTimestamp;
          sampleProps->NowMilliseconds = std::chrono::milliseconds(std::time(NULL)).count();

          // TODO: Get the stream indexes of the frist audio and video stream properly rather than relying on default values.
          if(streamIndex == 1)
          {
            //std::cout << "video:" << sampleTimestamp / 10000 << "." << std::endl;

            DWORD nCurrBufferCount = 0;
            CHECK_HR_EXTENDED(sample->GetBufferCount(&nCurrBufferCount), L"Failed to get the buffer count from the video sample.\n");
            sampleProps->FrameCount = nCurrBufferCount;

            IMFMediaBuffer * pVideoBuffer;
            CHECK_HR_EXTENDED(sample->ConvertToContiguousBuffer(&pVideoBuffer), L"Failed to extract the video sample into a raw buffer.\n");

            DWORD nCurrLen = 0;
            CHECK_HR_EXTENDED(pVideoBuffer->GetCurrentLength(&nCurrLen), L"Failed to get the length of the raw buffer holding the video sample.\n");

            byte *imgBuff;
            DWORD buffCurrLen = 0;
            DWORD buffMaxLen = 0;
            pVideoBuffer->Lock(&imgBuff, &buffMaxLen, &buffCurrLen);

            buffer = gcnew array<Byte>(buffCurrLen);
            Marshal::Copy((IntPtr)imgBuff, buffer, 0, buffCurrLen);

            pVideoBuffer->Unlock();
            pVideoBuffer->Release();

            sampleProps->HasVideoSample = true;

            sample->Release();

            //Sleep(20);
          }
          else if(streamIndex == 0)
          {
            //std::cout << "audio:" << sampleTimestamp / 10000 << "." << std::endl;

            DWORD nCurrBufferCount = 0;
            CHECK_HR_EXTENDED(sample->GetBufferCount(&nCurrBufferCount), L"Failed to get the buffer count from the audio sample.\n");
            sampleProps->FrameCount = nCurrBufferCount;

            IMFMediaBuffer* pAudioBuffer;
            CHECK_HR_EXTENDED(sample->ConvertToContiguousBuffer(&pAudioBuffer), L"Failed to extract the audio sample into a raw buffer.\n");

            DWORD nCurrLen = 0;
            CHECK_HR_EXTENDED(pAudioBuffer->GetCurrentLength(&nCurrLen), L"Failed to get the length of the raw buffer holding the audio sample.\n");

            byte *audioBuff;
            DWORD buffCurrLen = 0;
            DWORD buffMaxLen = 0;
            pAudioBuffer->Lock(&audioBuff, &buffMaxLen, &buffCurrLen);

            buffer = gcnew array<Byte>(buffCurrLen);
            Marshal::Copy((IntPtr)audioBuff, buffer, 0, buffCurrLen);

            //for(int i=0; i<buffCurrLen; i++) {
            //  buffer[i] = (buffer[i] < 0x80) ? 0x80 | buffer[i] : 0x7f & buffer[i]; // Convert from an 8 bit unsigned sample to an 8 bit 2's complement signed sample.
            //}

            pAudioBuffer->Unlock();
            pAudioBuffer->Release();

            sampleProps->HasAudioSample = true;

            sample->Release();

            //Sleep(5);

           /* auto now = std::chrono::milliseconds(std::time(NULL)).count();
            if(delayUntil > now)
            {
              std::cout << "sample delivery delay " << delayUntil - now << "." << std::endl;
              Sleep(delayUntil - now);
            }*/
          }
        }
      } // End of sample.

      return sampleProps;
    }
  }

  HRESULT MFVideoSampler::PlayAudio()
  {
    HRESULT hr = S_OK;

    IMMDeviceEnumerator *pEnum = NULL;      // Audio device enumerator.
    //IMMDeviceCollection *pDevices = NULL;   // Audio device collection.
    IMMDevice *pDevice = NULL;              // An audio device.
    IMFAttributes *pAttributes = NULL;      // Attribute store.
    LPWSTR wstrID = NULL;                   // Device ID.
    IMFMediaSink *pAudioSink = NULL;
    IMFSinkWriter *pSinkWriter = NULL;
    //IMFAttributes * pAudioOutType = NULL;
    IMFStreamSink *pStreamSink = NULL;
    IMFMediaTypeHandler *pMediaTypeHandler = NULL;
    IMFMediaType *pMediaType = NULL;
    IMFMediaType *pSinkMediaType = NULL;
    DWORD mediaTypeCount;

    // Create the device enumerator.
    /*CHECK_HR(CoCreateInstance(
      __uuidof(MMDeviceEnumerator),
      NULL,
      CLSCTX_ALL,
      __uuidof(IMMDeviceEnumerator),
      (void**)&pEnum
      ), L"Failed to create MF audio device enumberator.");*/

      //CHECK_HR(pEnum->GetDefaultAudioEndpoint(eRender, eMultimedia, &pDevice), L"Failed to get default audio end point.");
      //hr = pMmDevice->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (VOID**)&pAudioClient);

      // Enumerate the rendering devices.
      //if (SUCCEEDED(hr))
      //{
      //	hr = pEnum->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &pDevices);
      //}

      //// Get ID of the first device in the list.
      //if (SUCCEEDED(hr))
      //{
      //	hr = pDevices->Item(0, &pDevice);
      //}

      //CHECK_HR(pDevice->GetId(&wstrID), L"Failed to get audio device ID.");
      //wprintf(L"Audio device ID %s.\n", wstrID);

      // Create an attribute store and set the device ID attribute.
      //CHECK_HR(MFCreateAttributes(&pAttributes, 2), L"Failed to create IMFAttributes object.");

      //CHECK_HR(pAttributes->SetString(MF_AUDIO_RENDERER_ATTRIBUTE_ENDPOINT_ID, wstrID), L"Failed so set audio render string.");

      // Create the audio renderer.
      // Create the source readers. Need to pin the video reader as it's a managed resource being access by native code.
      //cli::pin_ptr<IMFMediaSink*> pinnedAudioSink = &_audioSink;

      //hr = MFCreateAudioRenderer(pAttributes, reinterpret_cast<IMFMediaSink**>(pinnedAudioSink));
      //CHECK_HR(MFCreateAudioRenderer(pAttributes, &pAudioSink), L"Failed to create audio sink.");
    CHECK_HR(MFCreateAudioRenderer(NULL, &pAudioSink), L"Failed to create audio sink.");

    /*CHECK_HR(_sourceReader->GetCurrentMediaType(
      (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,
      &audioType), L"Error retrieving current type from first audio stream.");

    Console::WriteLine(GetMediaTypeDescription(audioType));*/

    /*CHECK_HR(MFCreateAttributes(&pAudioOutType, 2), L"Failed to create IMFAttributes object for audio out.");

    CHECK_HR(pAudioOutType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), L"Failed to set audio output media major type.");
    CHECK_HR(pAudioOutType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM), L"Failed to set audio output audio sub type (PCM).");*/

    /*IMFMediaSink *pAudioRenderer = NULL;
    IMFSinkWriter *pSink = NULL;
    IMFStreamSink *pStreamSink = NULL;
    IMFMediaTypeHandler *pMediaTypeHandler = NULL;
    IMFMediaType *pMediaType = NULL;*/

    //EIF(MFCreateAudioRenderer(NULL, &pAudioRenderer));
    CHECK_HR(pAudioSink->GetStreamSinkByIndex(0, &pStreamSink), L"Failed to get audio renderer stream by index.");

    CHECK_HR(pStreamSink->GetMediaTypeHandler(&pMediaTypeHandler), L"Failed to get media type handler.");

    /*CHECK_HR(pMediaTypeHandler->GetMediaTypeCount(&mediaTypeCount), L"Failed to get media type count.");

    printf("Media type count %i.\n", mediaTypeCount);

    for (int index = 0; index < mediaTypeCount; index++)
    {
      CHECK_HR(pMediaTypeHandler->GetMediaTypeByIndex(index, &pSinkMediaType), L"Failed to get sink media type.");
      Console::WriteLine(GetMediaTypeDescription(pSinkMediaType));
    }*/

    //CHECK_HR(pMediaTypeHandler->GetCurrentMediaType(&pSinkMediaType), L"Failed to get sink media type.");

    //CHECK_HR(MFCreateMediaType(&pMediaType), L"Failed to instantiate media type.");
    //CHECK_HR(pMediaType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), L"Failed to set major media type to audio.");
    //CHECK_HR(pMediaType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM), L"Failed to set sub type to PCM.");
    //CHECK_HR(pMediaType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2), L"Failed to set number audio channels.");
    //CHECK_HR(pMediaType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 22050), L"Failed to set samples per second.");
    //CHECK_HR(pMediaType->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, 4), L"Failed to set audio block alignment.");
    //CHECK_HR(pMediaType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 88200), L"Failed to set average bytes per second.");
    //CHECK_HR(pMediaType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16), L"Failed to set audio bits per sample.");
    //CHECK_HR(pMediaType->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE), L"Failed to set all samples independent.");

    CHECK_HR(pMediaTypeHandler->GetMediaTypeByIndex(2, &pSinkMediaType), L"Failed to get sink media type.");
    std::cout << GetMediaTypeDescription(pSinkMediaType) << std::endl;

    CHECK_HR(pMediaTypeHandler->SetCurrentMediaType(pSinkMediaType), L"Failed to set current media type.");

    //EIF(MFCreateSinkWriterFromMediaSink(pAudioRenderer, NULL, &pSink));

    CHECK_HR(MFCreateSinkWriterFromMediaSink(pAudioSink, NULL, &pSinkWriter), L"Failed to create sink writer from audio sink.");

    //MFCreateSinkWriterFromMediaSink(pAudioSink, NULL, &pSinkWriter);

    //pEnum->Release();
    //pDevices->Release();
    //pDevice->Release();
    //pAttributes->Release();
    //CoTaskMemFree(wstrID);

    if(_sourceReader == NULL) {
      return -1;
    }
    else {

      printf("Commencing audio play.\n");

      IMFSample *audioSample = NULL;
      DWORD streamIndex, flags;
      LONGLONG llVideoTimeStamp;

      for(int index = 0; index < 10; index++)
        //while (true)
      {
        // Initial read results in a null pSample??
        CHECK_HR(_sourceReader->ReadSample(
          MF_SOURCE_READER_FIRST_AUDIO_STREAM,
          0,                              // Flags.
          &streamIndex,                   // Receives the actual stream index. 
          &flags,                         // Receives status flags.
          &llVideoTimeStamp,                   // Receives the time stamp.
          &audioSample                        // Receives the sample or NULL.
        ), L"Error reading audio sample.");

        if(flags & MF_SOURCE_READERF_ENDOFSTREAM)
        {
          wprintf(L"\tEnd of stream\n");
          break;
        }
        if(flags & MF_SOURCE_READERF_NEWSTREAM)
        {
          wprintf(L"\tNew stream\n");
        }
        if(flags & MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED)
        {
          wprintf(L"\tNative type changed\n");
        }
        if(flags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)
        {
          wprintf(L"\tCurrent type changed\n");

          IMFMediaType *audioType = NULL;
          CHECK_HR(_sourceReader->GetCurrentMediaType(
            (DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,
            &audioType), L"Error retrieving current media type from first audio stream.");

          std::cout << GetMediaTypeDescription(audioType) << std::endl;

          audioType->Release();
        }
        if(flags & MF_SOURCE_READERF_STREAMTICK)
        {
          wprintf(L"\tStream tick\n");

          pSinkWriter->SendStreamTick(0, llVideoTimeStamp);
        }

        if(!audioSample)
        {
          printf("Failed to get audio sample from MF.\n");
        }
        else
        {
          CHECK_HR(audioSample->SetSampleTime(llVideoTimeStamp), L"Error setting the audio sample time.");

          //DWORD nCurrBufferCount = 0;
          //CHECK_HR(audioSample->GetBufferCount(&nCurrBufferCount), L"Failed to get the buffer count from the audio sample.\n");

          //printf("Buffer count %i.\n", nCurrBufferCount);

          CHECK_HR(pSinkWriter->WriteSample(0, audioSample), L"The stream sink writer was not happy with the sample.");
          //CHECK_HR(pStreamSink->ProcessSample(audioSample), L"The stream sink was not happy with the sample.");

          //IMFMediaBuffer * pMediaBuffer;
          //CHECK_HR(audioSample->ConvertToContiguousBuffer(&pMediaBuffer), L"Failed to extract the audio sample into a raw buffer.\n");

          //DWORD nCurrLen = 0;
          //CHECK_HR(pMediaBuffer->GetCurrentLength(&nCurrLen), L"Failed to get the length of the raw buffer holding the audio sample.\n");

          //byte *audioBuff;
          //DWORD buffCurrLen = 0;
          //DWORD buffMaxLen = 0;
          //pMediaBuffer->Lock(&audioBuff, &buffMaxLen, &buffCurrLen);

          /*buffer = gcnew array<Byte>(buffCurrLen);
          //Marshal::Copy((IntPtr)audioBuff, buffer, 0, buffCurrLen);*/

          //pMediaBuffer->Unlock();
          //pMediaBuffer->Release();

          //audioSample->Release();

          //return S_OK;
        }
      }
    }
  }

  HRESULT MFVideoSampler::GetDefaultStride(IMFMediaType *pType, /* out */ LONG *plStride)
  {
    LONG lStride = 0;

    // Try to get the default stride from the media type.
    HRESULT hr = pType->GetUINT32(MF_MT_DEFAULT_STRIDE, (UINT32*)&lStride);
    if(FAILED(hr))
    {
      // Attribute not set. Try to calculate the default stride.

      GUID subtype = GUID_NULL;

      UINT32 width = 0;
      UINT32 height = 0;

      // Get the subtype and the image size.
      hr = pType->GetGUID(MF_MT_SUBTYPE, &subtype);
      if(FAILED(hr))
      {
        goto done;
      }

      hr = MFGetAttributeSize(pType, MF_MT_FRAME_SIZE, &width, &height);
      if(FAILED(hr))
      {
        goto done;
      }

      hr = MFGetStrideForBitmapInfoHeader(subtype.Data1, width, &lStride);
      if(FAILED(hr))
      {
        goto done;
      }

      // Set the attribute for later reference.
      (void)pType->SetUINT32(MF_MT_DEFAULT_STRIDE, UINT32(lStride));
    }

    if(SUCCEEDED(hr))
    {
      *plStride = lStride;
    }

  done:
    return hr;
  }

  LPCSTR STRING_FROM_GUID(GUID Attr)
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
    INTERNAL_GUID_TO_STRING(MF_MT_DEFAULT_STRIDE, 6);				          // STRIDE
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
    INTERNAL_GUID_TO_STRING(MFVideoFormat_RGB24, 14);				          // RGB24
    INTERNAL_GUID_TO_STRING(MFVideoFormat_YUY2, 14);				          // YUY2
    INTERNAL_GUID_TO_STRING(MFVideoFormat_YV12, 14);                   // YV12
    INTERNAL_GUID_TO_STRING(MFVideoFormat_I420, 14);				          // I420

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
    INTERNAL_GUID_TO_STRING(MFAudioFormat_AAC, 14);                   // AAC

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

  std::string GetMediaTypeDescription(IMFMediaType * pMediaType)
  {
    HRESULT hr = S_OK;
    GUID MajorType;
    UINT32 cAttrCount;
    LPCSTR pszGuidStr;
    std::string description;
    WCHAR TempBuf[200];

    if(pMediaType == NULL)
    {
      description = "<NULL>";
      goto done;
    }

    hr = pMediaType->GetMajorType(&MajorType);
    CHECKHR_GOTO(hr, done);

    pszGuidStr = STRING_FROM_GUID(MajorType);
    if(pszGuidStr != NULL)
    {
      description += pszGuidStr;
      description += ": ";
    }
    else
    {
      description += "Other: ";
    }

    hr = pMediaType->GetCount(&cAttrCount);
    CHECKHR_GOTO(hr, done);

    for(UINT32 i = 0; i < cAttrCount; i++)
    {
      GUID guidId;
      MF_ATTRIBUTE_TYPE attrType;

      hr = pMediaType->GetItemByIndex(i, &guidId, NULL);
      CHECKHR_GOTO(hr, done);

      hr = pMediaType->GetItemType(guidId, &attrType);
      CHECKHR_GOTO(hr, done);

      pszGuidStr = STRING_FROM_GUID(guidId);
      if(pszGuidStr != NULL)
      {
        description += pszGuidStr;
      }
      else
      {
        LPOLESTR guidStr = NULL;
        StringFromCLSID(guidId, &guidStr);
        description += *guidStr;

        CoTaskMemFree(guidStr);
      }

      description += "=";

      switch(attrType)
      {
      case MF_ATTRIBUTE_UINT32:
      {
        UINT32 Val;
        hr = pMediaType->GetUINT32(guidId, &Val);
        CHECKHR_GOTO(hr, done);

        description += std::to_string(Val);
        break;
      }
      case MF_ATTRIBUTE_UINT64:
      {
        UINT64 Val;
        hr = pMediaType->GetUINT64(guidId, &Val);
        CHECKHR_GOTO(hr, done);

        if(guidId == MF_MT_FRAME_SIZE || guidId == MF_MT_PIXEL_ASPECT_RATIO)
        {
          //tempStr.Format("W %u, H: %u", HI32(Val), LO32(Val));
          description += "W:" + std::to_string(HI32(Val)) + "H:" + std::to_string(LO32(Val));
        }
        else if(guidId == MF_MT_FRAME_RATE)
        {
          //tempStr.Format("W %u, H: %u", HI32(Val), LO32(Val));
          description += std::to_string(Val);
        }
        else
        {
          //tempStr.Format("%ld", Val);
          description += std::to_string(Val);
        }

        //description += tempStr;

        break;
      }
      case MF_ATTRIBUTE_DOUBLE:
      {
        DOUBLE Val;
        hr = pMediaType->GetDouble(guidId, &Val);
        CHECKHR_GOTO(hr, done);

        //tempStr.Format("%f", Val);
        description += std::to_string(Val);
        break;
      }
      case MF_ATTRIBUTE_GUID:
      {
        GUID Val;
        const char * pValStr;

        hr = pMediaType->GetGUID(guidId, &Val);
        CHECKHR_GOTO(hr, done);

        pValStr = STRING_FROM_GUID(Val);
        if(pValStr != NULL)
        {
          description += *pValStr;
        }
        else
        {
          LPOLESTR guidStr = NULL;
          StringFromCLSID(Val, &guidStr);
          description += *guidStr;

          CoTaskMemFree(guidStr);
        }

        break;
      }
      case MF_ATTRIBUTE_STRING:
      {
        hr = pMediaType->GetString(guidId, TempBuf, sizeof(TempBuf) / sizeof(TempBuf[0]), NULL);
        if(hr == HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER))
        {
          description += "<Too Long>";
          break;
        }
        CHECKHR_GOTO(hr, done);

        //description += CW2A(TempBuf);
        description += *TempBuf;

        break;
      }
      case MF_ATTRIBUTE_BLOB:
      {
        description += "<BLOB>";
        break;
      }
      case MF_ATTRIBUTE_IUNKNOWN:
      {
        description += "<UNK>";
        break;
      }
      //default:
      //assert(0);
      }

      description += ", ";
    }

    //assert(m_szResp.GetLength() >= 2);
    //m_szResp.Left(m_szResp.GetLength() - 2);

  done:

    return description;
  }

  /*
  Or just go to http://msdn.microsoft.com/en-us/library/windows/desktop/dd757532(v=vs.85).aspx.
  */
  //void MFVideoSampler::DumpVideoSubTypes()
  //{
  //  char* buffer;

  //  UuidToString((const _GUID *)&MFVideoFormat_RGB24, (RPC_WSTR*)&buffer);
  //  Console::WriteLine("RGB24: " + Marshal::PtrToStringUni((IntPtr)buffer));

  //  UuidToString((const _GUID *)&MFVideoFormat_I420, (RPC_WSTR*)&buffer);
  //  Console::WriteLine("I420: " + Marshal::PtrToStringUni((IntPtr)buffer));
  //}

  HRESULT MFVideoSampler::PlayTestAudio()
  {
    IMMDeviceEnumerator * pMmDeviceEnumerator;
    IMMDevice * pMmDevice;
    IAudioClient *pAudioClient;
    WAVEFORMATEX * pWaveFormatEx;
    IAudioRenderClient * pAudioRenderClient;

    HRESULT hr = CoCreateInstance(
      __uuidof(MMDeviceEnumerator),
      NULL,
      CLSCTX_ALL,
      __uuidof(IMMDeviceEnumerator),
      (void**)&pMmDeviceEnumerator
    );

    hr = pMmDeviceEnumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &pMmDevice);
    hr = pMmDevice->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (VOID**)&pAudioClient);
    hr = pAudioClient->GetMixFormat(&pWaveFormatEx);

    static const REFERENCE_TIME g_nBufferTime = 60 * 1000 * 10000i64; // 1 minute

    hr = pAudioClient->Initialize(AUDCLNT_SHAREMODE_SHARED, 0, g_nBufferTime, 0, pWaveFormatEx, NULL);

#pragma region Data

    hr = pAudioClient->GetService(__uuidof(IAudioRenderClient), (VOID**)&pAudioRenderClient);

    UINT32 nSampleCount = (UINT32)(g_nBufferTime / (1000 * 10000i64) * pWaveFormatEx->nSamplesPerSec) / 2;
    //		_A(pWaveFormatEx->wFormatTag == WAVE_FORMAT_EXTENSIBLE);

    const WAVEFORMATEXTENSIBLE* pWaveFormatExtensible = (const WAVEFORMATEXTENSIBLE*)(const WAVEFORMATEX*)pWaveFormatEx;
    //		_A(pWaveFormatExtensible->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);

    //		// ASSU: Mixing format is IEEE Float PCM
    BYTE* pnData = NULL;
    hr = pAudioRenderClient->GetBuffer(nSampleCount, &pnData);

    FLOAT* pfFloatData = (FLOAT*)pnData;
    for(UINT32 nSampleIndex = 0; nSampleIndex < nSampleCount; nSampleIndex++)
      for(WORD nChannelIndex = 0; nChannelIndex < pWaveFormatEx->nChannels; nChannelIndex++)
        pfFloatData[nSampleIndex * pWaveFormatEx->nChannels + nChannelIndex] = sin(1000.0f * nSampleIndex / pWaveFormatEx->nSamplesPerSec * 2 * 3.142);
    hr = pAudioRenderClient->ReleaseBuffer(nSampleCount, 0);

#pragma endregion

    //		CComPtr<ISimpleAudioVolume> pSimpleAudioVolume;
    //		__C(pAudioClient->GetService(__uuidof(ISimpleAudioVolume), (VOID**)&pSimpleAudioVolume));
    //		__C(pSimpleAudioVolume->SetMasterVolume(0.50f, NULL));
    printf("Playing Loud\n");
    hr = pAudioClient->Start();
    Sleep(5 * 1000);
    //		_tprintf(_T("Playing Quiet\n"));
    //		__C(pSimpleAudioVolume->SetMasterVolume(0.10f, NULL));
    //		Sleep(15 * 1000);
    //		// NOTE: We don't care for termination crash
    //		return 0;

    return hr;
  }

  HRESULT MFVideoSampler::PlayFileToSpeaker()
  {
    IMFSourceResolver *pSourceResolver = NULL;
    IUnknown* uSource = NULL;
    IMFMediaSource *mediaFileSource = NULL;
    IMFSourceReader *pSourceReader = NULL;
    IMFMediaType *pAudioOutType = NULL;
    IMFMediaType *pFileAudioMediaType = NULL;
    MF_OBJECT_TYPE ObjectType = MF_OBJECT_INVALID;
    IMFMediaSink *pAudioSink = NULL;
    IMFStreamSink *pStreamSink = NULL;
    IMFMediaTypeHandler *pMediaTypeHandler = NULL;
    IMFMediaType *pMediaType = NULL;
    IMFMediaType *pSinkMediaType = NULL;
    IMFSinkWriter *pSinkWriter = NULL;

    // Set up the reader for the file.
    CHECK_HR(MFCreateSourceResolver(&pSourceResolver), L"MFCreateSourceResolver failed.\n");

    CHECK_HR(pSourceResolver->CreateObjectFromURL(
      //L"big_buck_bunny.mp4",		// URL of the source.
      L"C:\\Dev\\sipsorcery\\mediafoundationsamples\\MediaFiles\\max4.mp4",		// URL of the source.
      //L"C:\\Dev\\sipsorcery\\mediafoundationsamples\\MediaFiles\\big_buck_bunny_48k.mp4",
      MF_RESOLUTION_MEDIASOURCE,  // Create a source object.
      NULL,                       // Optional property store.
      &ObjectType,				// Receives the created object type. 
      &uSource					// Receives a pointer to the media source.
    ), L"Failed to create media source resolver for file.\n");

    CHECK_HR(uSource->QueryInterface(IID_PPV_ARGS(&mediaFileSource)), L"Failed to create media file source.\n");

    CHECK_HR(MFCreateSourceReaderFromMediaSource(mediaFileSource, NULL, &pSourceReader), L"Error creating media source reader.\n");

    CHECK_HR(pSourceReader->GetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, &pFileAudioMediaType), L"Error retrieving current media type from first audio stream.\n");

    Console::WriteLine("File Media Type:");
    std::cout << GetMediaTypeDescription(pFileAudioMediaType) << std::endl;

    // Set the audio output type on the source reader.
    CHECK_HR(MFCreateMediaType(&pAudioOutType), L"Failed to create audio output media type.\n");
    CHECK_HR(pAudioOutType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), L"Failed to set audio output media major type.\n");
    CHECK_HR(pAudioOutType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_Float), L"Failed to set audio output audio sub type (Float).\n");

    Console::WriteLine("Sink Reader Output Type:");
    std::cout << GetMediaTypeDescription(pAudioOutType) << std::endl;

    CHECK_HR(pSourceReader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM, NULL, pAudioOutType), L"Error setting reader audio output type.\n");

    CHECK_HR(MFCreateAudioRenderer(NULL, &pAudioSink), L"Failed to create audio sink.\n");

    CHECK_HR(pAudioSink->GetStreamSinkByIndex(0, &pStreamSink), L"Failed to get audio renderer stream by index.\n");

    CHECK_HR(pStreamSink->GetMediaTypeHandler(&pMediaTypeHandler), L"Failed to get media type handler.\n");

    CHECK_HR(pMediaTypeHandler->GetMediaTypeByIndex(0, &pSinkMediaType), L"Failed to get sink media type.\n");

    CHECK_HR(pMediaTypeHandler->SetCurrentMediaType(pSinkMediaType), L"Failed to set current media type.\n");

    Console::WriteLine("Sink Media Type:");
    std::cout << GetMediaTypeDescription(pSinkMediaType) << std::endl;

    CHECK_HR(MFCreateSinkWriterFromMediaSink(pAudioSink, NULL, &pSinkWriter), L"Failed to create sink writer from audio sink.\n");

    printf("Read audio samples from file and write to speaker.\n");

    IMFSample *audioSample = NULL;
    DWORD streamIndex, flags;
    LONGLONG llAudioTimeStamp;

    //for(int index = 0; index < 10; index++)
    while(true)
    {
      // Initial read results in a null pSample??
      CHECK_HR(pSourceReader->ReadSample(
        MF_SOURCE_READER_FIRST_AUDIO_STREAM,
        0,                              // Flags.
        &streamIndex,                   // Receives the actual stream index. 
        &flags,                         // Receives status flags.
        &llAudioTimeStamp,              // Receives the time stamp.
        &audioSample                    // Receives the sample or NULL.
      ), L"Error reading audio sample.");

      if(flags & MF_SOURCE_READERF_ENDOFSTREAM)
      {
        printf("End of stream.\n");
        break;
      }
      if(flags & MF_SOURCE_READERF_STREAMTICK)
      {
        printf("Stream tick.\n");
        pSinkWriter->SendStreamTick(0, llAudioTimeStamp);
      }

      if(!audioSample)
      {
        printf("Null audio sample.\n");
      }
      else
      {
        CHECK_HR(audioSample->SetSampleTime(llAudioTimeStamp), L"Error setting the audio sample time.\n");

        CHECK_HR(pSinkWriter->WriteSample(0, audioSample), L"The stream sink writer was not happy with the sample.\n");
      }
    }

    return 0;
  }

}
