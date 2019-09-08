#include "MFSampleGrabber.h"

namespace SIPSorceryMedia
{
  MFSampleGrabber::MFSampleGrabber()
  {
    HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    hr = MFStartup(MF_VERSION);
  }

  MFSampleGrabber::~MFSampleGrabber()
  {
  }

  void MFSampleGrabber::OnClockStart(MFTIME hnsSystemTime, LONGLONG llClockStartOffset)
  {
    //Console::WriteLine("C++ MFSampleGrabber.OnClockStart " + hnsSystemTime + ", " + llClockStartOffset);
    OnClockStartEvent(hnsSystemTime, llClockStartOffset);
  }

  void MFSampleGrabber::OnProcessSample(REFGUID guidMajorMediaType, DWORD dwSampleFlags, LONGLONG llSampleTime, LONGLONG llSampleDuration, const BYTE * pSampleBuffer, DWORD dwSampleSize)
  {
    //Console::WriteLine("C++ MFSampleGrabber.OnProcessSample " + dwSampleSize);
    // TODO: Properly determine whether audio or video.
    // MFMediaType_Audio MFMediaType_Video
    int mediaType = (dwSampleSize > 5000) ? VIDEO_TYPE_ID : AUDIO_TYPE_ID;

    auto buffer = gcnew array<Byte>(dwSampleSize);
    Marshal::Copy((IntPtr)((byte*)pSampleBuffer), buffer, 0, dwSampleSize);

    OnProcessSampleEvent(mediaType, dwSampleFlags, llSampleTime, llSampleDuration, dwSampleSize, buffer);
  }

  void MFSampleGrabber::OnVideoResolutionChanged(UINT32 width, UINT32 height, UINT32 stride)
  {
    OnVideoResolutionChangedEvent(width, height, stride);
  }

