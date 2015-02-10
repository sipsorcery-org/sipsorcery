// MFStreamer.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include "stddef.h"
#include <mfapi.h>
#include <mfplay.h>
#include <mfapi.h>
#include <mftransform.h>
#include <mferror.h>
#include <mfobjects.h>
#include <mfreadwrite.h>
#include "errors.h"
#include "wmcodecdsp.h"
#include "mediatypetrace.h"

#include "vpx/vpx_encoder.h"
#include "vpx/vp8cx.h"

#include "MFStreamer.h"

#define CHECK_HR(hr, msg) if (HRHasFailed(hr, msg)) return hr;

const unsigned int WIDTH = 640;
const unsigned int HEIGHT = 480; // 480; // 400;
const unsigned int STRIDE = 1280;
const vpx_img_fmt VIDEO_INPUT_FORMAT = VPX_IMG_FMT_I420; // VPX_IMG_FMT_RGB24; // VPX_IMG_FMT_YUY2;
const GUID MF_INPUT_FORMAT = MFVideoFormat_RGB24; // WMMEDIASUBTYPE_YUY2; // MFVideoFormat_RGB24; // MFVideoFormat_I420;

IMFMediaSource *videoSource = NULL, *audioSource = NULL;
UINT32 videoDeviceCount = 0, audioDeviceCount = 0;
IMFAttributes *videoConfig = NULL, *audioConfig = NULL;
IMFActivate **videoDevices = NULL, **audioDevices = NULL;
IMFMediaType *videoType = NULL, *audioType = NULL;
IMFMediaType *desiredInputVideoType = NULL;
IMFSourceReader *videoReader, *audioReader;
DWORD videoStreamIndex, audioStreamIndex;

vpx_codec_enc_cfg_t _vpxConfig;
vpx_codec_ctx_t     _vpxCodec;
vpx_image_t _rawImage;

DWORD streamIndex, flags;
LONGLONG llVideoTimeStamp, llAudioTimeStamp;
//IMFSample *videoSample = NULL, *audioSample = NULL;
//CRITICAL_SECTION critsec;
BOOL bFirstVideoSample = TRUE, bFirstAudioSample = TRUE;
//LONGLONG llVideoBaseTime = 0, llAudioBaseTime = 0;
int _sampleCount = 0;

BOOL HRHasFailed(HRESULT hr, WCHAR* errtext);
HRESULT StartStreaming(vpx_codec_enc_cfg_t * vpxConfig, vpx_codec_ctx_t * vpxCodec);
//void GetCurrentMediaType(IMFSourceReader *pReader, DWORD dwStreamIndex);
void ListModes(IMFSourceReader *pReader);
//void GetDeviceName(IMFActivate *ppDevice);
void FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, int width, int height, /* out */ IMFMediaType *& foundpType);
//void WriteSampleToBitmap(IMFSample *pSample);
HRESULT CopyAttribute(IMFAttributes *pSrc, IMFAttributes *pDest, const GUID& key);
HRESULT ConfigureEncoder(IMFMediaType *pVideoType, DWORD *videoStreamIndex, DWORD *audioStreamIndex, IMFSinkWriter *pWriter);
HRESULT InitVPXEncoder(vpx_codec_enc_cfg_t * cfg, vpx_codec_ctx_t * vpxCodec, unsigned int width, unsigned int height);
int YUY2ToI420(int width, int height, int stride, BYTE *in, BYTE *out);
HRESULT GetDefaultStride(IMFMediaType *pType, LONG *plStride);

