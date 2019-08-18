#pragma once

#include <new>
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <Shlwapi.h>
#include <stdio.h>
#include <msclr\marshal.h>
#include <msclr\marshal_cppstd.h>

#include <iostream>
#include <string>

using namespace System;
using namespace System::Runtime::InteropServices;

#pragma comment(lib, "mfplat")
#pragma comment(lib, "mf")
#pragma comment(lib, "mfuuid")
#pragma comment(lib, "Shlwapi")

//#pragma unmanaged

typedef void(__stdcall *OnClockStartFunc)(MFTIME, LONGLONG);
typedef void(__stdcall *OnProcessSampleFunc)(REFGUID, DWORD, LONGLONG, LONGLONG, const BYTE *, DWORD dwSampleSize);

//#pragma managed

namespace SIPSorceryMedia 
{
  public delegate void OnClockStartDelegate(MFTIME, LONGLONG);
  public delegate void OnProcessSampleDelegateNative(REFGUID guidMajorMediaType, DWORD dwSampleFlags, LONGLONG llSampleTime, LONGLONG llSampleDuration, const BYTE * pSampleBuffer, DWORD dwSampleSize);
  public delegate void OnProcessSampleDelegateManaged(int mediaTypeID, DWORD dwSampleFlags, LONGLONG llSampleTime, LONGLONG llSampleDuration, DWORD dwSampleSize, array<Byte> ^% buffer);

  public ref class MFSampleGrabber
  {
  public:
    const int VIDEO_TYPE_ID = 0;
    const int AUDIO_TYPE_ID = 1;

    MFSampleGrabber();
    ~MFSampleGrabber();
    HRESULT Run(System::String^ path);

    event OnClockStartDelegate^ OnClockStartEvent;
    event OnProcessSampleDelegateManaged^ OnProcessSampleEvent;

    void OnClockStart(MFTIME hnsSystemTime, LONGLONG llClockStartOffset);
    void OnProcessSample(REFGUID guidMajorMediaType, DWORD dwSampleFlags, LONGLONG llSampleTime, LONGLONG llSampleDuration, const BYTE * pSampleBuffer, DWORD dwSampleSize);
  };
}

//#pragma unmanaged

HRESULT CreateMediaSource(PCWSTR pszURL, IMFMediaSource **ppSource);
HRESULT CreateTopology(IMFMediaSource *pSource, IMFActivate *pVideoSink, IMFActivate *pAudioSink, IMFTopology **ppTopo);
HRESULT RunSession(IMFMediaSession *pSession, IMFTopology *pTopology);
HRESULT RunSampleGrabber(PCWSTR pszFileName);

template <class T> void SafeRelease(T **ppT)
{
  if(*ppT)
  {
    (*ppT)->Release();
    *ppT = NULL;
  }
}

#define CHECK_HR(x) if (FAILED(x)) { goto done; }

// The class that implements the callback interface.
class SampleGrabberCB: public IMFSampleGrabberSinkCallback
{
  long m_cRef;

  SampleGrabberCB(): m_cRef(1) {}

public:
  static HRESULT CreateInstance(SampleGrabberCB **ppCB);

  // IUnknown methods
  STDMETHODIMP QueryInterface(REFIID iid, void** ppv);
  STDMETHODIMP_(ULONG) AddRef();
  STDMETHODIMP_(ULONG) Release();

  // IMFClockStateSink methods
  STDMETHODIMP OnClockStart(MFTIME hnsSystemTime, LONGLONG llClockStartOffset);
  STDMETHODIMP OnClockStop(MFTIME hnsSystemTime);
  STDMETHODIMP OnClockPause(MFTIME hnsSystemTime);
  STDMETHODIMP OnClockRestart(MFTIME hnsSystemTime);
  STDMETHODIMP OnClockSetRate(MFTIME hnsSystemTime, float flRate);

  // IMFSampleGrabberSinkCallback methods
  STDMETHODIMP OnSetPresentationClock(IMFPresentationClock* pClock);
  STDMETHODIMP OnProcessSample(REFGUID guidMajorMediaType, DWORD dwSampleFlags,
    LONGLONG llSampleTime, LONGLONG llSampleDuration, const BYTE * pSampleBuffer,
    DWORD dwSampleSize);
  STDMETHODIMP OnShutdown();

  void SetHandlers(OnClockStartFunc onClockStartFunc, OnProcessSampleFunc onProcessSampleFunc)
  {
    _onClockStartFunc = onClockStartFunc;
    _onProcessSampleFunc = onProcessSampleFunc;
  }

private:
  OnClockStartFunc _onClockStartFunc = nullptr;
  OnProcessSampleFunc _onProcessSampleFunc = nullptr;
};
