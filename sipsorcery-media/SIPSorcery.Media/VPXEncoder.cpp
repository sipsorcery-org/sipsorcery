#include "VPXEncoder.h"

namespace SIPSorceryMedia {

	VPXEncoder::VPXEncoder() 
		: _vpxCodec(0), _rawImage(0), _vpxDecoder(0)
	{ }

	VPXEncoder::~VPXEncoder()
	{
		if (_rawImage != NULL) {
			vpx_img_free(_rawImage);
		}

		if (_vpxCodec != NULL) {
			vpx_codec_destroy(_vpxCodec);
		}

		if (_vpxDecoder != NULL) {
			vpx_codec_destroy(_vpxDecoder);
		}
	}

	int VPXEncoder::InitEncoder(unsigned int width, unsigned int height, unsigned int stride)
	{
		_vpxCodec = new vpx_codec_ctx_t();
		_rawImage = new vpx_image_t();
		_width = width;
		_height = height;
    _stride = stride;

		vpx_codec_enc_cfg_t vpxConfig;
		vpx_codec_err_t res;

		printf("Using %s\n", vpx_codec_iface_name(vpx_codec_vp8_cx()));

		/* Populate encoder configuration */
		res = vpx_codec_enc_config_default((vpx_codec_vp8_cx()), &vpxConfig, 0);

		if (res) {
			printf("Failed to get VPX codec config: %s\n", vpx_codec_err_to_string(res));
			return -1;
		}
		else {
			vpx_img_alloc(_rawImage, VPX_IMG_FMT_I420, width, height, stride);

			vpxConfig.g_w = width;
			vpxConfig.g_h = height;
			vpxConfig.rc_target_bitrate = 300; // 5000; // in kbps.
			vpxConfig.rc_min_quantizer = 20; // 50;
			vpxConfig.rc_max_quantizer = 30; // 60;
			vpxConfig.g_pass = VPX_RC_ONE_PASS;
			vpxConfig.rc_end_usage = VPX_CBR;
			vpxConfig.g_error_resilient = VPX_ERROR_RESILIENT_DEFAULT;
			vpxConfig.g_lag_in_frames = 0;
			vpxConfig.rc_resize_allowed = 0;
			vpxConfig.kf_max_dist = 20;

			/* Initialize codec */
			if (vpx_codec_enc_init(_vpxCodec, (vpx_codec_vp8_cx()), &vpxConfig, 0)) {
				printf("Failed to initialize libvpx encoder.\n");
				return -1;
			}
		}
	}

	int VPXEncoder::InitDecoder()
	{
		_vpxDecoder = new vpx_codec_ctx_t();

		/* Initialize decoder */
		if (vpx_codec_dec_init(_vpxDecoder, (vpx_codec_vp8_dx()), NULL, 0)) {
			printf("Failed to initialize libvpx decoder.\n");
			return -1;
		}
	}

	int VPXEncoder::Encode(unsigned char * i420, int i420Length, int sampleCount, array<Byte> ^% buffer)
	{
		vpx_image_t* const img = vpx_img_wrap(_rawImage, VPX_IMG_FMT_I420, _width, _height, 1, i420);

		const vpx_codec_cx_pkt_t * pkt;
		vpx_enc_frame_flags_t flags = 0;

		if (vpx_codec_encode(_vpxCodec, _rawImage, sampleCount, 1, flags, VPX_DL_REALTIME)) {
			printf("VPX codec failed to encode the frame.\n");
			return -1;
		}
		else {
			vpx_codec_iter_t iter = NULL;

			while ((pkt = vpx_codec_get_cx_data(_vpxCodec, &iter))) {
				switch (pkt->kind) {
				case VPX_CODEC_CX_FRAME_PKT:
					//vpkt = const_cast<vpx_codec_cx_pkt_t **>(&pkt);
					//printf("%s %i\n", (pkt->data.frame.flags & VPX_FRAME_IS_KEY) ? "K" : ".", pkt->data.frame.sz);
					buffer = gcnew array<Byte>(pkt->data.raw.sz);
					Marshal::Copy((IntPtr)pkt->data.raw.buf, buffer, 0, pkt->data.raw.sz);
					break;
				default:
					break;
				}
			}
		}

		vpx_img_free(img);

		return 0;
	}

	int VPXEncoder::Decode(unsigned char* buffer, int bufferSize, array<Byte> ^% outBuffer, unsigned int % width, unsigned int % height)
	{
		vpx_codec_iter_t  iter = NULL;
		vpx_image_t      *img;

		/* Decode the frame */
		vpx_codec_err_t decodeResult = vpx_codec_decode(_vpxDecoder, (const uint8_t *)buffer, bufferSize, NULL, 0);

		if (decodeResult != VPX_CODEC_OK) {
			printf("VPX codec failed to decode the frame %s.\n", vpx_codec_err_to_string(decodeResult));
			return -1;
		}
		else {
			int pointer = 0;

			while ((img = vpx_codec_get_frame(_vpxDecoder, &iter))) {

				width = img->d_w;
				height = img->d_h;

				//int outputSize = img->stride[0] * img->d_w * 3 / 2;
				int outputSize = img->stride[0] * img->d_h * 3 / 2;
				unsigned char* bufferOut = (unsigned char *)malloc(outputSize);
				unsigned int plane, y;

				for (plane = 0; plane < 3; plane++) {                                

					unsigned char *buf = img->planes[plane];
				               
					for (y = 0; y < (plane ? (img->d_h + 1) >> 1 : img->d_h); y++) { 

						int numberofbitsToCopy = (plane ? (img->d_w + 1) >> 1 : img->d_w);
						memcpy(bufferOut + pointer, buf, numberofbitsToCopy);
						pointer += numberofbitsToCopy;

						buf += img->stride[plane];                                
					}                                                            
				}       

				outBuffer = gcnew array<Byte>(outputSize);
				Marshal::Copy((IntPtr)bufferOut, outBuffer, 0, outputSize);

				free(bufferOut);
				
				vpx_img_free(img);
				img = nullptr; 
			}
		}

		return 0;
	}
}