HRESULT InitMFStreamer()
{
	printf("InitMFStreamer.\n");

	CoInitializeEx(NULL, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);

	CHECK_HR(InitVPXEncoder(&_vpxConfig, &_vpxCodec, WIDTH, HEIGHT), L"Failed to intialise the VPX encoder.\n");

	// Create an attribute store to hold the search criteria.
	CHECK_HR(MFCreateAttributes(&videoConfig, 1), L"Error creating video configuation.");
	//CHECK_HR(MFCreateAttributes(&audioConfig, 1), L"Error creating audio configuation.");;

	// Request video capture devices.
	CHECK_HR(videoConfig->SetGUID(
		MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
		MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID), L"Error initialising video configuration object.");

	// Request audio capture devices.
	/*CHECK_HR(audioConfig->SetGUID(
	MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
	MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_AUDCAP_GUID), L"Error initialising audio configuration object.");*/

	// Enumerate the devices,
	CHECK_HR(MFEnumDeviceSources(videoConfig, &videoDevices, &videoDeviceCount), L"Error enumerating video devices.");
	//CHECK_HR(MFEnumDeviceSources(audioConfig, &audioDevices, &audioDeviceCount), L"Error enumerating audio devices.");

	//printf("Video device Count: %i, Audio device count: %i.\n", videoDeviceCount, audioDeviceCount);
	printf("Video device Count: %i.\n", videoDeviceCount);

	CHECK_HR(videoDevices[0]->ActivateObject(IID_PPV_ARGS(&videoSource)), L"Error activating video device.");
	//CHECK_HR(audioDevices[0]->ActivateObject(IID_PPV_ARGS(&audioSource)), L"Error activating audio device.");

	// Initialize the Media Foundation platform.
	CHECK_HR(MFStartup(MF_VERSION), L"Error on Media Foundation startup.");

	/*WCHAR *pwszFileName = L"sample.mp4";
	IMFSinkWriter *pWriter;*/

	/*CHECK_HR(MFCreateSinkWriterFromURL(
	pwszFileName,
	NULL,
	NULL,
	&pWriter), L"Error creating mp4 sink writer.");*/

	// Create the source readers.
	CHECK_HR(MFCreateSourceReaderFromMediaSource(
		videoSource,
		videoConfig,
		&videoReader), L"Error creating video source reader.");

	//ListModes(videoReader);

	/*CHECK_HR(MFCreateSourceReaderFromMediaSource(
	audioSource,
	audioConfig,
	&audioReader), L"Error creating audio source reader.");*/

	FindVideoMode(videoReader, MF_INPUT_FORMAT, WIDTH, HEIGHT, desiredInputVideoType);

	if (desiredInputVideoType == NULL) {
		printf("The specified media type could not be found for the MF video reader.\n");
	}
	else {
		CHECK_HR(videoReader->SetCurrentMediaType((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM, NULL, desiredInputVideoType),
			L"Error setting video reader media type.\n");

		CHECK_HR(videoReader->GetCurrentMediaType(
			(DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM,
			&videoType), L"Error retrieving current media type from first video stream.");
		CMediaTypeTrace *videoTypeMediaTrace = new CMediaTypeTrace(videoType);

		printf("Video input media type: %s.\n", videoTypeMediaTrace->GetString());

		LONG pStride = 0;
		GetDefaultStride(videoType, &pStride);
		printf("Stride %i.\n", pStride);
			
		/*printf("Press any key to continue...");
		getchar();*/

		/*audioReader->GetCurrentMediaType(
		(DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM,
		&audioType);*/
		//CMediaTypeTrace *audioTypeMediaTrace = new CMediaTypeTrace(audioType);
		//printf("Audio input media type: %s.\n", audioTypeMediaTrace->GetString());

		//printf("Configuring H.264 sink.\n");

		// Set up the H.264 sink.
		/*CHECK_HR(ConfigureEncoder(videoType, &videoStreamIndex, &audioStreamIndex, pWriter), L"Error configuring encoder.");
		printf("Video stream index %i, audio stream index %i.\n", videoStreamIndex, audioStreamIndex);*/

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

		// Add the input types to the H.264 sink writer.
		/*CHECK_HR(pWriter->SetInputMediaType(videoStreamIndex, videoType, NULL), L"Error setting the sink writer video input type.");
		videoType->Release();
		CHECK_HR(pWriter->SetInputMediaType(audioStreamIndex, audioType, NULL), L"Error setting the sink writer audio input type.");
		audioType->Release();*/

		//CHECK_HR(pWriter->BeginWriting(), L"Failed to begin writing on the H.264 sink.");

		//InitializeCriticalSection(&critsec);
	}
}

HRESULT InitVPXEncoder(vpx_codec_enc_cfg_t * vpxConfig, vpx_codec_ctx_t * vpxCodec, unsigned int width, unsigned int height)
{
	//vpx_codec_ctx_t      codec;

	vpx_codec_err_t res;

	printf("Using %s\n", vpx_codec_iface_name(vpx_codec_vp8_cx()));

	/* Populate encoder configuration */
	res = vpx_codec_enc_config_default((vpx_codec_vp8_cx()), vpxConfig, 0);

	if (res) {
		printf("Failed to get VPX codec config: %s\n", vpx_codec_err_to_string(res));
		return -1;
	}
	else {
		vpx_img_alloc(&_rawImage, VIDEO_INPUT_FORMAT, WIDTH, HEIGHT, 0);

		vpxConfig->g_w = width;
		vpxConfig->g_h = height;
		vpxConfig->rc_target_bitrate = 5000; // in kbps.
		vpxConfig->rc_min_quantizer = 20; // 50;
		vpxConfig->rc_max_quantizer = 30; // 60;
		vpxConfig->g_pass = VPX_RC_ONE_PASS;
		vpxConfig->rc_end_usage = VPX_CBR;
		//vpxConfig->kf_min_dist = 50;
		//vpxConfig->kf_max_dist = 50;
		//vpxConfig->kf_mode = VPX_KF_DISABLED;
		vpxConfig->g_error_resilient = VPX_ERROR_RESILIENT_DEFAULT;
		vpxConfig->g_lag_in_frames = 0;
		vpxConfig->rc_resize_allowed = 0;

		/* Initialize codec */
		if (vpx_codec_enc_init(vpxCodec, (vpx_codec_vp8_cx()), vpxConfig, 0)) {
			printf("Failed to initialize libvpx encoder.\n");
			return -1;
		}
		else {
			return S_OK;
		}
	}
}

HRESULT GetSampleFromMFStreamer(/* out */ const vpx_codec_cx_pkt_t *& vpkt)
{
	//printf("Get Sample...\n");

	IMFSample *videoSample = NULL;

	// Initial read results in a null pSample??
	CHECK_HR(videoReader->ReadSample(
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
		
		/*BYTE *i420 = new BYTE[4608000];
		YUY2ToI420(WIDTH, HEIGHT, STRIDE, imgBuff, i420);
		vpx_image_t* img = vpx_img_wrap(&_rawImage, VIDEO_INPUT_FORMAT, _vpxConfig.g_w, _vpxConfig.g_h, 1, i420);*/
		
		vpx_image_t* const img = vpx_img_wrap(&_rawImage, VIDEO_INPUT_FORMAT, _vpxConfig.g_w, _vpxConfig.g_h, 1, imgBuff);
		
		const vpx_codec_cx_pkt_t * pkt;
		vpx_enc_frame_flags_t flags = 0;
		
		if (vpx_codec_encode(&_vpxCodec, &_rawImage, _sampleCount, 1, flags, VPX_DL_REALTIME)) {
			printf("VPX codec failed to encode the frame.\n");
			return -1;
		}
		else {
			vpx_codec_iter_t iter = NULL;

			while ((pkt = vpx_codec_get_cx_data(&_vpxCodec, &iter))) {
				switch (pkt->kind) {
				case VPX_CODEC_CX_FRAME_PKT:                                
					vpkt = pkt; // const_cast<vpx_codec_cx_pkt_t **>(&pkt);
					break;
				default:
					break;
				}

				printf("%s %i\n", pkt->kind == VPX_CODEC_CX_FRAME_PKT && (pkt->data.frame.flags & VPX_FRAME_IS_KEY) ? "K" : ".", pkt->data.frame.sz);
			}
		}

		_sampleCount++;

		vpx_img_free(img);

		pMediaBuffer->Unlock();
		pMediaBuffer->Release();

		//delete i420;

		videoSample->Release();

		return S_OK;
	}
}

/*
List all the media modes available on the device.
*/
void FindVideoMode(IMFSourceReader *pReader, const GUID mediaSubType, int width, int height, /* out */ IMFMediaType *&foundpType)
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
			/*CMediaTypeTrace *nativeTypeMediaTrace = new CMediaTypeTrace(pType);
			printf("Native media type: %s.\n", nativeTypeMediaTrace->GetString());*/

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
}

/*
List all the media modes available on the device.
*/
void ListModes(IMFSourceReader *pReader)
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
			CMediaTypeTrace *nativeTypeMediaTrace = new CMediaTypeTrace(pType);
			printf("Native media type: %s.\n", nativeTypeMediaTrace->GetString());

			pType->Release();
		}
		++dwMediaTypeIndex;
	}
}