  HRESULT MFSampleGrabber::Run(System::String^ mediaPath, bool loop)
  {
    System::Console::WriteLine("MFSampleGrabber.Run " + mediaPath + ".");

    std::wstring mediaPathNative = msclr::interop::marshal_as<std::wstring>(mediaPath);

    // Need to create a pinned copy of the media session pointer it's a managed resource being access by native code.
    cli::pin_ptr<IMFMediaSession*> pinnedMediaSession = &_pcliSession;
    IMFMediaSession * pMediaSession = reinterpret_cast<IMFMediaSession*>(pinnedMediaSession);

    IMFMediaSource *pSource = NULL;
    SampleGrabberCB *pSampleGrabberSinkCallback = NULL;
    IMFActivate *pAudioSinkActivate = NULL, *pVideoSinkActivate = NULL;
    IMFTopology *pTopology = NULL;
    IMFMediaType *pVideoType = NULL, *pAudioType = NULL;

    // Configure the media type that the Sample Grabber will receive.
    // Setting the major and subtype is usually enough for the topology loader
    // to resolve the topology.
    HRESULT hr = S_OK;
    CHECK_HR(hr = MFCreateMediaType(&pVideoType));
    CHECK_HR(hr = pVideoType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    CHECK_HR(hr = pVideoType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_I420));

    CHECK_HR(hr = MFCreateMediaType(&pAudioType));
    CHECK_HR(hr = pAudioType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio));
    CHECK_HR(hr = pAudioType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM));
    CHECK_HR(hr = pAudioType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 1));
    CHECK_HR(hr = pAudioType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
    CHECK_HR(hr = pAudioType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 8000));

    // Create the sample grabber sink.
    CHECK_HR(hr = SampleGrabberCB::CreateInstance(&pSampleGrabberSinkCallback));
    CHECK_HR(hr = MFCreateSampleGrabberSinkActivate(pAudioType, pSampleGrabberSinkCallback, &pAudioSinkActivate));
    CHECK_HR(hr = MFCreateSampleGrabberSinkActivate(pVideoType, pSampleGrabberSinkCallback, &pVideoSinkActivate));

    OnClockStartDelegate ^clockStartDelegate = gcnew OnClockStartDelegate(this, &MFSampleGrabber::OnClockStart);
    GCHandle gchClockStart = GCHandle::Alloc(clockStartDelegate); // Stop delegate from being garbage ollected. TODO: gch.Free(); when finished.
    IntPtr ipClockStart = Marshal::GetFunctionPointerForDelegate(clockStartDelegate);
    OnClockStartFunc cbClockStart = static_cast<OnClockStartFunc>(ipClockStart.ToPointer());

    OnProcessSampleDelegateNative ^processSampleDelegate = gcnew OnProcessSampleDelegateNative(this, &MFSampleGrabber::OnProcessSample);
    GCHandle gchProcessSample = GCHandle::Alloc(processSampleDelegate); // Stop delegate from being garbage ollected. TODO: gch.Free(); when finished.
    IntPtr ipProcessSample = Marshal::GetFunctionPointerForDelegate(processSampleDelegate);
    OnProcessSampleFunc cbProcessSample = static_cast<OnProcessSampleFunc>(ipProcessSample.ToPointer());

    pSampleGrabberSinkCallback->SetHandlers(cbClockStart, cbProcessSample);

    // To run as fast as possible, set this attribute (requires Windows 7):
    //CHECK_HR(hr = pSinkActivate->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK, TRUE));

    // Create the Media Session.
    CHECK_HR(hr = MFCreateMediaSession(NULL, &pMediaSession));

    // Create the media source.
    CHECK_HR(hr = CreateMediaSource(mediaPathNative.c_str(), &pSource));

    // Create the topology.
    CHECK_HR(hr = CreateTopology(pSource, pVideoSinkActivate, pAudioSinkActivate, &pTopology));

    OnVideoResolutionChangedDelegate ^vidResChangedDelegate = gcnew OnVideoResolutionChangedDelegate(this, &MFSampleGrabber::OnVideoResolutionChanged);
    GCHandle gchVidResChanged = GCHandle::Alloc(vidResChangedDelegate); // Stop delegate from being garbage ollected. TODO: gch.Free(); when finished.
    IntPtr ipPVidResChanged = Marshal::GetFunctionPointerForDelegate(vidResChangedDelegate);
    OnVideoResolutionChangedFunc cbVidResChanged = static_cast<OnVideoResolutionChangedFunc>(ipPVidResChanged.ToPointer());

    do {
      // Run the media session.
      if(_paused) {
        Sleep(1000);
      }
      else {
        CHECK_HR(hr = RunSession(pMediaSession, pTopology, cbVidResChanged));
      }
    } while(loop == true && _exit == false);

    System::Console::WriteLine("MFSampleGrabber.Run, finished.");

  done:
    // Clean up.
    if(pSource)
    {
      pSource->Shutdown();
    }
    if(pMediaSession)
    {
      pMediaSession->Shutdown();
    }

    SafeRelease((IMFMediaSession**)(&pMediaSession));
    SafeRelease(&pSource);
    SafeRelease(&pSampleGrabberSinkCallback);
    SafeRelease(&pAudioSinkActivate);
    SafeRelease(&pVideoSinkActivate);
    SafeRelease(&pTopology);
    SafeRelease(&pVideoType);
    SafeRelease(&pAudioType);
    return hr;
  }

  // Pauses an initialised session.
  HRESULT MFSampleGrabber::Pause()
  {
    System::Console::WriteLine("MFSampleGrabber.Pause.");

    if(_paused == false)
    {
      _paused = true;

      if(_pcliSession != nullptr)
      {
        // Need to create a pinned copy of the media session pointer it's a managed resource being access by native code.
        cli::pin_ptr<IMFMediaSession*> pinnedMediaSession = &_pcliSession;
        IMFMediaSession * pMediaSession = reinterpret_cast<IMFMediaSession*>(pinnedMediaSession);
        _pcliSession->Pause();
      }
    }

    return S_OK;
  }

  // Restarts a paused session (relies on the do/while loop in Run, see above).
  HRESULT MFSampleGrabber::Start()
  {
    System::Console::WriteLine("MFSampleGrabber.Start.");
    _paused = false;
    return S_OK;
  }

  // Stops the media session 
  HRESULT MFSampleGrabber::StopAndExit()
  {
    System::Console::WriteLine("MFSampleGrabber.Stop.");

    _exit = true;

    if(_pcliSession != nullptr)
    {
      // Need to create a pinned copy of the media session pointer it's a managed resource being access by native code.
      cli::pin_ptr<IMFMediaSession*> pinnedMediaSession = &_pcliSession;
      IMFMediaSession * pMediaSession = reinterpret_cast<IMFMediaSession*>(pinnedMediaSession);
      _pcliSession->Stop();
    }

    return S_OK;
  }
} // End SIPSorceryMedia namespace

// SampleGrabberCB implementation

