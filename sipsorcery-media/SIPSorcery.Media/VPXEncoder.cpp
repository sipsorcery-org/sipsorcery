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

	//int VPXEncoder::Init(unsigned int width, unsigned int height)
	//{
	//	av_register_all();
	//	avcodec_register_all();

	//	_codec = avcodec_find_encoder(CODEC_ID_VP8);
	//	if (!_codec) {
	//		printf("Codec not found\n");
	//		return -1;
	//	}
	//	else {
	//		_codecContext = avcodec_alloc_context3(_codec);
	//		if (!_codecContext) {
	//			fprintf(stderr, "Could not allocate video codec context\n");
	//			return -1;
	//		}
	//		else {
	//			/* put sample parameters */
	//			//c->bit_rate = 400000;
	//			/* resolution must be a multiple of two */
	//			_codecContext->width = width;
	//			_codecContext->height = height;
	//			/* frames per second */
	//			//c->time_base = (AVRational){ 1, 25 };
	//			//c->gop_size = 10; /* emit one intra frame every ten frames */
	//			//c->max_b_frames = 1;
	//			_codecContext->pix_fmt = AV_PIX_FMT_YUV420P;

	//			if (avcodec_open2(_codecContext, _codec, NULL) < 0) {
	//				fprintf(stderr, "Could not open codec.\n");
	//				return -1;
	//			}
	//		}
	//	}

	//	return 0;
	//}

	int VPXEncoder::InitEncoder(unsigned int width, unsigned int height)
	{
		_vpxCodec = new vpx_codec_ctx_t();
		_rawImage = new vpx_image_t();
		_width = width;
		_height = height;

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
			vpx_img_alloc(_rawImage, VIDEO_INPUT_FORMAT, width, height, 0);

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
		vpx_image_t* const img = vpx_img_wrap(_rawImage, VIDEO_INPUT_FORMAT, _width, _height, 1, i420);

		const vpx_codec_cx_pkt_t * pkt;
		vpx_enc_frame_flags_t flags = 0;

		if (vpx_codec_encode(_vpxCodec, _rawImage, sampleCount, 1, flags, VPX_DL_REALTIME)) {
			printf("VPX codec failed to encode the frame.\n");
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

	int VPXEncoder::EncodeRGB24(unsigned char * bmp, int bmpLength, int sampleCount, array<Byte> ^% buffer)
	{
		unsigned char * yuv = new unsigned char[bmpLength];

		AVFrame * yuvFrame = ConvertToI420(_width, _height);

		//const vpx_codec_cx_pkt_t * vpkt;
		//vpx_image_t* const img = vpx_img_wrap(_rawImage, VIDEO_INPUT_FORMAT, _vpxConfig->g_w, _vpxConfig->g_h, 1, yuv);
		vpx_image_t* const img = vpx_img_wrap(_rawImage, VIDEO_INPUT_FORMAT, _width, _height, 1, (unsigned char *)yuvFrame->data[0]);

		const vpx_codec_cx_pkt_t * pkt;
		vpx_enc_frame_flags_t flags = 0;

		if (vpx_codec_encode(_vpxCodec, _rawImage, sampleCount, 1, flags, VPX_DL_REALTIME)) {
			printf("VPX codec failed to encode the frame.\n");
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
		delete[] yuv;

		return 0;
	}

	/*void VPXEncoder::Decode(unsigned char * buf, int bufLength, array<Byte> ^% buffer)
	{
		AVFrame * outFrame = av_frame_alloc();
		int gotPicture;
		AVPacket * inPacket = new AVPacket;
		av_new_packet(inPacket, bufLength);
		av_init_packet(inPacket);
		memcpy(inPacket->data, buf, bufLength);

		int decodeResult = avcodec_decode_video2(_codecContext, outFrame, &gotPicture, inPacket);

		if (decodeResult < 0) {
			fprintf(stderr, "Failed to decode video frame.\n");
		}
		else {
			printf("Video decode was successful decode result %i, got picture %i.\n", decodeResult, gotPicture);

			if (gotPicture != 0) {
				buffer = gcnew array<Byte>(outFrame->pkt_size);
				Marshal::Copy((IntPtr)outFrame->data[0], buffer, 0, outFrame->pkt_size);
			}
		}

		av_free(outFrame);
		av_free(inPacket->data);
		delete inPacket;
	}*/

	int VPXEncoder::Decode(unsigned char* buffer, int bufferSize, array<Byte> ^% outBuffer, unsigned int % width, unsigned int % height)
	{
		vpx_codec_iter_t  iter = NULL;
		vpx_image_t      *img;

		/* Decode the frame */
		vpx_codec_err_t decodeResult = vpx_codec_decode(_vpxDecoder, (const uint8_t *)buffer, bufferSize, NULL, 0);

		if (decodeResult != VPX_CODEC_OK) {
			printf("VPX codec failed to decode the frame %s.\n", vpx_codec_err_to_string(decodeResult));
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

				delete bufferOut;
			}
		}

		vpx_img_free(img);

		return 0;
	}

	AVFrame* VPXEncoder::ConvertToI420(int width, int height)
	{
		AVFormatContext *pFormatCtx = avformat_alloc_context();
		if (avformat_open_input(&pFormatCtx, "favicon.bmp", NULL, NULL) != 0) {
			printf("AVFormat couldn't open input file.\n");
		}

		av_dump_format(pFormatCtx, 0, NULL, 0);

		AVCodecContext* codecContext = pFormatCtx->streams[0]->codec;
		codecContext->codec = avcodec_find_decoder(codecContext->codec_id);

		if (codecContext->codec == NULL) {
			printf("Could not find the codec for the input file.\n");
		}
		else {
			printf("Codec for the input file %s.\n", codecContext->codec->name);
		}

		if (avcodec_open2(codecContext, codecContext->codec, NULL) < 0)
		{
			printf("Could not open codec.\n");
		}

		AVPacket avPacket;
		av_init_packet(&avPacket);
		AVFrame *pFrame;
		pFrame = av_frame_alloc();
		int frameFinished;

		// Read the packets in a loop
		while (av_read_frame(pFormatCtx, &avPacket) == 0)
		{
			int ret = avcodec_decode_video2(codecContext, pFrame, &frameFinished, &avPacket);
			if (ret > 0) {
				printf("Frame is decoded, size %d\n", ret);
				//pFrame->quality = 4;
				//return pFrame;
			}
			else {
				printf("Error [%d] while decoding frame.\n", ret);
			}
		}

		avformat_close_input(&pFormatCtx);

		SwsContext* swsContext = sws_getContext(width, height, AV_PIX_FMT_RGB24, width, height, AV_PIX_FMT_YUV420P, SWS_BILINEAR, NULL, NULL, NULL);

		AVFrame* frame2 = av_frame_alloc();
		int num_bytes = avpicture_get_size(AV_PIX_FMT_YUV420P, width, height);
		uint8_t* frame2_buffer = (uint8_t *)av_malloc(num_bytes*sizeof(uint8_t));
		avpicture_fill((AVPicture*)frame2, frame2_buffer, AV_PIX_FMT_YUV420P, width, height);

		sws_scale(swsContext, pFrame->data, pFrame->linesize, 0, height, frame2->data, frame2->linesize);

		return frame2;
	}
}