/*
Adds the video and audio stream to the H.264 writer sink.
*/
HRESULT ConfigureEncoder(IMFMediaType *pVideoType, DWORD *videoStreamIndex, DWORD *audioStreamIndex, IMFSinkWriter *pWriter)
{
	IMFMediaType *pVideoOutType = NULL;
	IMFMediaType *pAudioOutType = NULL;

	// Configure the video stream.
	CHECK_HR(MFCreateMediaType(&pVideoOutType), L"Configure encoder failed to create media type for video output sink.");
	CHECK_HR(pVideoOutType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), L"Failed to set video writer attribute, media type.");
	CHECK_HR(pVideoOutType->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264), L"Failed to set video writer attribute, video format (H.264).");
	CHECK_HR(pVideoOutType->SetUINT32(MF_MT_AVG_BITRATE, 240 * 1000), L"Failed to set video writer attribute, bit rate.");
	CHECK_HR(CopyAttribute(pVideoType, pVideoOutType, MF_MT_FRAME_SIZE), L"Failed to set video writer attribute, frame size.");
	CHECK_HR(CopyAttribute(pVideoType, pVideoOutType, MF_MT_FRAME_RATE), L"Failed to set video writer attribute, frame rate.");
	CHECK_HR(CopyAttribute(pVideoType, pVideoOutType, MF_MT_PIXEL_ASPECT_RATIO), L"Failed to set video writer attribute, aspect ratio.");
	CHECK_HR(CopyAttribute(pVideoType, pVideoOutType, MF_MT_INTERLACE_MODE), L"Failed to set video writer attribute, interlace mode.");;
	CHECK_HR(pWriter->AddStream(pVideoOutType, videoStreamIndex), L"Failed to add the video stream to the sink writer.");
	pVideoOutType->Release();

	// Configure the audio stream.
	// See http://msdn.microsoft.com/en-us/library/windows/desktop/dd742785(v=vs.85).aspx for AAC encoder settings.
	// http://msdn.microsoft.com/en-us/library/ff819476%28VS.85%29.aspx
	CHECK_HR(MFCreateMediaType(&pAudioOutType), L"Configure encoder failed to create media type for audio output sink.");
	CHECK_HR(pAudioOutType->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), L"Failed to set audio writer attribute, media type.");
	CHECK_HR(pAudioOutType->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC), L"Failed to set audio writer attribute, audio format (AAC).");
	CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2), L"Failed to set audio writer attribute, number of channels.");
	CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16), L"Failed to set audio writer attribute, bits per sample.");
	CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 44100), L"Failed to set audio writer attribute, samples per second.");
	CHECK_HR(pAudioOutType->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 16000), L"Failed to set audio writer attribute, average bytes per second.");
	//CHECK_HR( pAudioOutType->SetUINT32( MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29 ), L"Failed to set audio writer attribute, level indication.");
	CHECK_HR(pWriter->AddStream(pAudioOutType, audioStreamIndex), L"Failed to add the audio stream to the sink writer.");
	pAudioOutType->Release();

	return S_OK;
}