// Create a new instance of the object.
HRESULT SampleGrabberCB::CreateInstance(SampleGrabberCB **ppCB)
{
  *ppCB = new (std::nothrow) SampleGrabberCB();

  if(ppCB == NULL)
  {
    return E_OUTOFMEMORY;
  }
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::QueryInterface(REFIID riid, void** ppv)
{
  static const QITAB qit[] =
  {
    QITABENT(SampleGrabberCB, IMFSampleGrabberSinkCallback),
    QITABENT(SampleGrabberCB, IMFClockStateSink),
  {0}
  };
  return QISearch(this, qit, riid, ppv);
}

STDMETHODIMP_(ULONG) SampleGrabberCB::AddRef()
{
  return InterlockedIncrement(&m_cRef);
}

STDMETHODIMP_(ULONG) SampleGrabberCB::Release()
{
  ULONG cRef = InterlockedDecrement(&m_cRef);
  if(cRef == 0)
  {
    delete this;
  }
  return cRef;

}

// IMFClockStateSink methods.

// In these example, the IMFClockStateSink methods do not perform any actions. 
// You can use these methods to track the state of the sample grabber sink.

STDMETHODIMP SampleGrabberCB::OnClockStart(MFTIME hnsSystemTime, LONGLONG llClockStartOffset)
{
  //std::cout << "native SampleGrabberCB::OnClockStart " << hnsSystemTime << ", " << llClockStartOffset << std::endl;
  _onClockStartFunc(hnsSystemTime, llClockStartOffset);
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::OnClockStop(MFTIME hnsSystemTime)
{
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::OnClockPause(MFTIME hnsSystemTime)
{
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::OnClockRestart(MFTIME hnsSystemTime)
{
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::OnClockSetRate(MFTIME hnsSystemTime, float flRate)
{
  return S_OK;
}

// IMFSampleGrabberSink methods.

STDMETHODIMP SampleGrabberCB::OnSetPresentationClock(IMFPresentationClock* pClock)
{
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::OnProcessSample(REFGUID guidMajorMediaType, DWORD dwSampleFlags,
  LONGLONG llSampleTime, LONGLONG llSampleDuration, const BYTE * pSampleBuffer,
  DWORD dwSampleSize)
{
  if(dwSampleFlags & MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED)
  {
    std::cout << "Native type changed." << std::endl;
  }
  if(dwSampleFlags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)
  {
    std::cout << "Current type changed for media type " << guidMajorMediaType.Data1 << "." << std::endl;
  }

  // Display information about the sample.
  //printf("Sample: start = %I64d, duration = %I64d, bytes = %d\n", llSampleTime, llSampleDuration, dwSampleSize);
  _onProcessSampleFunc(guidMajorMediaType, dwSampleFlags, llSampleTime, llSampleDuration, pSampleBuffer, dwSampleSize);
  return S_OK;
}

STDMETHODIMP SampleGrabberCB::OnShutdown()
{
  return S_OK;
}

// Create a media source from a URL.
HRESULT CreateMediaSource(PCWSTR pszURL, IMFMediaSource **ppSource)
{
  IMFSourceResolver* pSourceResolver = NULL;
  IUnknown* pSource = NULL;

  // Create the source resolver.
  HRESULT hr = S_OK;
  CHECK_HR(hr = MFCreateSourceResolver(&pSourceResolver));

  MF_OBJECT_TYPE ObjectType;
  CHECK_HR(hr = pSourceResolver->CreateObjectFromURL(pszURL,
    MF_RESOLUTION_MEDIASOURCE, NULL, &ObjectType, &pSource));

  hr = pSource->QueryInterface(IID_PPV_ARGS(ppSource));

done:
  SafeRelease(&pSourceResolver);
  SafeRelease(&pSource);
  return hr;
}

// Add a source node to a topology.
HRESULT AddSourceNode(
  IMFTopology *pTopology,           // Topology.
  IMFMediaSource *pSource,          // Media source.
  IMFPresentationDescriptor *pPD,   // Presentation descriptor.
  IMFStreamDescriptor *pSD,         // Stream descriptor.
  IMFTopologyNode **ppNode)         // Receives the node pointer.
{
  IMFTopologyNode *pNode = NULL;

  HRESULT hr = S_OK;
  CHECK_HR(hr = MFCreateTopologyNode(MF_TOPOLOGY_SOURCESTREAM_NODE, &pNode));
  CHECK_HR(hr = pNode->SetUnknown(MF_TOPONODE_SOURCE, pSource));
  CHECK_HR(hr = pNode->SetUnknown(MF_TOPONODE_PRESENTATION_DESCRIPTOR, pPD));
  CHECK_HR(hr = pNode->SetUnknown(MF_TOPONODE_STREAM_DESCRIPTOR, pSD));
  CHECK_HR(hr = pTopology->AddNode(pNode));

  // Return the pointer to the caller.
  *ppNode = pNode;
  (*ppNode)->AddRef();

done:
  SafeRelease(&pNode);
  return hr;
}

// Add an output node to a topology.
HRESULT AddOutputNode(
  IMFTopology *pTopology,     // Topology.
  IMFActivate *pActivate,     // Media sink activation object.
  DWORD dwId,                 // Identifier of the stream sink.
  IMFTopologyNode **ppNode)   // Receives the node pointer.
{
  IMFTopologyNode *pNode = NULL;

  HRESULT hr = S_OK;
  CHECK_HR(hr = MFCreateTopologyNode(MF_TOPOLOGY_OUTPUT_NODE, &pNode));
  CHECK_HR(hr = pNode->SetObject(pActivate));
  CHECK_HR(hr = pNode->SetUINT32(MF_TOPONODE_STREAMID, dwId));
  CHECK_HR(hr = pNode->SetUINT32(MF_TOPONODE_NOSHUTDOWN_ON_REMOVE, FALSE));
  CHECK_HR(hr = pTopology->AddNode(pNode));

  // Return the pointer to the caller.
  *ppNode = pNode;
  (*ppNode)->AddRef();

done:
  SafeRelease(&pNode);
  return hr;
}

// Create the topology.
HRESULT CreateTopology(IMFMediaSource *pSource, IMFActivate *pVideoSinkActivate, IMFActivate *pAudioSinkActivate, IMFTopology **ppTopo)
{
  IMFTopology *pTopology = NULL;
  IMFPresentationDescriptor *pPD = NULL;
  IMFStreamDescriptor *pSD = NULL;
  IMFMediaTypeHandler *pHandler = NULL;
  IMFTopologyNode *pAudioSourceNode = NULL, *pVideoSourceNode = NULL;
  IMFTopologyNode *pAudioSinkNode = NULL, *pVideoSinkNode = NULL;

  HRESULT hr = S_OK;
  DWORD cStreams = 0;

  CHECK_HR(hr = MFCreateTopology(&pTopology));
  CHECK_HR(hr = pSource->CreatePresentationDescriptor(&pPD));
  CHECK_HR(hr = pPD->GetStreamDescriptorCount(&cStreams));

  for(DWORD i = 0; i < cStreams; i++)
  {
    // In this example, we look for audio streams and connect them to the sink.

    BOOL fSelected = FALSE;
    GUID majorType;

    CHECK_HR(hr = pPD->GetStreamDescriptorByIndex(i, &fSelected, &pSD));
    CHECK_HR(hr = pSD->GetMediaTypeHandler(&pHandler));
    CHECK_HR(hr = pHandler->GetMajorType(&majorType));

    if(majorType == MFMediaType_Video && fSelected)
    {
      CHECK_HR(hr = AddSourceNode(pTopology, pSource, pPD, pSD, &pVideoSourceNode));
      CHECK_HR(hr = AddOutputNode(pTopology, pVideoSinkActivate, 0, &pVideoSinkNode));
      CHECK_HR(hr = pVideoSourceNode->ConnectOutput(0, pVideoSinkNode, 0));
    }
    else if(majorType == MFMediaType_Audio && fSelected)
    {
      CHECK_HR(hr = AddSourceNode(pTopology, pSource, pPD, pSD, &pAudioSourceNode));
      // TODO: Should be possible to add the MULAW codec here using AddTransformNode, see https://docs.microsoft.com/en-us/windows/win32/medfound/adding-a-decoder-to-a-topology.
      CHECK_HR(hr = AddOutputNode(pTopology, pAudioSinkActivate, 0, &pAudioSinkNode));
      CHECK_HR(hr = pAudioSourceNode->ConnectOutput(0, pAudioSinkNode, 0));
    }
    else
    {
      CHECK_HR(hr = pPD->DeselectStream(i));
    }

    SafeRelease(&pSD);
    SafeRelease(&pHandler);
  }

  *ppTopo = pTopology;
  (*ppTopo)->AddRef();

done:
  SafeRelease(&pTopology);
  SafeRelease(&pAudioSourceNode);
  SafeRelease(&pVideoSourceNode);
  SafeRelease(&pAudioSinkNode);
  SafeRelease(&pVideoSinkNode);
  SafeRelease(&pPD);
  SafeRelease(&pSD);
  SafeRelease(&pHandler);
  return hr;
}

HRESULT RunSession(IMFMediaSession *pSession, IMFTopology *pTopology, OnVideoResolutionChangedFunc onVideoResolutionChanged)
{
  IMFMediaEvent *pEvent = NULL;
  IMFTopologyNode *pNode = nullptr;
  IMFStreamSink *pStreamSink = nullptr;
  IUnknown *pNodeObject = NULL;
  IMFMediaTypeHandler *pMediaTypeHandler = nullptr;
  IMFMediaType *pMediaType = nullptr;

  PROPVARIANT var;
  PropVariantInit(&var);

  HRESULT hr = S_OK;
  CHECK_HR(hr = pSession->SetTopology(0, pTopology));
  CHECK_HR(hr = pSession->Start(&GUID_NULL, &var));

  while(true)
  {
    HRESULT hrStatus = S_OK;
    MediaEventType met;

    CHECK_HR(hr = pSession->GetEvent(0, &pEvent));
    CHECK_HR(hr = pEvent->GetStatus(&hrStatus));
    CHECK_HR(hr = pEvent->GetType(&met));

    if(FAILED(hrStatus))
    {
      printf("Session error: 0x%x (event id: %d)\n", hrStatus, met);
      hr = hrStatus;
      goto done;
    }
    else
    {
      //printf("Session event: event id: %d\n",  met);
      switch(met)
      {
      case MESessionStreamSinkFormatChanged:
        //std::cout << "MESessionStreamSinkFormatChanged." << std::endl;

        {
          MF_TOPOLOGY_TYPE nodeType;
          UINT64 outputNode{0};
          GUID majorMediaType;
          UINT64 videoResolution{0};
          UINT32 stride{0};

          // This seems a ridiculously convoluted way to extract the change to the video resolution. There may
          // be a simpler way but then again this is the Media Foundation and COM!
          CHECK_HR_ERROR(pEvent->GetUINT64(MF_EVENT_OUTPUT_NODE, &outputNode), "Failed to get ouput node from media changed event.");
          CHECK_HR_ERROR(pTopology->GetNodeByID(outputNode, &pNode), "Failed to get topology node for output ID.");
          CHECK_HR_ERROR(pNode->GetObject(&pNodeObject), "Failed to get the node's object pointer.");
          CHECK_HR_ERROR(pNodeObject->QueryInterface(IID_PPV_ARGS(&pStreamSink)), "Failed to get media stream sink from activation object.");
          CHECK_HR_ERROR(pStreamSink->GetMediaTypeHandler(&pMediaTypeHandler), "Failed to get media type handler from stream sink.");
          CHECK_HR_ERROR(pMediaTypeHandler->GetCurrentMediaType(&pMediaType), "Failed to get current media type.");
          CHECK_HR_ERROR(pMediaType->GetMajorType(&majorMediaType), "Failed to get major media type.");
          
          if(majorMediaType == MFMediaType_Video)
          {
            CHECK_HR_ERROR(pMediaType->GetUINT64(MF_MT_FRAME_SIZE, &videoResolution), "Failed to get new video resolution.");
            CHECK_HR_ERROR(pMediaType->GetUINT32(MF_MT_DEFAULT_STRIDE, &stride), "Failed to get the new stride.");
            //std::cout << "Media session video resolution changed to width " << std::to_string(HI32(videoResolution)) 
            //  << " and height " << std::to_string(LO32(videoResolution)) 
            //  << " and stride " << stride << "." << std::endl;
            if(onVideoResolutionChanged != nullptr) {
              onVideoResolutionChanged(HI32(videoResolution), LO32(videoResolution), stride);
            }
          }
          break;
        }
      default:
        break;
      }
    }

    if(met == MESessionEnded || met == MESessionPaused)
    {
      break;
    }
    SafeRelease(&pEvent);
    SafeRelease(&pNode);
    SafeRelease(&pStreamSink);
    SafeRelease(&pNodeObject);
    SafeRelease(&pMediaTypeHandler);
    SafeRelease(&pMediaType);
  }

done:
  SafeRelease(&pEvent);
  SafeRelease(&pNode);
  SafeRelease(&pStreamSink);
  SafeRelease(&pNodeObject);
  SafeRelease(&pMediaTypeHandler);
  SafeRelease(&pMediaType);
  return hr;
}