/*
Helper method to check a method call's HRESULT. If the result is a failure then this helper method will
cause the parent method to print out an error message and immediately return.
*/
BOOL HRHasFailed(HRESULT hr, WCHAR* errtext)
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

/*
Copies a media type attribute from an input media type to an output media type. Useful when setting
up the video sink and where a number of the video sink input attributes need to be duplicated on the
video writer attributes.
*/
HRESULT CopyAttribute(IMFAttributes *pSrc, IMFAttributes *pDest, const GUID& key)
{
	PROPVARIANT var;
	PropVariantInit(&var);

	HRESULT hr = S_OK;

	hr = pSrc->GetItem(key, &var);
	if (SUCCEEDED(hr))
	{
		hr = pDest->SetItem(key, var);
	}

	PropVariantClear(&var);
	return hr;
}

int YUY2ToI420(int width, int height, int stride, BYTE *in, BYTE *out)
{

	long pixels = width * height;
	long macropixels = pixels / 2; // macropixel count
	// new size will be w * h * 3/2 -> 12 bits per pixel 4:2:0

	//long mpx_per_row = info.biWidth / 2;
	long mpx_per_row = stride / 2;

	// for each macropixel
	for (int i = 0, ci = 0; i < macropixels; i++){ // ci is chroma index
		// get macropixel address, order is Y0 U0 Y1 V0
		BYTE *mpAddress = in + i * 4;

		// copy luma data
		out[i * 2] = mpAddress[0];
		out[i * 2 + 1] = mpAddress[2];

		// copy chroma data - we skip odd rows because of 4:2:0 sampling
		long row_number = i / mpx_per_row;
		if (row_number % 2 == 0) {
			out[pixels + ci] = mpAddress[1]; // shift by Y vector
			out[pixels + pixels / 4 + ci] = mpAddress[3]; // shift by Y and U vector
			ci++;
		}
	}

	return pixels * 12 / 8; // I420
}

HRESULT GetDefaultStride(IMFMediaType *pType, LONG *plStride)
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
